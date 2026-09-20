using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace ScreenWatch
{
    public static class Native
    {
        [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(Point p);
        [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hwnd, StringBuilder s, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hwnd, StringBuilder s, int max);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint process);
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; public Rectangle Rect { get { return Rectangle.FromLTRB(Left, Top, Right, Bottom); } } }
        public static string Title(IntPtr h) { var b = new StringBuilder(512); GetWindowText(h, b, b.Capacity); return b.ToString(); }
        public static string ClassName(IntPtr h) { var b = new StringBuilder(256); GetClassName(h, b, b.Capacity); return b.ToString(); }
        public static long Started(int id) { try { using (var p = Process.GetProcessById(id)) return p.StartTime.ToUniversalTime().Ticks; } catch { return 0; } }
        public static void Bind(MonitorConfig c)
        {
            var p = new Point(c.X + c.Width / 2, c.Y + c.Height / 2);
            var h = GetAncestor(WindowFromPoint(p), 2); RECT r;
            if (h == IntPtr.Zero || !GetWindowRect(h, out r) || !r.Rect.Contains(c.Region)) throw new Exception("选区需要完全位于一个窗口内。");
            uint id; GetWindowThreadProcessId(h, out id);
            if (id == Process.GetCurrentProcess().Id) throw new Exception("请框选要监控的其他窗口。");
            c.WindowHandle = h.ToInt64(); c.WindowTitle = Title(h); c.WindowClass = ClassName(h); c.WindowProcess = (int)id; c.WindowStarted = Started((int)id);
            c.OffsetX = c.X - r.Left; c.OffsetY = c.Y - r.Top;
        }
        public static Rectangle Resolve(MonitorConfig c)
        {
            Rectangle result = c.Region;
            if (c.FollowWindow)
            {
                IntPtr h = new IntPtr(c.WindowHandle); uint pid; GetWindowThreadProcessId(h, out pid);
                if (!IsWindow(h) || pid != c.WindowProcess || ClassName(h) != c.WindowClass || (c.WindowStarted != 0 && Started((int)pid) != c.WindowStarted)) throw new Exception("目标窗口已关闭或更换，请重新框选");
                if (IsIconic(h) || !IsWindowVisible(h)) throw new Exception("目标窗口已最小化或隐藏");
                RECT r; if (!GetWindowRect(h, out r)) throw new Exception("无法读取目标窗口位置");
                result = new Rectangle(r.Left + c.OffsetX, r.Top + c.OffsetY, c.Width, c.Height);
                if (!r.Rect.Contains(result)) throw new Exception("窗口尺寸已改变，请重新框选");
                // Check the center and inset corners; a covered target must not read an unrelated window.
                foreach (var p in new[] { new Point(result.Left + result.Width / 2, result.Top + result.Height / 2), new Point(result.Left + 2, result.Top + 2), new Point(result.Right - 3, result.Bottom - 3) })
                    if (GetAncestor(WindowFromPoint(p), 2) != h) throw new Exception("监控区域被其他窗口遮挡");
            }
            if (result.Width < 8 || result.Height < 8 || !SystemInformation.VirtualScreen.Contains(result)) throw new Exception("监控区域不在屏幕范围内，请重新框选");
            return result;
        }
        public static Bitmap Capture(Rectangle region)
        {
            var b = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);
            try { using (var g = Graphics.FromImage(b)) g.CopyFromScreen(region.Location, Point.Empty, region.Size, CopyPixelOperation.SourceCopy); return b; }
            catch { b.Dispose(); throw; }
        }
    }
    public sealed class OcrReader
    {
        readonly OcrEngine engine;
        // Chinese OCR can render a short minus as a middle dot. Preserve only a
        // clearly isolated horizontal leading stroke, based on the actual pixels.
        static bool HasLeadingMinus(Bitmap bitmap)
        {
            var data = bitmap.LockBits(new Rectangle(0,0,bitmap.Width,bitmap.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
            try
            {
                byte[] pixels = new byte[data.Stride * data.Height]; Marshal.Copy(data.Scan0,pixels,0,pixels.Length);
                int[] top = Enumerable.Repeat(bitmap.Height,bitmap.Width).ToArray(), bottom = new int[bitmap.Width];
                int allTop = bitmap.Height, allBottom = -1;
                for(int y=0;y<bitmap.Height;y++) for(int x=0;x<bitmap.Width;x++)
                {
                    int at = y * data.Stride + x * 4;
                    if(pixels[at]+pixels[at+1]+pixels[at+2] < 420)
                    { top[x] = Math.Min(top[x],y); bottom[x] = y; allTop = Math.Min(allTop,y); allBottom = Math.Max(allBottom,y); }
                }
                int first = Array.FindIndex(top,y=>y<bitmap.Height); if(first < 0) return false;
                int end = first, t = bitmap.Height, b = 0;
                while(end<bitmap.Width && top[end]<bitmap.Height) { t=Math.Min(t,top[end]); b=Math.Max(b,bottom[end]); end++; }
                int width = end-first, height=b-t+1, totalHeight=allBottom-allTop+1;
                double center = (t+b)/2.0;
                return width >= height * 2.2 && width >= totalHeight * .18 && width <= totalHeight && height <= totalHeight * .24 && center > allTop+totalHeight*.25 && center < allTop+totalHeight*.78;
            }
            finally { bitmap.UnlockBits(data); }
        }
        static async Task<T> Complete<T>(Windows.Foundation.IAsyncOperation<T> operation)
        {
            try
            {
                var deadline = DateTime.UtcNow.AddSeconds(20);
                while (operation.Status == Windows.Foundation.AsyncStatus.Started)
                {
                    if (DateTime.UtcNow > deadline) { operation.Cancel(); throw new TimeoutException("OCR 响应超时，请暂停后重试"); }
                    await Task.Delay(15);
                }
                return operation.GetResults();
            }
            finally { operation.Close(); }
        }
        public OcrReader()
        {
            engine = OcrEngine.TryCreateFromUserProfileLanguages();
            if (engine == null && OcrEngine.AvailableRecognizerLanguages.Count > 0) engine = OcrEngine.TryCreateFromLanguage(OcrEngine.AvailableRecognizerLanguages[0]);
            if (engine == null) throw new Exception("Windows 尚未安装 OCR 语言包。请在系统 设置 → 时间和语言 → 语言 中安装中文或英文的“基本键入/光学字符识别”功能。");
        }
        static Bitmap Contrast(Bitmap source, bool invert, bool binary)
        {
            var result = new Bitmap(source.Width,source.Height,PixelFormat.Format32bppArgb);
            using(var g = Graphics.FromImage(result)) { g.Clear(Color.White); g.DrawImageUnscaled(source,0,0); }
            var data = result.LockBits(new Rectangle(Point.Empty,result.Size),ImageLockMode.ReadWrite,PixelFormat.Format32bppArgb);
            try
            {
                byte[] pixels = new byte[data.Stride*data.Height]; Marshal.Copy(data.Scan0,pixels,0,pixels.Length);
                for(int y=0;y<data.Height;y++) for(int x=0;x<result.Width;x++)
                {
                    int at=y*data.Stride+x*4;
                    // Preserve small, anti-aliased dots before enlargement. A cutoff
                    // of 200 includes faint edge pixels that the OCR otherwise drops.
                    int gray=(pixels[at]*114+pixels[at+1]*587+pixels[at+2]*299)/1000;
                    if(invert) gray=255-gray;
                    if(binary) gray=gray<200?0:255;
                    pixels[at]=pixels[at+1]=pixels[at+2]=(byte)gray; pixels[at+3]=255;
                }
                Marshal.Copy(pixels,0,data.Scan0,pixels.Length);
            }
            finally { result.UnlockBits(data); }
            return result;
        }
        static string NumericText(string text)
        {
            string normalized=(text??"").Normalize(NormalizationForm.FormKC).Trim().Replace('·','.').Replace('•','.').Replace('−','-').Replace('–','-');
            normalized=Regex.Replace(normalized,@"(?<=\d)[^\S\r\n]*([.,])[^\S\r\n]*(?=\d)","$1");
            normalized=Regex.Replace(normalized,@"^([+\-])[^\S\r\n]+(?=\d)","$1");
            return Regex.IsMatch(normalized,@"^[+\-]?(?:\d+(?:[.,]\d+)*|[.,]\d+)(?:[eE][+\-]?\d+)?$")?normalized:null;
        }
        public async Task<string> Read(Bitmap source, bool invert)
        {
            string enhanced=await RecognizePass(source,invert,true);
            string original=await RecognizePass(source,invert,false);
            string a=NumericText(enhanced), b=NumericText(original);
            if(a!=null && b!=null && a!=b)
            {
                decimal av,bv; string error;
                if(!Numbers.TryRead(a,0,0,out av,out error) || !Numbers.TryRead(b,0,0,out bv,out error) || av!=bv)
                    throw new InvalidOperationException("两次识别不一致（"+a+" / "+b+"），请缩小选区或放大原数字后试识别");
            }
            if(a!=null) return a;
            if(b!=null) return b;
            // Retry isolated numeric-looking glyph errors at another raster size.
            // Do not replace '&' with '8' in text: ask OCR to read the pixels again.
            if(Regex.IsMatch(enhanced??"",@"^[\d&.·•+\- \t]+$") && !string.IsNullOrWhiteSpace(enhanced))
            {
                string retry=await RecognizePass(source,invert,true,6.0);
                string number=NumericText(retry);
                if(number!=null) return number;
                if((enhanced??"").Contains("&")) throw new InvalidOperationException("数字字形不明确，请放大原数字或重新框选后试识别");
            }
            return string.IsNullOrWhiteSpace(enhanced)?original:enhanced;
        }
        public Task<string> ReadText(Bitmap source,bool invert)
        {
            // Text rules retain letters, punctuation and line content; numeric repairs do not apply.
            return RecognizePass(source,invert,false,3.0,false);
        }
        async Task<string> RecognizePass(Bitmap source, bool invert, bool binary, double requestedScale=3.0, bool numeric=true)
        {
            double scale = Math.Min(requestedScale, (OcrEngine.MaxImageDimension - 24.0) / Math.Max(source.Width, source.Height));
            int w = Math.Max(1, (int)(source.Width * scale)), h = Math.Max(1, (int)(source.Height * scale));
            using (var prepared = new Bitmap(w + 24, h + 24, PixelFormat.Format32bppArgb))
            using (var contrasted = Contrast(source,invert,binary))
            {
                using (var g = Graphics.FromImage(prepared))
                {
                    g.Clear(Color.White); g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(contrasted, new Rectangle(12,12,w,h), 0,0,source.Width,source.Height,GraphicsUnit.Pixel);
                }
                using (var bytes = new MemoryStream())
                using (var stream = new InMemoryRandomAccessStream())
                {
                    prepared.Save(bytes, ImageFormat.Png);
                    using (var writer = new DataWriter(stream.GetOutputStreamAt(0))) { writer.WriteBytes(bytes.ToArray()); await Complete<uint>(writer.StoreAsync()); await Complete<bool>(writer.FlushAsync()); writer.DetachStream(); }
                    stream.Seek(0);
                    var decoder = await Complete<BitmapDecoder>(BitmapDecoder.CreateAsync(stream));
                    using (var bitmap = await Complete<SoftwareBitmap>(decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied)))
                    {
                        var result = await Complete<OcrResult>(engine.RecognizeAsync(bitmap));
                        string text = string.Join("\n", result.Lines.Select(line => line.Text));
                        if (!numeric) return text;
                        if (result.Lines.Count == 1 && HasLeadingMinus(prepared))
                        {
                            text = text.TrimStart();
                            if (text.StartsWith("·") || text.StartsWith("•") || text.StartsWith(".")) text = "-" + text.Substring(1).TrimStart();
                            else if (text.Length > 0 && char.IsDigit(text[0])) text = "-" + text;
                        }
                        text=DigitGeometry.RestoreDecimal(prepared,text);
                        string number=NumericText(text);
                        if(number!=null && !DigitGeometry.CoversVisibleGlyphs(prepared,number)) return "";
                        return text;
                    }
                }
            }
        }
    }
    public sealed class SelectionForm : Form
    {
        readonly Bitmap screen;
        Point anchor;
        Rectangle selected;
        bool dragging;
        public Rectangle SelectedRegion { get; private set; }
        public SelectionForm()
        {
            var bounds = SystemInformation.VirtualScreen;
            screen = Native.Capture(bounds);
            AutoScaleMode = AutoScaleMode.None; FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual;
            Bounds = bounds; TopMost = true; ShowInTaskbar = false; DoubleBuffered = true; KeyPreview = true; Cursor = Cursors.Cross;
            KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); } };
            MouseDown += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Right) { DialogResult = DialogResult.Cancel; Close(); return; } anchor = e.Location; dragging = true; Capture = true; };
            MouseMove += delegate(object s, MouseEventArgs e) { if (!dragging) return; selected = Rectangle.FromLTRB(Math.Min(anchor.X,e.X), Math.Min(anchor.Y,e.Y), Math.Max(anchor.X,e.X), Math.Max(anchor.Y,e.Y)); Invalidate(); };
            MouseUp += delegate { if (!dragging) return; dragging = false; Capture = false; if (selected.Width < 8 || selected.Height < 8) { selected = Rectangle.Empty; Invalidate(); return; } SelectedRegion = new Rectangle(Left + selected.Left, Top + selected.Top, selected.Width, selected.Height); DialogResult = DialogResult.OK; Close(); };
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.DrawImageUnscaled(screen,0,0);
            using (var shade = new SolidBrush(Color.FromArgb(120,0,0,0))) e.Graphics.FillRectangle(shade,ClientRectangle);
            if (selected.Width > 0 && selected.Height > 0)
            {
                e.Graphics.DrawImage(screen,selected,selected,GraphicsUnit.Pixel);
                using (var pen = new Pen(Color.FromArgb(51,216,188),2)) e.Graphics.DrawRectangle(pen,selected);
            }
            string caption = "拖动框选数字区域 · 松开完成 · Esc / 右键取消";
            if (selected.Width > 0) caption += "    " + selected.Width + " × " + selected.Height;
            var cursor = PointToClient(Cursor.Position);
            var box = new Rectangle(Math.Max(8,Math.Min(ClientSize.Width - 650,cursor.X + 18)), Math.Max(8,Math.Min(ClientSize.Height - 46,cursor.Y + 24)), 630,34);
            using (var b = new SolidBrush(Color.FromArgb(240,22,31,46))) e.Graphics.FillRectangle(b,box);
            using(var font = new Font("Microsoft YaHei UI",10)) TextRenderer.DrawText(e.Graphics,caption,font,box,Color.White,TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
        }
        protected override void Dispose(bool disposing) { if (disposing) screen.Dispose(); base.Dispose(disposing); }
    }
}


