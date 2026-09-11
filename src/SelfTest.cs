using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace ScreenWatch
{
    public static class SelfTest
    {
        static readonly List<string> results = new List<string>();
        static void Check(bool condition,string name) { if(!condition) throw new Exception("FAIL: " + name); results.Add("PASS: " + name); }
        sealed class MockHandler : HttpMessageHandler
        {
            public string Body = "{\"code\":200}", RequestJson, RequestUrl;
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
            { RequestUrl = request.RequestUri.ToString(); RequestJson = await request.Content.ReadAsStringAsync(); return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Body) }; }
        }
        public static async Task Run(string output)
        {
            try
            {
                decimal v; string error;
                Check(Numbers.TryRead("温度 − 12.50 ℃",0,0,out v,out error) && v == -12.50m,"negative decimal and separated sign");
                Check(Numbers.TryRead("１，２３４．５６",0,0,out v,out error) && v == 1234.56m,"full width + thousands separator");
                Check(Numbers.TryRead(".75",0,0,out v,out error) && v == .75m,"leading decimal point");
                Check(Numbers.TryRead("1.25e-3",0,0,out v,out error) && v == .00125m,"scientific notation");
                Check(Numbers.TryRead("1.234,56",0,1,out v,out error) && v == 1234.56m,"comma decimal mode");
                Check(!Numbers.TryRead("12,34",0,0,out v,out error),"ambiguous comma rejected");
                Check(!Numbers.TryRead("no data",0,0,out v,out error),"missing number rejected");
                Check(!Numbers.TryRead("12 34",0,0,out v,out error),"multiple values rejected");
                Check(Numbers.TryRead("12\n34",2,0,out v,out error) && v == 34,"explicit index");
                Check(!Numbers.TryRead("12",2,0,out v,out error),"missing index rejected");
                Check(!Numbers.TryRead("1.2.3",0,0,out v,out error),"malformed decimal rejected");
                var c = new MonitorConfig { Lower = 10, Upper = 20, ConfirmCount = 2, CooldownSeconds = 60 };
                c.Rule = 0; Check(Rules.Matches(c,21) && !Rules.Matches(c,20),"greater than strict boundary");
                c.Rule = 1; Check(Rules.Matches(c,9) && !Rules.Matches(c,10),"less than strict boundary");
                c.Rule = 2; Check(Rules.Matches(c,9) && Rules.Matches(c,21) && !Rules.Matches(c,10) && !Rules.Matches(c,20),"outside interval boundaries");
                c.Rule = 3; Check(Rules.Matches(c,10) && Rules.Matches(c,20) && !Rules.Matches(c,21),"inside interval boundaries");
                c.Rule = 0; var state = new AlarmState(); var now = DateTime.UtcNow;
                Check(!state.Observe(c,21,now) && state.Observe(c,21,now.AddSeconds(2)),"consecutive confirmation");
                Check(!state.Observe(c,21,now.AddSeconds(90)),"one alert per excursion");
                state.Observe(c,15,now.AddSeconds(92)); state.Observe(c,21,now.AddSeconds(94));
                Check(state.Observe(c,21,now.AddSeconds(96)),"rearm after normal reading");
                state.Observe(c,15,now.AddSeconds(97)); state.Observe(c,21,now.AddSeconds(98));
                Check(!state.Observe(c,21,now.AddSeconds(99)),"cooldown across excursions");
                Check(state.Observe(c,21,now.AddSeconds(156)),"delayed excursion alerts at cooldown expiry");
                c.Repeat = true;
                Check(!state.Observe(c,21,now.AddSeconds(160)) && state.Observe(c,21,now.AddSeconds(216)),"repeating alert cooldown");
                state = new AlarmState(); state.Observe(c,21,now); state.Invalid(); Check(!state.Observe(c,21,now.AddSeconds(3)),"invalid reading resets confirmation");
                Check(BarkClient.Normalize("https://api.day.app/test-key/任意内容") == "https://api.day.app/test-key","Bark sample content stripped");
                bool rejected = false; try { BarkClient.Normalize("http://api.day.app/key"); } catch { rejected = true; } Check(rejected,"HTTPS required");
                var handler = new MockHandler();
                using(var client = new BarkClient(handler))
                {
                    await client.Send("https://api.day.app/test-key","测试","数值 21\n报警",CancellationToken.None);
                    var body = new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(handler.RequestJson);
                    Check(handler.RequestUrl == "https://api.day.app/push" && Convert.ToString(body["device_key"]) == "test-key" && Convert.ToString(body["body"]) == "数值 21\n报警","Bark POST route / JSON body (mock, no network)");
                    handler.Body = "{\"code\":400}"; rejected = false; try { await client.Send("https://api.day.app/key","a","b",CancellationToken.None); } catch { rejected = true; } Check(rejected,"Bark API errors not reported as success");
                    handler.Body = "invalid"; rejected = false; try { await client.Send("https://api.day.app/key","a","b",CancellationToken.None); } catch { rejected = true; } Check(rejected,"Bark malformed response rejected");
                }
                Check(Settings.Decrypt(Settings.Encrypt("secret-test")) == "secret-test","Windows DPAPI roundtrip");
                string previous = Settings.FilePath;
                try
                {
                    Settings.FilePath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output)),"test-settings.xml");
                    var data = new AppConfig(); data.Monitors.Add(c); data.BarkProtected = Settings.Encrypt("test-only"); Settings.Save(data); Settings.Save(data);
                    var loaded = Settings.Load(); Check(loaded.Monitors.Count == 1 && loaded.Monitors[0].Upper == 20 && Settings.Decrypt(loaded.BarkProtected) == "test-only","atomic settings roundtrip / backup");
                    File.Delete(Settings.FilePath); File.Delete(Settings.FilePath + ".bak");
                }
                finally { Settings.FilePath = previous; }
                var reader = new OcrReader();
                string regression=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"tests","decimal-regression.png");
                using(var sample=new Bitmap(regression))
                {
                    string text=await reader.Read(sample,false);
                    Check(Numbers.TryRead(text,0,0,out v,out error) && v==8.97m,"user screenshot 8.97: "+text);
                    using(var dark=new Bitmap(sample.Width,sample.Height))
                    {
                        for(int y=0;y<sample.Height;y++)for(int x=0;x<sample.Width;x++){var pixel=sample.GetPixel(x,y);dark.SetPixel(x,y,Color.FromArgb(255-pixel.R,255-pixel.G,255-pixel.B));}
                        text=await reader.Read(dark,true);
                        Check(Numbers.TryRead(text,0,0,out v,out error) && v==8.97m,"user screenshot inverted: "+text);
                    }
                }
                foreach(string family in new[]{"Arial","Times New Roman"})
                foreach(string number in new[]{"8.97","97","897","-8.97"})
                using(var sample=new Bitmap(180,50))
                {
                    using(var g=Graphics.FromImage(sample)) { g.Clear(Color.White); using(var font=new Font(family,24,FontStyle.Regular,GraphicsUnit.Pixel)) g.DrawString(number,font,Brushes.Black,8,8); }
                    string text=await reader.Read(sample,false);
                    Check(Numbers.TryRead(text,0,0,out v,out error) && v==decimal.Parse(number,System.Globalization.CultureInfo.InvariantCulture),"OCR "+family+" expected "+number+": "+text);
                }
                using(var sample=new Bitmap(240,60))
                {
                    using(var g=Graphics.FromImage(sample)){g.Clear(Color.White);using(var font=new Font("Arial",24,FontStyle.Regular,GraphicsUnit.Pixel)){g.DrawString("12",font,Brushes.Black,8,8);g.DrawString("34",font,Brushes.Black,130,8);}}
                    string text=await reader.Read(sample,false);
                    Check(!Numbers.TryRead(text,0,0,out v,out error),"separate OCR values never concatenated: "+text);
                }
                using(var bitmap = new Bitmap(340,85))
                {
                    using(var g = Graphics.FromImage(bitmap)) { g.Clear(Color.White); using(var f = new Font("Arial",36)) g.DrawString("123.45",f,Brushes.Black,12,10); }
                    var text = await reader.Read(bitmap,false);
                    Check(Numbers.TryRead(text,0,0,out v,out error) && v == 123.45m,"Windows OCR rendered decimal: " + text);
                }
                using(var bitmap = new Bitmap(340,85))
                {
                    using(var g = Graphics.FromImage(bitmap)) { g.Clear(Color.FromArgb(20,20,20)); using(var f = new Font("Arial",36)) g.DrawString("-67.89",f,Brushes.White,12,10); }
                    var text = await reader.Read(bitmap,true);
                    Check(Numbers.TryRead(text,0,0,out v,out error) && v == -67.89m,"Windows OCR inverted negative: " + text);
                }
                Exception uiError = null;
                var thread = new Thread(delegate()
                {
                    try
                    {
                        var demo = new AppConfig(); demo.Monitors.Add(new MonitorConfig { Name = "温度监控（界面示例）", Width = 180, Height = 64, Upper = 80 });
                        using(var form = new MainForm(demo))
                        using(var image = new Bitmap(form.Width,form.Height))
                        {
                            form.StartPosition = System.Windows.Forms.FormStartPosition.Manual; form.Location = new Point(-30000,-30000); form.Show(); System.Windows.Forms.Application.DoEvents();
                            form.DrawToBitmap(image,new Rectangle(Point.Empty,form.Size)); image.Save(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output)),"界面预览.png"),ImageFormat.Png);
                            using(var editor = new Editor(form,demo.Monitors[0]))
                            {
                                editor.StartPosition = System.Windows.Forms.FormStartPosition.Manual; editor.Location = new Point(-30000,-30000); editor.Show(); System.Windows.Forms.Application.DoEvents();
                                using(var editorImage = new Bitmap(editor.Width,editor.Height)) { editor.DrawToBitmap(editorImage,new Rectangle(Point.Empty,editor.Size)); editorImage.Save(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output)),"设置预览.png"),ImageFormat.Png); }
                                editor.Close();
                            }
                            form.Close();
                        }
                    }
                    catch(Exception ex) { uiError = ex; }
                });
                thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(); Check(uiError == null,"WinForms UI construction and preview" + (uiError == null ? "" : ": " + uiError));
                await LiveCaptureTest();
                results.Add("COMPLETED · " + results.FindAll(x=>x.StartsWith("PASS:")).Count + " passed · " + results.FindAll(x=>x.StartsWith("SKIP:")).Count + " skipped");
            }
            catch(Exception ex) { results.Add(ex.ToString()); Environment.ExitCode = 1; }
            File.WriteAllLines(output,results,Encoding.UTF8);
        }
        static Task LiveCaptureTest()
        {
            var completion = new TaskCompletionSource<bool>();
            var thread = new Thread(delegate()
            {
                using(var fixture = new System.Windows.Forms.Form { Text = "屏幕监控自动验证", BackColor = Color.White, ClientSize = new Size(400,160), StartPosition = System.Windows.Forms.FormStartPosition.Manual, Location = new Point(50,80), TopMost = true })
                {
                    fixture.Paint += delegate(object sender,System.Windows.Forms.PaintEventArgs e) { using(var font = new Font("Arial",36)) e.Graphics.DrawString("123.45",font,Brushes.Black,20,20); };
                    fixture.Shown += async delegate
                    {
                        try
                        {
                            await Task.Delay(250);
                            var rect = fixture.RectangleToScreen(new Rectangle(10,10,330,90));
                            var c = new MonitorConfig { X = rect.X,Y = rect.Y,Width = rect.Width,Height = rect.Height };
                            decimal value; string error;
                            try
                            {
                                using(var screenshot = Native.Capture(Native.Resolve(c)))
                                {
                                    string text = await new OcrReader().Read(screenshot,false);
                                    Check(Numbers.TryRead(text,0,0,out value,out error) && value == 123.45m,"live screen capture to OCR: " + text);
                                }
                            }
                            catch(System.ComponentModel.Win32Exception ex) { results.Add("SKIP: live screen capture / move / cover / minimize — desktop capture unavailable in this execution session: " + ex.NativeErrorCode); completion.SetResult(true); return; }
                            Native.RECT wr; Native.GetWindowRect(fixture.Handle,out wr); uint pid; Native.GetWindowThreadProcessId(fixture.Handle,out pid);
                            c.FollowWindow = true; c.WindowHandle = fixture.Handle.ToInt64(); c.WindowProcess = (int)pid; c.WindowClass = Native.ClassName(fixture.Handle); c.WindowStarted = Native.Started((int)pid); c.OffsetX = rect.X - wr.Left; c.OffsetY = rect.Y - wr.Top;
                            fixture.Left += 30; fixture.Top += 20; await Task.Delay(100);
                            var moved = Native.Resolve(c); Check(moved.X == rect.X+30 && moved.Y == rect.Y+20,"follow window movement");
                            using(var cover = new System.Windows.Forms.Form { StartPosition = System.Windows.Forms.FormStartPosition.Manual, Bounds = moved, TopMost = true, FormBorderStyle = System.Windows.Forms.FormBorderStyle.None })
                            {
                                cover.Show(); await Task.Delay(100); bool rejected = false; try { Native.Resolve(c); } catch { rejected = true; } Check(rejected,"covered target rejected"); cover.Close();
                            }
                            fixture.WindowState = System.Windows.Forms.FormWindowState.Minimized; await Task.Delay(100);
                            bool minimized = false; try { Native.Resolve(c); } catch { minimized = true; } Check(minimized,"minimized target rejected");
                            completion.SetResult(true);
                        }
                        catch(Exception ex) { completion.SetException(ex); }
                        finally { fixture.Close(); }
                    };
                    System.Windows.Forms.Application.Run(fixture);
                }
            });
            thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
        }
    }
}

