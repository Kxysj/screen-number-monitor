using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Windows.Forms;

namespace ScreenWatch
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            try { Native.SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch { Native.SetProcessDPIAware(); }
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            if(args.Length > 0 && args[0] == "--self-test") { SelfTest.Run(args.Length > 1 ? args[1] : "test-results.txt").GetAwaiter().GetResult(); return; }
            if(args.Length > 1 && args[0] == "--set-bark") { var c = Settings.Load(); c.BarkProtected = Settings.Encrypt(BarkClient.Normalize(args[1])); Settings.Save(c); return; }
            bool created;
            using(var mutex = new Mutex(true,"Local\\ScreenNumericWatch_Portable",out created))
            {
                if(!created) { MessageBox.Show("屏幕数值监控已经运行，请切换到现有窗口。","提示"); return; }
                try { Application.Run(new MainForm(Settings.Load())); }
                catch(Exception ex) { MessageBox.Show("程序无法启动：" + ex.Message + "\n如果设置文件损坏，请先备份再重命名 settings.xml 后重试。","屏幕数值监控"); }
            }
        }
    }
}
