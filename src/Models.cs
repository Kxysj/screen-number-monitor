using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Serialization;

namespace ScreenWatch
{
    public class MonitorConfig
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Name = "数值监控";
        public int X, Y, Width, Height;
        public bool FollowWindow;
        public long WindowHandle;
        public string WindowTitle = "", WindowClass = "";
        public int WindowProcess;
        public long WindowStarted;
        public int OffsetX, OffsetY;
        public int Rule;
        public decimal Lower = 0, Upper = 100;
        public int IntervalSeconds = 2, ConfirmCount = 2, CooldownSeconds = 60;
        public int UnchangedSeconds = 60;
        public bool Sound = true, Bark = true, Repeat = false, Enabled = true, Invert = false;
        public bool SystemNotification = false;
        public int NumberIndex = 0, DecimalMode = 0;
        public Rectangle Region { get { return new Rectangle(X, Y, Width, Height); } }
        public MonitorConfig Copy() { return (MonitorConfig)MemberwiseClone(); }
        public string RuleText { get { return Rules.Describe(this); } }
    }
    public class AppConfig
    {
        public string BarkProtected = "";
        public List<MonitorConfig> Monitors = new List<MonitorConfig>();
    }
    public static class Settings
    {
        public static string FilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.xml");
        public static AppConfig Load()
        {
            if (!File.Exists(FilePath)) return new AppConfig();
            using (var f = File.OpenRead(FilePath)) return (AppConfig)new XmlSerializer(typeof(AppConfig)).Deserialize(f);
        }
        public static void Save(AppConfig config)
        {
            string temp = FilePath + ".tmp";
            using (var f = File.Create(temp)) new XmlSerializer(typeof(AppConfig)).Serialize(f, config);
            if (File.Exists(FilePath)) File.Replace(temp, FilePath, FilePath + ".bak");
            else File.Move(temp, FilePath);
        }
        public static string Encrypt(string value)
        {
            return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));
        }
        public static string Decrypt(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value), null, DataProtectionScope.CurrentUser));
        }
    }
    public static class Rules
    {
        public static readonly string[] Names = { "大于上限", "小于下限", "超出区间", "进入区间", "数值持续不变", "文字持续不变" };
        public static bool IsUnchanged(MonitorConfig c) { return c.Rule == 4 || c.Rule == 5; }
        public static bool IsText(MonitorConfig c) { return c.Rule == 5; }
        public static bool Matches(MonitorConfig c, decimal value)
        {
            switch (c.Rule) { case 0: return value > c.Upper; case 1: return value < c.Lower;
                case 2: return value < c.Lower || value > c.Upper; case 3: return value >= c.Lower && value <= c.Upper;
                default: throw new InvalidOperationException("未知报警条件"); }
        }
        public static string Describe(MonitorConfig c)
        {
            if (IsUnchanged(c)) return (IsText(c) ? "文字" : "数值") + "不变 ≥ " + c.UnchangedSeconds + " 秒";
            if (c.Rule == 0) return "> " + c.Upper;
            if (c.Rule == 1) return "< " + c.Lower;
            return (c.Rule == 2 ? "区间外 " : "区间内 ") + "[" + c.Lower + ", " + c.Upper + "]";
        }
    }
    public sealed class AlarmState
    {
        public int Hits;
        public bool Active;
        public DateTime LastAlert = DateTime.MinValue;
        string lastContent;
        DateTime unchangedSince, lastObservation;
        public double UnchangedElapsed { get; private set; }
        public bool UnchangedExpired { get; private set; }
        public void Invalid()
        {
            Hits = 0;
            if (lastContent != null) Active = false;
            lastContent = null; UnchangedElapsed = 0; UnchangedExpired = false;
        }
        public bool Observe(MonitorConfig c,MonitorReading reading,DateTime now)
        {
            if (!Rules.IsUnchanged(c)) return Observe(c,reading.Value,now);
            // A failed observation or a long interruption cannot establish continuous stability.
            bool gap = lastContent != null && (now < lastObservation || (now-lastObservation).TotalSeconds > Math.Max(30,c.IntervalSeconds*3));
            if (gap || !string.Equals(lastContent,reading.Identity,StringComparison.Ordinal))
            {
                lastContent = reading.Identity; unchangedSince = now; Active = false;
                UnchangedElapsed = 0; UnchangedExpired = false;
            }
            lastObservation = now;
            UnchangedElapsed = Math.Max(0,(now-unchangedSince).TotalSeconds);
            UnchangedExpired = UnchangedElapsed >= Math.Max(1,c.UnchangedSeconds);
            return UnchangedExpired && AlertIfReady(c,now);
        }
        public bool Observe(MonitorConfig c, decimal value, DateTime now)
        {
            if (!Rules.Matches(c, value)) { Hits = 0; Active = false; return false; }
            Hits = Math.Min(Hits + 1, c.ConfirmCount);
            if (Hits < c.ConfirmCount) return false;
            return AlertIfReady(c,now);
        }
        bool AlertIfReady(MonitorConfig c,DateTime now)
        {
            if (Active && !c.Repeat) return false;
            if (LastAlert != DateTime.MinValue && (now - LastAlert).TotalSeconds < c.CooldownSeconds) return false;
            Active = true; LastAlert = now; return true;
        }
    }
    public sealed class MonitorReading
    {
        public string Display, Identity;
        public decimal Value;
        public static bool TryRead(MonitorConfig c,string raw,out MonitorReading reading,out string error)
        {
            reading = null; error = "";
            if (Rules.IsText(c))
            {
                string text = Regex.Replace((raw ?? "").Normalize(NormalizationForm.FormC),@"\s+"," ").Trim();
                if (text.Length == 0) { error = "未识别到文字"; return false; }
                reading = new MonitorReading { Display = text, Identity = text }; return true;
            }
            decimal value;
            if (!Numbers.TryRead(raw,c.NumberIndex,c.DecimalMode,out value,out error)) return false;
            reading = new MonitorReading { Value = value, Display = value.ToString(), Identity = value.ToString("G29",CultureInfo.InvariantCulture) }; return true;
        }
    }
    public static class Numbers
    {
        public static bool TryRead(string text, int index, int decimalMode, out decimal value, out string error)
        {
            value = 0; error = "";
            string clean = (text ?? "").Normalize(NormalizationForm.FormKC).Replace('−', '-').Replace('–', '-').Replace('，', ',').Replace('。', '.');
            if (Regex.IsMatch(clean, @"^\s*[·•]\s*\d")) { error = "数字前符号不明确，请调整选区或反色后试识别"; return false; }
            // Join a separated sign, but never concatenate separate numbers or lines.
            clean = Regex.Replace(clean, @"([+\-])[^\S\r\n]+(?=\d)", "$1");
            clean = Regex.Replace(clean, @"(?<=\d)[^\S\r\n]*[·•][^\S\r\n]*(?=\d)", ".");
            clean = Regex.Replace(clean, @"(?<=\d)[^\S\r\n]*([.,])[^\S\r\n]*(?=\d)", "$1");
            var tokens = Regex.Matches(clean, @"[+\-]?(?:\d[\d.,]*|[.,]\d+)(?:[eE][+\-]?\d+)?").Cast<Match>().Select(m => m.Value.TrimEnd('.', ',')).ToList();
            if (tokens.Count == 0) { error = "未识别到数字"; return false; }
            if (index == 0 && tokens.Count != 1) { error = "识别到多个数字，请缩小区域或指定数字序号"; return false; }
            int i = index == 0 ? 0 : index - 1;
            if (i >= tokens.Count) { error = "未找到第 " + index + " 个数字"; return false; }
            string token = tokens[i];
            if (decimalMode == 0)
            {
                if (!Regex.IsMatch(token, @"^[+\-]?(?:(?:\d{1,3}(?:,\d{3})+|\d+)(?:\.\d+)?|\.\d+)(?:[eE][+\-]?\d+)?$"))
                { error = "数字格式不明确（逗号小数请选择对应格式）"; return false; }
                token = token.Replace(",", "");
            }
            else
            {
                if (!Regex.IsMatch(token, @"^[+\-]?(?:(?:\d{1,3}(?:\.\d{3})+|\d+)(?:,\d+)?|,\d+)(?:[eE][+\-]?\d+)?$"))
                { error = "数字格式不明确"; return false; }
                token = token.Replace(".", "").Replace(',', '.');
            }
            if (!decimal.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) { error = "数字超出可处理范围"; return false; }
            return true;
        }
    }
}
