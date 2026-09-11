using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Media;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ScreenWatch
{
    sealed class MonitorRuntime
    {
        public MonitorConfig Config;
        public AlarmState Alarm = new AlarmState();
        public DateTime Due = DateTime.MinValue;
        public DataGridViewRow Row;
        public bool PushPending;
    }
    public sealed class MainForm : Form
    {
        AppConfig config;
        string endpoint = "";
        readonly List<MonitorRuntime> monitors = new List<MonitorRuntime>();
        readonly BarkClient bark = new BarkClient();
        readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        CancellationTokenSource monitoring = new CancellationTokenSource();
        readonly SemaphoreSlim sends = new SemaphoreSlim(1,1);
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 200 };
        readonly SoundPlayer sound = CreateSoundPlayer();
        DataGridView grid;
        TextBox log;
        Label status, barkStatus;
        Button start;
        OcrReader ocr;
        bool running, busy, closed;
        int generation;
        static SoundPlayer CreateSoundPlayer()
        {
            var embedded = Assembly.GetExecutingAssembly().GetManifestResourceStream("ScreenWatch.alarm.wav");
            return embedded == null ? new SoundPlayer(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"alarm.wav")) : new SoundPlayer(embedded);
        }
        public MainForm(AppConfig loaded)
        {
            config = loaded;
            try { endpoint = Settings.Decrypt(config.BarkProtected); } catch { endpoint = ""; }
            Text = "屏幕数值监控 1.1"; Font = new Font("Microsoft YaHei UI",10); ForeColor = Theme.Ink; BackColor = Theme.Background;
            Size = new Size(1220,790); MinimumSize = new Size(1000,650); StartPosition = FormStartPosition.CenterScreen; AutoScaleMode = AutoScaleMode.Dpi;
            var top = new Panel { Dock = DockStyle.Top, Height = 148, Padding = new Padding(24) };
            var title = new Label { Text = "屏幕数值监控", Font = new Font("Microsoft YaHei UI",22,FontStyle.Bold), Left = 24, Top = 18, AutoSize = true };
            status = new Label { Text = "已暂停  ·  框选数字区域，设置条件后开始监控", Left = 26, Top = 66, Width = 1050, Height = 26, ForeColor = Color.FromArgb(95,113,132) };
            var actions = new FlowLayoutPanel { Left = 24, Top = 104, Width = 1120, Height = 40, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            actions.Controls.Add(Theme.Button("＋ 新建监控",async delegate { await Add(); },true));
            start = Theme.Button("开始全部",delegate { if (running) Stop(); else Start(); }); actions.Controls.Add(start);
            actions.Controls.Add(Theme.Button("编辑选中",delegate { Edit(); }));
            actions.Controls.Add(Theme.Button("启用 / 停用",delegate { Toggle(); }));
            actions.Controls.Add(Theme.Button("删除选中",delegate { Delete(); }));
            actions.Controls.Add(Theme.Button("Bark 设置",delegate { ConfigureBark(); }));
            actions.Controls.Add(Theme.Button("测试声音",delegate { PlaySound(); }));
            actions.Controls.Add(Theme.Button("测试推送",async delegate { await TestPush(); }));
            actions.SizeChanged += delegate { int height = Math.Max(40,actions.GetPreferredSize(new Size(actions.Width,0)).Height); if(actions.Height != height) actions.Height = height; top.Height = actions.Top + height + 4; };
            top.Controls.AddRange(new Control[] {title,status,actions});
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 205, Padding = new Padding(24,0,24,16) };
            barkStatus = new Label { Dock = DockStyle.Top, Height = 31, ForeColor = Color.FromArgb(95,113,132) }; UpdateBarkStatus();
            log = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Microsoft YaHei UI",9) };
            bottom.Controls.Add(log); bottom.Controls.Add(barkStatus);
            var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24,16,24,16) };
            grid = new DataGridView { Dock = DockStyle.Fill, BackgroundColor = Color.White, BorderStyle = BorderStyle.None, AutoGenerateColumns = false, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false, ReadOnly = true, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, EnableHeadersVisualStyles = false, CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal, GridColor = Color.FromArgb(232,238,243) };
            grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(228,235,242), ForeColor = Theme.Ink, Font = new Font(Font,FontStyle.Bold), Padding = new Padding(6) };
            grid.DefaultCellStyle = new DataGridViewCellStyle { Padding = new Padding(6), SelectionBackColor = Color.FromArgb(218,242,237), SelectionForeColor = Theme.Ink };
            grid.ColumnHeadersHeight = 44; grid.RowTemplate.Height = 58;
            AddColumn("监控名称",16); AddColumn("当前数值",12); AddColumn("报警条件",16); AddColumn("状态",23); AddColumn("检测时间",11); AddColumn("区域 / 模式",22);
            grid.CellDoubleClick += delegate(object s,DataGridViewCellEventArgs e) { if (e.RowIndex >= 0) Edit(); };
            body.Controls.Add(grid); Controls.Add(body); Controls.Add(bottom); Controls.Add(top);
            ReloadRows();
            timer.Tick += async delegate { await Tick(); }; timer.Start();
            FormClosing += delegate { closed = true; running = false; generation++; timer.Stop(); lifetime.Cancel(); monitoring.Cancel(); sound.Stop(); };
            FormClosed += delegate { timer.Dispose(); sound.Dispose(); };
            Log("就绪。检测在本机完成；仅报警内容通过 Bark 发送。启动后默认暂停。");
        }
        void AddColumn(string title, float weight) { grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = title, FillWeight = weight, SortMode = DataGridViewColumnSortMode.NotSortable }); }
        void UpdateBarkStatus() { barkStatus.Text = "事件记录     ·     Bark " + (endpoint.Length == 0 ? "未配置 / 当前账户无法解密，请重新填写" : "已配置（地址已加密）") + "     ·     目标数字需保持可见，锁屏时请暂停"; }
        void Log(string message)
        {
            if (closed) return;
            if (log.TextLength > 42000) log.Text = log.Text.Substring(log.TextLength - 28000);
            log.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + message.Replace("\r"," ").Replace("\n"," ") + Environment.NewLine);
        }
        void ReloadRows()
        {
            grid.Rows.Clear(); monitors.Clear();
            foreach (var c in config.Monitors)
            {
                int i = grid.Rows.Add(c.Name,"—",c.RuleText,c.Enabled ? "已暂停" : "已停用","—",string.Format("{0} × {1}  /  {2}",c.Width,c.Height,c.FollowWindow ? "跟随窗口" : "固定屏幕"));
                var m = new MonitorRuntime { Config = c, Row = grid.Rows[i] }; m.Row.Tag = m; monitors.Add(m);
            }
        }
        MonitorRuntime Selected() { return grid.SelectedRows.Count == 0 ? null : grid.SelectedRows[0].Tag as MonitorRuntime; }
        void Save() { try { Settings.Save(config); } catch(Exception ex) { MessageBox.Show(this,"设置保存失败：" + ex.Message,"无法保存"); } }
        public void Stop()
        {
            running = false; generation++; sound.Stop(); start.Text = "开始全部"; status.Text = "已暂停  ·  可新建或编辑监控";
            monitoring.Cancel();
            foreach (var m in monitors) { m.Alarm.Hits = 0; m.Row.Cells[3].Value = m.Config.Enabled ? "已暂停" : "已停用"; m.Row.DefaultCellStyle.ForeColor = Theme.Ink; }
        }
        void Start()
        {
            if (!monitors.Any(m=>m.Config.Enabled)) { MessageBox.Show(this,"请先创建并启用至少一个监控。"); return; }
            if (endpoint.Length == 0 && monitors.Any(m=>m.Config.Enabled && m.Config.Bark)) { MessageBox.Show(this,"有监控启用了 Bark，请先填写 Bark 地址，或在该监控中关闭 Bark。"); return; }
            try { if (ocr == null) ocr = new OcrReader(); } catch(Exception ex) { MessageBox.Show(this,ex.Message,"无法开始 OCR"); return; }
            generation++; running = true;
            monitoring.Dispose(); monitoring = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            foreach (var m in monitors) { m.Due = DateTime.MinValue; m.Alarm.Hits = 0; }
            start.Text = "暂停全部"; status.Text = "正在监控 " + monitors.Count(m=>m.Config.Enabled) + " 个区域  ·  保持目标数字可见"; Log("开始监控。");
        }
        async Task Add()
        {
            Stop(); Hide(); var c = new MonitorConfig { Name = "监控 " + (config.Monitors.Count + 1) };
            try
            {
                await Task.Delay(250);
                using(var selection = new SelectionForm())
                {
                    if (selection.ShowDialog() != DialogResult.OK) return;
                    var r = selection.SelectedRegion; c.X = r.X; c.Y = r.Y; c.Width = r.Width; c.Height = r.Height;
                    await Task.Delay(120);
                    try { Native.Bind(c); } catch { c.WindowHandle = 0; }
                }
            }
            catch(Exception ex) { MessageBox.Show(ex.Message,"框选失败"); return; }
            finally { Show(); Activate(); }
            using(var editor = new Editor(this,c)) if(editor.ShowDialog(this) == DialogResult.OK) { config.Monitors.Add(editor.Result); Save(); ReloadRows(); }
        }
        void Edit()
        {
            var m = Selected(); if(m == null) return; Stop();
            using(var editor = new Editor(this,m.Config)) if(editor.ShowDialog(this) == DialogResult.OK) { config.Monitors[config.Monitors.IndexOf(m.Config)] = editor.Result; Save(); ReloadRows(); }
        }
        void Toggle() { var m = Selected(); if(m == null) return; Stop(); m.Config.Enabled = !m.Config.Enabled; Save(); ReloadRows(); }
        void Delete() { var m = Selected(); if(m == null) return; Stop(); if(MessageBox.Show(this,"删除监控“" + m.Config.Name + "”？","删除监控",MessageBoxButtons.OKCancel) != DialogResult.OK) return; config.Monitors.Remove(m.Config); Save(); ReloadRows(); }
        void ConfigureBark()
        {
            Stop(); using(var dialog = new BarkDialog(endpoint)) if(dialog.ShowDialog(this) == DialogResult.OK) { endpoint = dialog.Endpoint; config.BarkProtected = endpoint.Length == 0 ? "" : Settings.Encrypt(endpoint); Save(); UpdateBarkStatus(); }
        }
        void PlaySound() { try { sound.Play(); } catch { SystemSounds.Exclamation.Play(); Log("自定义声音不可用，已使用系统提示音。"); } }
        async Task TestPush()
        {
            if(endpoint.Length == 0) { ConfigureBark(); if(endpoint.Length == 0) return; }
            await Send(endpoint,"屏幕数值监控 · 测试","Bark 通知已连接。时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),"测试",lifetime.Token);
        }
        async Task Send(string address,string title,string body,string name,CancellationToken cancellation)
        {
            bool entered = false;
            try { await sends.WaitAsync(cancellation); entered = true; await bark.Send(address,title,body,cancellation); Log(name + "：Bark 已被服务器接收。"); }
            catch(OperationCanceledException) { if(!closed) Log(name + "：Bark 请求超时或已取消。"); }
            catch(Exception ex) { Log(name + "：Bark 发送失败（" + (ex is System.Net.Http.HttpRequestException ? "网络连接失败" : ex.Message) + "）。可检查网络后点击测试推送；持续提醒需开启重复报警。"); }
            finally { if(entered) sends.Release(); }
        }
        async Task Tick()
        {
            if(!running || busy || closed) return;
            busy = true; int token = generation;
            try
            {
                foreach(var m in monitors.ToArray())
                {
                    if(!running || generation != token || closed) break;
                    var c = m.Config; if(!c.Enabled || DateTime.UtcNow < m.Due) continue;
                    m.Due = DateTime.UtcNow.AddSeconds(c.IntervalSeconds);
                    try
                    {
                        string raw;
                        using(var shot = Native.Capture(Native.Resolve(c))) raw = await ocr.Read(shot,c.Invert);
                        if(!running || generation != token || closed) break;
                        decimal value; string error;
                        m.Row.Cells[4].Value = DateTime.Now.ToString("HH:mm:ss");
                        m.Row.Cells[1].ToolTipText = "OCR 原文：" + raw;
                        if(!Numbers.TryRead(raw,c.NumberIndex,c.DecimalMode,out value,out error)) { Invalid(m,error); continue; }
                        m.Row.Cells[1].Value = value.ToString();
                        bool alert = m.Alarm.Observe(c,value,DateTime.UtcNow), abnormal = Rules.Matches(c,value);
                        m.Row.Cells[3].Value = abnormal ? (m.Alarm.Hits < c.ConfirmCount ? "命中 " + m.Alarm.Hits + "/" + c.ConfirmCount : "异常 · " + (m.Alarm.Active ? "已报警" : "冷却中")) : "正常";
                        m.Row.DefaultCellStyle.ForeColor = abnormal ? Color.FromArgb(189,62,43) : Theme.Accent;
                        if(alert)
                        {
                            Log(c.Name + "：当前值 " + value + "，触发条件 " + c.RuleText);
                            if(c.Sound) PlaySound();
                            if(c.Bark && !m.PushPending) SendInBackground(endpoint,m,value,monitoring.Token);
                        }
                    }
                    catch(Exception ex) { if(running && generation == token && !closed) Invalid(m,ex.Message); }
                }
            }
            finally { busy = false; }
        }
        async void SendInBackground(string address,MonitorRuntime monitor,decimal value,CancellationToken cancellation)
        {
            monitor.PushPending = true;
            try { await Send(address,"数值报警 · " + monitor.Config.Name,"当前值：" + value + "\n条件：" + monitor.Config.RuleText + "\n时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),monitor.Config.Name,cancellation); }
            finally { monitor.PushPending = false; }
        }
        void Invalid(MonitorRuntime m,string error)
        {
            string old = Convert.ToString(m.Row.Cells[3].Value); m.Alarm.Invalid(); m.Row.Cells[1].Value = "—"; m.Row.Cells[3].Value = error; m.Row.Cells[3].ToolTipText = error;
            m.Row.Cells[4].Value = DateTime.Now.ToString("HH:mm:ss"); m.Row.DefaultCellStyle.ForeColor = Color.FromArgb(155,110,30);
            if(old != error) Log(m.Config.Name + "：" + error);
        }
    }
}
