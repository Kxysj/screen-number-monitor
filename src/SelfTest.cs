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
using System.Windows.Forms;

namespace ScreenWatch
{
    public static class SelfTest
    {
        static readonly List<string> results = new List<string>();
        static void Check(bool condition,string name) { if(!condition) throw new Exception("FAIL: " + name); results.Add("PASS: " + name); }
        static Control FindControl(Control parent,string text)
        {
            if(parent.Text == text) return parent;
            foreach(Control child in parent.Controls) { var found = FindControl(child,text); if(found != null) return found; }
            return null;
        }
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
                UnchangedAlarmTests();
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
                    c.Sound = false; c.Bark = false; c.SystemNotification = true; Settings.Save(data);
                    loaded = Settings.Load(); Check(loaded.Monitors[0].SystemNotification && !loaded.Monitors[0].Sound && !loaded.Monitors[0].Bark,"system notification only settings roundtrip");
                    c.SystemNotification = false; Settings.Save(data);
                    Check(!Settings.Load().Monitors[0].SystemNotification,"system notification disabled survives reload");
                    File.WriteAllText(Settings.FilePath,File.ReadAllText(Settings.FilePath).Replace("<SystemNotification>false</SystemNotification>",""));
                    Check(!Settings.Load().Monitors[0].SystemNotification,"legacy settings preserve existing notification choices");
                    c.Rule = 5; c.UnchangedSeconds = 17; Settings.Save(data);
                    loaded = Settings.Load(); Check(loaded.Monitors[0].Rule == 5 && loaded.Monitors[0].UnchangedSeconds == 17,"text unchanged duration settings roundtrip");
                    File.WriteAllText(Settings.FilePath,File.ReadAllText(Settings.FilePath).Replace("<UnchangedSeconds>17</UnchangedSeconds>",""));
                    Check(Settings.Load().Monitors[0].UnchangedSeconds == 60,"legacy settings default unchanged duration");
                    File.Delete(Settings.FilePath); File.Delete(Settings.FilePath + ".bak");
                }
                finally { Settings.FilePath = previous; }
                string longTitle = new string('名',61) + "\U0001F514" + "通知";
                Check(SystemNotificationClient.Limit(longTitle,63) == new string('名',61) + "…","long notification title does not split emoji");
                Check(SystemNotificationClient.Limit(new string('值',300),255).Length == 255 && SystemNotificationClient.Limit("a\0b",63) == "a b","notification text respects Windows limits");
                var reader = new OcrReader();
                foreach(string label in new[]{"READY 42","运行正常"})
                using(var sample = new Bitmap(350,80))
                {
                    using(var g = Graphics.FromImage(sample)) { g.Clear(Color.White); using(var font = new Font("Microsoft YaHei UI",28)) g.DrawString(label,font,Brushes.Black,8,8); }
                    string text = await reader.ReadText(sample,false);
                    Check(text.Replace(" ","").Replace("\n","").Trim() == label.Replace(" ",""),"Windows text OCR preserves words: " + text);
                }
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
                                ((CheckBox)FindControl(editor,"电脑发出报警声")).Checked = false;
                                ((CheckBox)FindControl(editor,"Bark 推送到手机")).Checked = false;
                                ((CheckBox)FindControl(editor,"Windows 系统通知")).Checked = true;
                                ((Button)FindControl(editor,"保存监控")).PerformClick();
                                Check(editor.DialogResult == DialogResult.OK && editor.Result.SystemNotification && !editor.Result.Sound && !editor.Result.Bark,"editor accepts Windows notifications as the only alarm channel");
                            }
                            Check(FindControl(form,"测试系统通知") != null,"system notification test action available without Bark");
                            form.Close();
                        }
                    }
                    catch(Exception ex) { uiError = ex; }
                });
                thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(); Check(uiError == null,"WinForms UI construction and preview" + (uiError == null ? "" : ": " + uiError));
                await ModalEditorTest();
                await LiveCaptureTest();
                results.Add("COMPLETED · " + results.FindAll(x=>x.StartsWith("PASS:")).Count + " passed · " + results.FindAll(x=>x.StartsWith("SKIP:")).Count + " skipped");
            }
            catch(Exception ex) { results.Add(ex.ToString()); Environment.ExitCode = 1; }
            File.WriteAllLines(output,results,Encoding.UTF8);
        }
        static MonitorReading Reading(MonitorConfig c,string text)
        {
            MonitorReading reading; string error;
            if(!MonitorReading.TryRead(c,text,out reading,out error)) throw new Exception(error);
            return reading;
        }
        static void UnchangedAlarmTests()
        {
            var c = new MonitorConfig { Rule = 4, UnchangedSeconds = 10, ConfirmCount = 30, IntervalSeconds = 2, CooldownSeconds = 5 };
            var now = DateTime.UtcNow; var state = new AlarmState();
            Check(!state.Observe(c,Reading(c,"8.0"),now),"unchanged timer begins at first valid reading");
            Check(!state.Observe(c,Reading(c,"8.00"),now.AddSeconds(9.999)),"unchanged value does not alert before duration");
            Check(state.Observe(c,Reading(c,"8"),now.AddSeconds(10)),"equal numeric formats alert at duration without extra confirmation hits");
            Check(!state.Observe(c,Reading(c,"8"),now.AddSeconds(20)),"unchanged one-shot alert is not repeated");
            Check(!state.Observe(c,Reading(c,"9"),now.AddSeconds(21)) && !state.Active && state.UnchangedElapsed == 0,"numeric change rearms and restarts duration");
            Check(!state.Observe(c,Reading(c,"9"),now.AddSeconds(30)) && state.Observe(c,Reading(c,"9"),now.AddSeconds(31)),"changed numeric content must complete its own duration");
            c.Repeat = true;
            Check(!state.Observe(c,Reading(c,"9"),now.AddSeconds(35)) && state.Observe(c,Reading(c,"9"),now.AddSeconds(36)),"unchanged repeats respect cooldown");
            state.Invalid();
            Check(!state.Observe(c,Reading(c,"9"),now.AddSeconds(40)) && state.UnchangedElapsed == 0 && !state.Active,"failed capture/parse or pause resets unchanged baseline");
            Check(!state.Observe(c,Reading(c,"9"),now.AddSeconds(49)) && state.Observe(c,Reading(c,"9"),now.AddSeconds(50)),"unchanged resumes only after full valid duration");
            Check(!state.Observe(c,Reading(c,"9"),now.AddSeconds(100)) && state.UnchangedElapsed == 0,"long unobserved gap is not counted as unchanged");
            Check(!state.Observe(c,Reading(c,"9"),now.AddSeconds(99)) && state.UnchangedElapsed == 0,"backwards time restarts unchanged observation");
            c.CooldownSeconds = 60; c.Repeat = false; state = new AlarmState();
            state.Observe(c,Reading(c,"1"),now); state.Observe(c,Reading(c,"1"),now.AddSeconds(10));
            state.Observe(c,Reading(c,"2"),now.AddSeconds(11));
            Check(!state.Observe(c,Reading(c,"2"),now.AddSeconds(21)) && state.UnchangedExpired && !state.Active,"new stable content still respects previous alarm cooldown");
            state.Observe(c,Reading(c,"2"),now.AddSeconds(40)); state.Observe(c,Reading(c,"2"),now.AddSeconds(60));
            Check(state.Observe(c,Reading(c,"2"),now.AddSeconds(70)),"stable content alerts once cooldown expires");
            c.Rule = 5; c.CooldownSeconds = 5; state = new AlarmState();
            Check(!state.Observe(c,Reading(c,"  运行\r\n正常  "),now) && state.Observe(c,Reading(c,"运行 正常"),now.AddSeconds(10)),"text unchanged supports nonnumeric words and normalizes whitespace");
            Check(!state.Observe(c,Reading(c,"运行 异常"),now.AddSeconds(11)) && !state.UnchangedExpired,"text change restarts unchanged timer");
            MonitorReading empty; string error;
            Check(!MonitorReading.TryRead(c," \r\n ",out empty,out error),"blank text is an invalid observation, not stable content");
            Check(Reading(c,"READY").Identity != Reading(c,"ready").Identity && Reading(c,"正常。").Identity != Reading(c,"正常").Identity,"text comparison preserves case and punctuation");
            Check(Reading(c,"8.0").Identity != Reading(c,"8.00").Identity,"text mode retains numeric formatting");
            var a = new AlarmState(); var b = new AlarmState();
            a.Observe(c,Reading(c,"OK"),now); b.Observe(c,Reading(c,"OK"),now.AddSeconds(5));
            Check(a.Observe(c,Reading(c,"OK"),now.AddSeconds(10)) && !b.Observe(c,Reading(c,"OK"),now.AddSeconds(10)),"multiple monitors have independent unchanged timers");
            c.Rule = 4;
            Check(!MonitorReading.TryRead(c,"状态正常",out empty,out error),"numeric unchanged still rejects missing numbers");
            Check(Rules.Describe(c).Contains("10 秒"),"unchanged duration is included in alarm condition");
        }
        static async Task RunEditorDialog(MainForm main,Func<Editor,Task> scenario,bool allowEarlyClose = false,MonitorConfig config = null)
        {
            var completed = new TaskCompletionSource<Exception>();
            bool returnedEarly;
            using(var editor = new Editor(main,config ?? new MonitorConfig { Name = "回归监控", Width = 180, Height = 64 }))
            {
                editor.StartPosition = FormStartPosition.Manual; editor.Location = new Point(-30000,-30000);
                editor.Shown += async delegate
                {
                    Exception failure = null;
                    try { await scenario(editor); }
                    catch(Exception ex) { failure = ex; }
                    finally
                    {
                        completed.TrySetResult(failure);
                        if(!editor.IsDisposed) editor.Close();
                    }
                };
                // Same ShowDialog + using lifetime as MainForm.Add/Edit, rather than Show().
                editor.ShowDialog(main);
                returnedEarly = !completed.Task.IsCompleted;
            }
            Exception error = await completed.Task;
            if(error != null) throw error;
            if(!allowEarlyClose) Check(!returnedEarly,"modal editor remains alive until screen operation completes");
        }
        static void CheckEditorRestored(Editor editor,MainForm main,string name)
        {
            Check(!editor.IsDisposed && editor.Modal && editor.Visible && editor.Enabled && editor.Opacity == 1 && main.Opacity == 1,name);
        }
        static Task ModalEditorTest()
        {
            var completion = new TaskCompletionSource<bool>();
            var thread = new Thread(delegate()
            {
                try
                {
                    using(var main = new MainForm(new AppConfig()))
                    {
                        main.StartPosition = FormStartPosition.Manual; main.Location = new Point(-30000,-30000);
                        main.Shown += async delegate
                        {
                            try
                            {
                                await RunEditorDialog(main,async editor =>
                                {
                                    for(int i=0;i<2;i++)
                                    {
                                        await editor.Preview(() => new Bitmap(180,64),async (bitmap,invert) =>
                                        {
                                            await Task.Delay(40);
                                            Check(editor.Modal && editor.Visible && !editor.IsDisposed && editor.Opacity == 0 && main.Opacity == 0,"preview uncovers screen without ending modal dialog");
                                            await editor.Preview(() => { throw new Exception("reentrant preview must not capture"); },(b,v) => Task.FromResult("0"));
                                            return "8.97";
                                        });
                                        CheckEditorRestored(editor,main,"successful/repeated preview restores editor");
                                        Check(FindControl(editor,"当前数值：8.97   ·   正常\n原文：8.97") != null,"preview displays parsed decimal result");
                                    }
                                    ((Button)FindControl(editor,"保存监控")).PerformClick();
                                    Check(editor.DialogResult == DialogResult.OK && editor.Result.Width == 180,"monitor can be saved after preview");
                                });
                                await RunEditorDialog(main,async editor =>
                                {
                                    await editor.Preview(() => { throw new InvalidOperationException("模拟截图失败"); },(b,v) => Task.FromResult("0"));
                                    CheckEditorRestored(editor,main,"capture failure restores modal editor");
                                    Check(FindControl(editor,"模拟截图失败") != null,"capture error shown inside editor");
                                    await editor.Preview(() => new Bitmap(180,64),async (b,v) => { await Task.Delay(40); throw new InvalidOperationException("模拟 OCR 失败"); });
                                    CheckEditorRestored(editor,main,"asynchronous OCR failure restores modal editor");
                                    Check(FindControl(editor,"模拟 OCR 失败") != null,"OCR error shown inside editor");
                                    await editor.Preview(() => new Bitmap(180,64),(b,v) => Task.FromResult("8.97"));
                                    CheckEditorRestored(editor,main,"preview can be retried after failure");
                                });
                                await RunEditorDialog(main,async editor =>
                                {
                                    var original = editor.Result.Region;
                                    await editor.SelectRegion(() => null,c => { throw new Exception("unexpected bind"); });
                                    CheckEditorRestored(editor,main,"cancelling reselection restores modal editor");
                                    Check(editor.Result.Region == original,"cancelling reselection keeps previous region");
                                    ((CheckBox)FindControl(editor,"跟随所在窗口移动（关闭后需重新框选）")).Checked = false;
                                    var selected = new Rectangle(30,40,200,80);
                                    await editor.SelectRegion(() => selected,c => { throw new Exception("unexpected bind"); });
                                    CheckEditorRestored(editor,main,"successful reselection restores modal editor");
                                    Check(editor.Result.Region == selected,"successful reselection updates region");
                                    await editor.Preview(() => new Bitmap(200,80),(b,v) => Task.FromResult("8.97"));
                                    CheckEditorRestored(editor,main,"preview works after reselection");
                                });
                                await RunEditorDialog(main,async editor =>
                                {
                                    var ruleChoice = (ComboBox)FindControl(editor,"大于上限");
                                    ruleChoice.SelectedIndex = 5;
                                    var seconds = (NumericUpDown)editor.Controls.Find("UnchangedSeconds",true)[0]; seconds.Value = 17;
                                    Check(seconds.Enabled,"unchanged duration enabled for text rule");
                                    await editor.Preview(() => new Bitmap(180,64),(b,v) => Task.FromResult("运行正常"));
                                    CheckEditorRestored(editor,main,"text preview preserves modal editor");
                                    Check(FindControl(editor,"当前文字：运行正常   ·   开始监控后计时：文字不变 ≥ 17 秒\n原文：运行正常") != null,"text preview works without numbers and does not claim duration already met");
                                    ((Button)FindControl(editor,"保存监控")).PerformClick();
                                    Check(editor.DialogResult == DialogResult.OK && editor.Result.Rule == 5 && editor.Result.UnchangedSeconds == 17,"text unchanged rule saves despite unused inverted thresholds");
                                },false,new MonitorConfig { Name = "文字监控", Width = 180, Height = 64, Lower = 100, Upper = 0 });
                                foreach(bool fail in new[]{false,true})
                                {
                                    await RunEditorDialog(main,async editor =>
                                    {
                                        await editor.Preview(() => new Bitmap(180,64),async (b,v) =>
                                        {
                                            editor.Dispose();
                                            await Task.Delay(40);
                                            if(fail) throw new InvalidOperationException("late OCR failure");
                                            return "8.97";
                                        });
                                        Check(editor.IsDisposed && main.Opacity == 1,"late OCR completion/failure does not revive disposed editor");
                                    },true);
                                }
                                completion.TrySetResult(true);
                            }
                            catch(Exception ex) { completion.TrySetException(ex); }
                            finally { main.Close(); }
                        };
                        Application.Run(main);
                    }
                }
                catch(Exception ex) { completion.TrySetException(ex); }
            });
            thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
        }
        static Task LiveCaptureTest()
        {
            var completion = new TaskCompletionSource<bool>();
            var thread = new Thread(delegate()
            {
                using(var fixture = new System.Windows.Forms.Form { Text = "屏幕监控自动验证", BackColor = Color.White, ClientSize = new Size(400,160), StartPosition = System.Windows.Forms.FormStartPosition.Manual, TopMost = true })
                {
                    // Keep the fixture away from screen-edge overlays and use physical monitor coordinates.
                    var testScreen = Screen.AllScreens[0];
                    foreach(var candidate in Screen.AllScreens) if(candidate.Bounds.Left < testScreen.Bounds.Left) testScreen = candidate;
                    fixture.Location = new Point(testScreen.WorkingArea.Left + (testScreen.WorkingArea.Width-fixture.Width)/2,testScreen.WorkingArea.Top + (testScreen.WorkingArea.Height-fixture.Height)/2);
                    string fixtureText = "123.45";
                    fixture.Paint += delegate(object sender,System.Windows.Forms.PaintEventArgs e) { using(var font = new Font("Arial",36)) e.Graphics.DrawString(fixtureText,font,Brushes.Black,20,20); };
                    fixture.Shown += async delegate
                    {
                        try
                        {
                            await Task.Delay(250);
                            fixture.Show(); fixture.TopMost = false; fixture.TopMost = true; fixture.BringToFront(); fixture.Activate(); fixture.Refresh();
                            await Task.Delay(200);
                            var rect = fixture.RectangleToScreen(new Rectangle(10,10,330,90));
                            var c = new MonitorConfig { X = rect.X,Y = rect.Y,Width = rect.Width,Height = rect.Height };
                            decimal value; string error;
                            try
                            {
                                using(var screenshot = Native.Capture(Native.Resolve(c)))
                                {
                                    screenshot.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"tests","live-capture.png"),ImageFormat.Png);
                                    Native.RECT diagnosticBounds; Native.GetWindowRect(fixture.Handle,out diagnosticBounds);
                                    var underPoint = Native.GetAncestor(Native.WindowFromPoint(new Point(rect.Left+rect.Width/2,rect.Top+rect.Height/2)),2);
                                    Check(underPoint == fixture.Handle,"live test target is visible during capture (region " + rect + ", native window " + diagnosticBounds.Rect + ", visible " + Native.IsWindowVisible(fixture.Handle) + ", opacity " + fixture.Opacity + ", minimized " + Native.IsIconic(fixture.Handle) + ", foreground class " + Native.ClassName(underPoint) + ")");
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
                            fixture.WindowState = FormWindowState.Normal; await Task.Delay(100);
                            using(var main = new MainForm(new AppConfig()))
                            {
                                main.StartPosition = FormStartPosition.Manual; main.Bounds = moved; main.TopMost = true; main.ShowInTaskbar = false;
                                main.Show(); main.Activate(); await Task.Delay(150);
                                var center = new Point(moved.Left + moved.Width / 2,moved.Top + moved.Height / 2);
                                Check(Native.GetAncestor(Native.WindowFromPoint(center),2) == main.Handle,"live preview starts with main window covering target");
                                await RunEditorDialog(main,async editor =>
                                {
                                    // Exercise the production capture/resolve/OCR path with a real owned modal dialog.
                                    await editor.Preview();
                                    CheckEditorRestored(editor,main,"live preview restores modal editor after actual capture and OCR");
                                    Check(FindControl(editor,"当前数值：123.45   ·   满足报警条件\n原文：123.45") != null,"live modal preview uncovers followed target and recognizes 123.45");
                                },false,c);
                                main.Close();
                            }
                            var stableConfig = new AppConfig();
                            var numericMonitor = c.Copy(); numericMonitor.Rule = 4; numericMonitor.Name = "数值保持"; numericMonitor.UnchangedSeconds = 1; numericMonitor.IntervalSeconds = 1; numericMonitor.ConfirmCount = 30; numericMonitor.CooldownSeconds = 5;
                            numericMonitor.Sound = numericMonitor.Bark = numericMonitor.SystemNotification = false;
                            var textMonitor = numericMonitor.Copy(); textMonitor.Id = Guid.NewGuid().ToString("N"); textMonitor.Rule = 5; textMonitor.Name = "文字保持"; textMonitor.UnchangedSeconds = 2;
                            stableConfig.Monitors.Add(numericMonitor); stableConfig.Monitors.Add(textMonitor);
                            using(var main = new MainForm(stableConfig))
                            {
                                main.StartPosition = FormStartPosition.Manual; main.Location = new Point(-30000,-30000); main.Show();
                                var grid = (DataGridView)main.Controls.Find("Monitors",true)[0];
                                ((Button)FindControl(main,"开始全部")).PerformClick();
                                var deadline = DateTime.UtcNow.AddSeconds(15);
                                while(DateTime.UtcNow < deadline && (!Convert.ToString(grid.Rows[0].Cells[3].Value).Contains("已报警") || !Convert.ToString(grid.Rows[1].Cells[3].Value).Contains("已报警"))) await Task.Delay(100);
                                Check(Convert.ToString(grid.Rows[0].Cells[3].Value).Contains("已报警") && Convert.ToString(grid.Rows[0].Cells[1].Value) == "123.45","live monitoring loop triggers numeric unchanged alarm");
                                Check(Convert.ToString(grid.Rows[1].Cells[3].Value).Contains("已报警") && !string.IsNullOrWhiteSpace(Convert.ToString(grid.Rows[1].Cells[1].Value)),"live monitoring loop triggers text unchanged alarm independently");
                                main.Stop();
                                Check(Convert.ToString(grid.Rows[0].Cells[3].Value) == "已暂停" && Convert.ToString(grid.Rows[1].Cells[3].Value) == "已暂停","unchanged monitors can be paused");
                                fixtureText = "READY"; fixture.Refresh();
                                ((Button)FindControl(main,"开始全部")).PerformClick();
                                deadline = DateTime.UtcNow.AddSeconds(15);
                                while(DateTime.UtcNow < deadline && !Convert.ToString(grid.Rows[1].Cells[3].Value).Contains("已报警")) await Task.Delay(100);
                                Check(Convert.ToString(grid.Rows[1].Cells[3].Value).Contains("已报警") && Convert.ToString(grid.Rows[1].Cells[1].Value).Replace(" ","") == "READY","live nonnumeric READY text reaches unchanged alarm after resume");
                                Check(Convert.ToString(grid.Rows[0].Cells[1].Value) == "—" && !Convert.ToString(grid.Rows[0].Cells[3].Value).Contains("已报警"),"nonnumeric content invalidates numeric unchanged monitor");
                                main.Stop();
                                main.Close();
                            }
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

