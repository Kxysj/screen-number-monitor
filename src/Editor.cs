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
        NumericUpDown lower, upper, interval, confirm, cooldown, index;
        CheckBox sound, bark, repeat, invert, follow;
        Label region, preview;
        PictureBox picture;
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
            preview = Theme.Label("点击“试识别”检查选区，建议只框住一个数字。"); preview.MaximumSize = new Size(490,0); AddRow(table,"识别结果",preview);
            rule = Combo(Rules.Names,c.Rule); rule.SelectedIndexChanged += delegate { UpdateRule(); }; AddRow(table,"报警条件",rule);
            lower = Number(c.Lower,-1000000000000m,1000000000000m,6); upper = Number(c.Upper,-1000000000000m,1000000000000m,6);
            AddRow(table,"下限",lower); AddRow(table,"上限",upper); UpdateRule();
            AddRow(table,"边界说明",Theme.Label("大于 / 小于不含等于；区间内包含上下限。"));
            interval = Number(c.IntervalSeconds,1,3600,0); AddRow(table,"检测间隔（秒）",interval);
            confirm = Number(c.ConfirmCount,1,30,0); AddRow(table,"连续命中次数",confirm);
            cooldown = Number(c.CooldownSeconds,5,86400,0); AddRow(table,"报警冷却（秒）",cooldown);
            index = Number(c.NumberIndex,0,99,0); AddRow(table,"数字序号",index);
            AddRow(table,"序号说明",Theme.Label("0 = 必须只有一个数字；1、2… = 按阅读顺序取值。"));
            format = Combo(new[] { "小数点 . / 千位分隔 ,（如 1,234.56）", "小数逗号 , / 千位分隔 .（如 1.234,56）" },c.DecimalMode); AddRow(table,"数字格式",format);
            invert = Check("深色背景、浅色数字（反色增强）",c.Invert); AddRow(table,"识别增强",invert);
            sound = Check("电脑发出报警声",c.Sound); bark = Check("Bark 推送到手机",c.Bark);
            var channels = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill }; channels.Controls.Add(sound); channels.Controls.Add(bark); AddRow(table,"报警方式",channels);
            repeat = Check("持续异常时，按冷却时间重复报警",c.Repeat); AddRow(table,"重复提醒",repeat);
            AddRow(table,"提醒说明",Theme.Label("未勾选时每轮异常提醒一次，恢复正常后重新布防。"));
            scroll.Controls.Add(table); Controls.Add(scroll); Controls.Add(footer);
            FormClosed += delegate { if (picture.Image != null) picture.Image.Dispose(); };
        }
        static ComboBox Combo(string[] values, int selected) { var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill }; c.Items.AddRange(values); c.SelectedIndex = selected; return c; }
        static CheckBox Check(string text, bool value) { return new CheckBox { Text = text, Checked = value, AutoSize = true, Margin = new Padding(0,6,12,6) }; }
        static NumericUpDown Number(decimal value, decimal min, decimal max, int places) { return new NumericUpDown { Minimum = min, Maximum = max, DecimalPlaces = places, Value = Math.Min(max,Math.Max(min,value)), Width = 240, ThousandsSeparator = true }; }
        static void AddRow(TableLayoutPanel t, string label, Control c) { int r = t.RowCount++; t.RowStyles.Add(new RowStyle(SizeType.AutoSize)); t.Controls.Add(Theme.Label(label),0,r); c.Margin = new Padding(0,5,0,5); t.Controls.Add(c,1,r); }
        void UpdateRule() { if (lower != null) lower.Enabled = rule.SelectedIndex != 0; if (upper != null) upper.Enabled = rule.SelectedIndex != 1; }
        void UpdateRegion() { region.Text = string.Format("X {0} · Y {1}    {2} × {3} 像素{4}",Result.X,Result.Y,Result.Width,Result.Height,string.IsNullOrEmpty(Result.WindowTitle) ? "" : "\n" + Result.WindowTitle); }
        void Pull()
        {
            Result.Name = name.Text.Trim(); Result.Rule = rule.SelectedIndex; Result.Lower = lower.Value; Result.Upper = upper.Value;
            Result.IntervalSeconds = (int)interval.Value; Result.ConfirmCount = (int)confirm.Value; Result.CooldownSeconds = (int)cooldown.Value;
            Result.NumberIndex = (int)index.Value; Result.DecimalMode = format.SelectedIndex; Result.Invert = invert.Checked;
            Result.Sound = sound.Checked; Result.Bark = bark.Checked; Result.Repeat = repeat.Checked; Result.FollowWindow = follow.Checked;
        }
        async Task SelectRegion()
        {
            Hide(); main.Hide();
            try
            {
                await Task.Delay(250);
                using (var selection = new SelectionForm()) if (selection.ShowDialog() == DialogResult.OK)
                {
                    var r = selection.SelectedRegion; Result.X = r.X; Result.Y = r.Y; Result.Width = r.Width; Result.Height = r.Height;
                    Result.WindowHandle = 0; Result.WindowTitle = "";
                    if (follow.Checked) { await Task.Delay(150); Native.Bind(Result); }
                    UpdateRegion();
                }
            }
            catch (Exception ex) { MessageBox.Show(ex.Message,"框选失败"); }
            finally { main.Show(); Show(); Activate(); }
        }
        async Task Preview()
        {
            Pull(); Enabled = false; Hide(); main.Hide();
            Bitmap shot = null;
            try
            {
                await Task.Delay(250); shot = Native.Capture(Native.Resolve(Result));
                var text = await new OcrReader().Read(shot,Result.Invert);
                decimal value; string error;
                bool ok = Numbers.TryRead(text,Result.NumberIndex,Result.DecimalMode,out value,out error);
                preview.Text = (ok ? "当前数值：" + value + "   ·   " + (Rules.Matches(Result,value) ? "满足报警条件" : "正常") : error) + "\n原文：" + text;
                preview.ForeColor = ok ? Theme.Accent : Color.Firebrick;
                if (picture.Image != null) picture.Image.Dispose(); picture.Image = shot; shot = null;
            }
            catch (Exception ex) { preview.Text = ex.Message; preview.ForeColor = Color.Firebrick; }
            finally { if (shot != null) shot.Dispose(); Enabled = true; main.Show(); Show(); Activate(); }
        }
        void Save(object sender, EventArgs args)
        {
            Pull();
            if (Result.Name.Length == 0) { MessageBox.Show(this,"请输入监控名称。"); return; }
            if (Result.Width < 8 || Result.Height < 8) { MessageBox.Show(this,"请先框选监控区域。"); return; }
            if (Result.Rule >= 2 && Result.Lower >= Result.Upper) { MessageBox.Show(this,"区间下限必须小于上限。"); return; }
            if (Result.FollowWindow && Result.WindowHandle == 0) { MessageBox.Show(this,"请重新框选，绑定要跟随的窗口。"); return; }
            if (!Result.Sound && !Result.Bark) { MessageBox.Show(this,"至少选择一种报警方式。"); return; }
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

