using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ScreenWatch
{
    public static class Theme
    {
        public static Color Ink = Color.FromArgb(27,40,59), Accent = Color.FromArgb(0,126,112), Background = Color.FromArgb(244,247,250);
        public static Button Button(string caption, EventHandler click, bool primary = false)
        {
            var b = new Button { Text = caption, AutoSize = true, Height = 36, MinimumSize = new Size(95,36), FlatStyle = FlatStyle.Flat, BackColor = primary ? Accent : Color.White, ForeColor = primary ? Color.White : Ink, Margin = new Padding(0,0,10,0), Padding = new Padding(10,2,10,2), Cursor = Cursors.Hand };
            b.FlatAppearance.BorderColor = primary ? Accent : Color.FromArgb(212,221,230); if (click != null) b.Click += click; return b;
        }
        public static Label Label(string text) { return new Label { Text = text, AutoSize = true, ForeColor = Ink, Anchor = AnchorStyles.Left, Margin = new Padding(0,8,8,8) }; }
    }
    public sealed class Editor : Form
    {
        public MonitorConfig Result;
        readonly MainForm main;
        TextBox name;
        ComboBox rule, format;
        NumericUpDown lower, upper, interval, confirm, cooldown, index, unchanged;
        CheckBox sound, bark, systemNotification, repeat, invert, follow;
        Label region, preview;
        PictureBox picture;
        bool screenOperation, closing;
        bool CanUpdate { get { return !closing && !IsDisposed && !Disposing && !main.IsDisposed && !main.Disposing; } }
        public Editor(MainForm owner, MonitorConfig c)
        {
            main = owner; Result = c.Copy();
            Text = "设置数值监控"; Font = new Font("Microsoft YaHei UI",10); BackColor = Theme.Background; ForeColor = Theme.Ink;
            Size = new Size(760,820); MinimumSize = new Size(660,650); StartPosition = FormStartPosition.CenterParent; AutoScaleMode = AutoScaleMode.Dpi;
            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 64, Padding = new Padding(20,12,0,0), BackColor = Color.White };
            footer.Controls.Add(Theme.Button("保存监控",Save,true)); footer.Controls.Add(Theme.Button("取消",delegate { DialogResult = DialogResult.Cancel; Close(); }));
            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(24,16,24,16) };
            var table = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,150)); table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            name = new TextBox { Text = c.Name, Dock = DockStyle.Fill, MaxLength = 80 };
            AddRow(table,"监控名称",name);
            follow = new CheckBox { Text = "跟随所在窗口移动（关闭后需重新框选）", Checked = c.FollowWindow, AutoSize = true };
            follow.CheckedChanged += delegate { if (follow.Checked && Result.WindowHandle == 0) { MessageBox.Show(this,"请在勾选后重新框选区域，以绑定目标窗口。","提示"); } };
            var regionButtons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
            regionButtons.Controls.Add(Theme.Button("重新框选",async delegate { await SelectRegion(); }));
            regionButtons.Controls.Add(Theme.Button("试识别",async delegate { await Preview(); }));
            AddRow(table,"监控区域",regionButtons);
            region = Theme.Label(""); region.MaximumSize = new Size(490,0); AddRow(table,"当前位置",region); UpdateRegion();
            AddRow(table,"窗口模式",follow);
            picture = new PictureBox { Height = 84, Dock = DockStyle.Fill, BackColor = Color.FromArgb(226,233,239), SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle }; AddRow(table,"区域预览",picture);
            preview = Theme.Label("点击“试识别”检查选区；文字监控请框住所需文字。"); preview.MaximumSize = new Size(490,0); AddRow(table,"识别结果",preview);
            rule = Combo(Rules.Names,c.Rule); rule.SelectedIndexChanged += delegate { UpdateRule(); }; AddRow(table,"报警条件",rule);
            lower = Number(c.Lower,-1000000000000m,1000000000000m,6); upper = Number(c.Upper,-1000000000000m,1000000000000m,6);
            AddRow(table,"下限",lower); AddRow(table,"上限",upper); UpdateRule();
            AddRow(table,"边界说明",Theme.Label("大于 / 小于不含等于；区间内包含上下限。"));
            unchanged = Number(c.UnchangedSeconds,1,86400,0); unchanged.Name = "UnchangedSeconds"; AddRow(table,"不变时长（秒）",unchanged);
            var stableNote = Theme.Label("不变条件从首次识别开始计时，变化或识别失败后重计；\n达到时长后的下一次检测报警，不使用连续命中次数。\n文字忽略多余空白，区分大小写与标点。"); AddRow(table,"不变说明",stableNote);
            interval = Number(c.IntervalSeconds,1,3600,0); AddRow(table,"检测间隔（秒）",interval);
            confirm = Number(c.ConfirmCount,1,30,0); AddRow(table,"连续命中次数",confirm);
            cooldown = Number(c.CooldownSeconds,5,86400,0); AddRow(table,"报警冷却（秒）",cooldown);
            index = Number(c.NumberIndex,0,99,0); AddRow(table,"数字序号",index);
            AddRow(table,"序号说明",Theme.Label("0 = 必须只有一个数字；1、2… = 按阅读顺序取值。"));
            format = Combo(new[] { "小数点 . / 千位分隔 ,（如 1,234.56）", "小数逗号 , / 千位分隔 .（如 1.234,56）" },c.DecimalMode); AddRow(table,"数字格式",format);
            invert = Check("深色背景、浅色数字（反色增强）",c.Invert); AddRow(table,"识别增强",invert);
            sound = Check("电脑发出报警声",c.Sound); bark = Check("Bark 推送到手机",c.Bark);
            systemNotification = Check("Windows 系统通知",c.SystemNotification);
            var channels = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false }; channels.Controls.Add(sound); channels.Controls.Add(systemNotification); channels.Controls.Add(bark); AddRow(table,"报警方式",channels);
            repeat = Check("持续异常时，按冷却时间重复报警",c.Repeat); AddRow(table,"重复提醒",repeat);
            AddRow(table,"提醒说明",Theme.Label("未勾选时每轮异常提醒一次，恢复正常后重新布防。"));
            UpdateRule();
            scroll.Controls.Add(table); Controls.Add(scroll); Controls.Add(footer);
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (!e.Cancel) closing = true;
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && picture != null && picture.Image != null) { picture.Image.Dispose(); picture.Image = null; }
            base.Dispose(disposing);
        }
        async Task WithScreenUncovered(Func<Task> operation)
        {
            screenOperation = true;
            double editorOpacity = Opacity, mainOpacity = main.Opacity;
            bool wasEnabled = Enabled;
            try
            {
                // Hide() ends the ShowDialog loop; its caller then disposes this editor
                // while OCR is awaiting. Zero opacity uncovers the screen without ending it.
                Enabled = false; Opacity = 0; main.Opacity = 0;
                await Task.Delay(250);
                if (CanUpdate) await operation();
            }
            finally
            {
                screenOperation = false;
                if (!main.IsDisposed && !main.Disposing) main.Opacity = mainOpacity;
                if (CanUpdate) { Opacity = editorOpacity; Enabled = wasEnabled; Activate(); }
            }
        }
        static ComboBox Combo(string[] values, int selected) { var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill }; c.Items.AddRange(values); c.SelectedIndex = selected; return c; }
        static CheckBox Check(string text, bool value) { return new CheckBox { Text = text, Checked = value, AutoSize = true, Margin = new Padding(0,6,12,6) }; }
        static NumericUpDown Number(decimal value, decimal min, decimal max, int places) { return new NumericUpDown { Minimum = min, Maximum = max, DecimalPlaces = places, Value = Math.Min(max,Math.Max(min,value)), Width = 240, ThousandsSeparator = true }; }
        static void AddRow(TableLayoutPanel t, string label, Control c) { int r = t.RowCount++; t.RowStyles.Add(new RowStyle(SizeType.AutoSize)); t.Controls.Add(Theme.Label(label),0,r); c.Margin = new Padding(0,5,0,5); t.Controls.Add(c,1,r); }
        void UpdateRule()
        {
            bool stable = rule.SelectedIndex >= 4, text = rule.SelectedIndex == 5;
            if (lower != null) lower.Enabled = !stable && rule.SelectedIndex != 0;
            if (upper != null) upper.Enabled = !stable && rule.SelectedIndex != 1;
            if (unchanged != null) unchanged.Enabled = stable;
            if (confirm != null) confirm.Enabled = !stable;
            if (index != null) index.Enabled = !text;
            if (format != null) format.Enabled = !text;
        }
        void UpdateRegion() { region.Text = string.Format("X {0} · Y {1}    {2} × {3} 像素{4}",Result.X,Result.Y,Result.Width,Result.Height,string.IsNullOrEmpty(Result.WindowTitle) ? "" : "\n" + Result.WindowTitle); }
        void Pull()
        {
            Result.Name = name.Text.Trim(); Result.Rule = rule.SelectedIndex; Result.Lower = lower.Value; Result.Upper = upper.Value;
            Result.IntervalSeconds = (int)interval.Value; Result.ConfirmCount = (int)confirm.Value; Result.CooldownSeconds = (int)cooldown.Value;
            Result.NumberIndex = (int)index.Value; Result.DecimalMode = format.SelectedIndex; Result.Invert = invert.Checked;
            Result.Sound = sound.Checked; Result.Bark = bark.Checked; Result.Repeat = repeat.Checked; Result.FollowWindow = follow.Checked;
            Result.SystemNotification = systemNotification.Checked;
            Result.UnchangedSeconds = (int)unchanged.Value;
        }
        async Task SelectRegion()
        {
            await SelectRegion(delegate
            {
                using (var selection = new SelectionForm())
                    return selection.ShowDialog() == DialogResult.OK ? (Rectangle?)selection.SelectedRegion : null;
            },Native.Bind);
        }
        internal async Task SelectRegion(Func<Rectangle?> select,Action<MonitorConfig> bind)
        {
            if (!CanUpdate || screenOperation) return;
            try
            {
                await WithScreenUncovered(async delegate
                {
                    var selected = select();
                    if (!CanUpdate || !selected.HasValue) return;
                    var r = selected.Value; Result.X = r.X; Result.Y = r.Y; Result.Width = r.Width; Result.Height = r.Height;
                    Result.WindowHandle = 0; Result.WindowTitle = "";
                    if (follow.Checked) { await Task.Delay(150); if (!CanUpdate) return; bind(Result); }
                    UpdateRegion();
                });
            }
            catch (Exception ex) { if (CanUpdate) MessageBox.Show(this,ex.Message,"框选失败"); }
        }
        internal async Task Preview()
        {
            await Preview(() => Native.Capture(Native.Resolve(Result)),(shot,invert) => Rules.IsText(Result) ? new OcrReader().ReadText(shot,invert) : new OcrReader().Read(shot,invert));
        }
        internal async Task Preview(Func<Bitmap> capture,Func<Bitmap,bool,Task<string>> read)
        {
            if (!CanUpdate || screenOperation) return;
            Pull();
            Bitmap shot = null;
            try
            {
                await WithScreenUncovered(async delegate
                {
                    shot = capture();
                    var text = await read(shot,Result.Invert);
                    if (!CanUpdate) return;
                    MonitorReading reading; string error;
                    bool ok = MonitorReading.TryRead(Result,text,out reading,out error);
                    string state = Rules.IsUnchanged(Result) ? "开始监控后计时：" + Result.RuleText : (ok && Rules.Matches(Result,reading.Value) ? "满足报警条件" : "正常");
                    preview.Text = (ok ? (Rules.IsText(Result) ? "当前文字：" : "当前数值：") + reading.Display + "   ·   " + state : error) + "\n原文：" + text;
                    preview.ForeColor = ok ? Theme.Accent : Color.Firebrick;
                    if (picture.Image != null) picture.Image.Dispose(); picture.Image = shot; shot = null;
                });
            }
            catch (Exception ex) { if (CanUpdate) { preview.Text = ex.Message; preview.ForeColor = Color.Firebrick; } }
            finally { if (shot != null) shot.Dispose(); }
        }
        void Save(object sender, EventArgs args)
        {
            Pull();
            if (Result.Name.Length == 0) { MessageBox.Show(this,"请输入监控名称。"); return; }
            if (Result.Width < 8 || Result.Height < 8) { MessageBox.Show(this,"请先框选监控区域。"); return; }
            if ((Result.Rule == 2 || Result.Rule == 3) && Result.Lower >= Result.Upper) { MessageBox.Show(this,"区间下限必须小于上限。"); return; }
            if (Result.FollowWindow && Result.WindowHandle == 0) { MessageBox.Show(this,"请重新框选，绑定要跟随的窗口。"); return; }
            if (!Result.Sound && !Result.Bark && !Result.SystemNotification) { MessageBox.Show(this,"至少选择一种报警方式。"); return; }
            DialogResult = DialogResult.OK; Close();
        }
    }
    public sealed class BarkDialog : Form
    {
        public string Endpoint;
        public BarkDialog(string current)
        {
            Text = "Bark 通知设置"; Size = new Size(660,265); Font = new Font("Microsoft YaHei UI",10); StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; BackColor = Theme.Background;
            var label = new Label { Text = "粘贴 Bark 地址（完整示例链接也可以，推送内容会自动替换）", Left = 22, Top = 24, Width = 610, Height = 30 };
            var input = new TextBox { Text = current, Left = 22, Top = 64, Width = 596, UseSystemPasswordChar = true };
            var show = new CheckBox { Text = "显示地址", Left = 22, Top = 104, AutoSize = true }; show.CheckedChanged += delegate { input.UseSystemPasswordChar = !show.Checked; };
            var note = new Label { Text = "地址使用当前 Windows 账户加密保存；换电脑后需重新填写。", Left = 22, Top = 140, Width = 610, Height = 25 };
            var save = Theme.Button("保存",delegate { try { Endpoint = input.Text.Trim().Length == 0 ? "" : BarkClient.Normalize(input.Text); DialogResult = DialogResult.OK; Close(); } catch(Exception ex) { MessageBox.Show(this,ex.Message); } },true); save.Location = new Point(22,174);
            Controls.AddRange(new Control[] {label,input,show,note,save});
        }
    }
}

