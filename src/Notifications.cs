using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace ScreenWatch
{
    public sealed class BarkClient : IDisposable
    {
        readonly HttpClient client;
        public BarkClient() : this(new HttpClientHandler { AllowAutoRedirect = false }) { }
        public BarkClient(HttpMessageHandler handler) { client = new HttpClient(handler); client.Timeout = TimeSpan.FromSeconds(12); }
        public static string Normalize(string input)
        {
            Uri uri;
            if (!Uri.TryCreate(input.Trim(), UriKind.Absolute, out uri) || uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo)) throw new Exception("请输入 HTTPS Bark 地址，例如 https://api.day.app/你的设备Key");
            string[] pieces = uri.AbsolutePath.Split(new[] {'/'},StringSplitOptions.RemoveEmptyEntries);
            if (pieces.Length < 1) throw new Exception("Bark 地址缺少设备 Key");
            return uri.GetLeftPart(UriPartial.Authority) + "/" + pieces[0];
        }
        public async Task Send(string endpoint, string title, string body, CancellationToken token)
        {
            string normalized = Normalize(endpoint);
            var uri = new Uri(normalized);
            var payload = new { device_key = Uri.UnescapeDataString(uri.AbsolutePath.Trim('/')), title = title, body = body, group = "屏幕数值监控", sound = "alarm", level = "timeSensitive", isArchive = "1" };
            var json = new JavaScriptSerializer().Serialize(payload);
            using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
            using (var response = await client.PostAsync(uri.GetLeftPart(UriPartial.Authority) + "/push", content, token))
            {
                if (!response.IsSuccessStatusCode) throw new Exception("Bark HTTP " + (int)response.StatusCode);
                Dictionary<string, object> result;
                try { result = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(await response.Content.ReadAsStringAsync()); }
                catch { throw new Exception("Bark 返回了无法解析的响应"); }
                if (result == null || !result.ContainsKey("code") || Convert.ToInt32(result["code"]) != 200) throw new Exception("Bark 未确认推送成功，请检查设备 Key");
            }
        }
        public void Dispose() { client.Dispose(); }
    }
}
