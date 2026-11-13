using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace GGMaps
{
    public sealed class MapsApiException : Exception
    {
        public int Status { get; private set; }
        public MapsApiException(int status, string message) : base(message) { Status = status; }
    }

    /// <summary>
    /// HTTP plumbing for the (unofficial) endpoints used by the Google Maps web app, same approach as GGTranslate:
    /// no API key, browser User-Agent, cookies kept by HttpClientHandler, responses are ")]}'"-prefixed JSON arrays.
    /// </summary>
    public static class MapsApi
    {
        public const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

        public static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            var handler = new HttpClientHandler
            {
                UseCookies = true,
                CookieContainer = new CookieContainer(),
                AllowAutoRedirect = true
            };
            if (handler.SupportsAutomaticDecompression)
                handler.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            var c = new HttpClient(handler);
            c.Timeout = TimeSpan.FromSeconds(30);
            c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
            c.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "vi-VN,vi;q=0.9,en;q=0.6");
            return c;
        }

        /// <summary>
        /// True when the phone has no usable internet connection. On Windows Phone 8.1 HttpClient then answers every
        /// request with a fake "404 Not Found" instead of throwing, so this must be checked to show the right message.
        /// </summary>
        public static bool IsOffline
        {
            get
            {
                try
                {
                    var prof = Windows.Networking.Connectivity.NetworkInformation.GetInternetConnectionProfile();
                    return prof == null || prof.GetNetworkConnectivityLevel() == Windows.Networking.Connectivity.NetworkConnectivityLevel.None;
                }
                catch { return false; }
            }
        }

        public const string OfflineMessage = "Không có kết nối Internet. Bật Wi-Fi hoặc dữ liệu di động rồi thử lại.";

        /// <summary>GET a Google Maps web endpoint and parse its ")]}'" + JSON array body.</summary>
        public static async Task<JToken> GetJsonAsync(string url, CancellationToken ct)
        {
            if (IsOffline) throw new MapsApiException(0, OfflineMessage);
            using (var resp = await Http.GetAsync(url, ct))
            {
                string text = await resp.Content.ReadAsStringAsync();
                var final = resp.RequestMessage != null ? resp.RequestMessage.RequestUri : null;
                int status = (int)resp.StatusCode;
                if (status == 429 || (final != null && final.AbsolutePath.Contains("/sorry")) || text.Contains("google.com/sorry"))
                {
                    DiagLog.Write("blocked " + status + " " + Shorten(url));
                    throw new MapsApiException(429, "Google đang tạm chặn yêu cầu từ mạng này (quá nhiều truy vấn). Thử lại sau ít phút hoặc đổi mạng (Wi-Fi/4G).");
                }
                if (!resp.IsSuccessStatusCode)
                {
                    DiagLog.Write("HTTP " + status + " " + Shorten(url) + " " + Shorten(text));
                    throw new MapsApiException(status, "Google trả về lỗi HTTP " + status + ".");
                }
                int start = text.IndexOf('[');
                int brace = text.IndexOf('{');
                if (brace >= 0 && (start < 0 || brace < start)) start = brace;
                if (start < 0)
                {
                    DiagLog.Write("bad body " + Shorten(url) + " " + Shorten(text));
                    throw new MapsApiException(0, "Phản hồi của Google không đúng định dạng (có thể Google đã thay đổi).");
                }
                try { return JToken.Parse(text.Substring(start)); }
                catch (Exception ex)
                {
                    DiagLog.Write("parse " + ex.Message + " " + Shorten(text));
                    throw new MapsApiException(0, "Không đọc được dữ liệu từ Google (có thể Google đã thay đổi định dạng).");
                }
            }
        }

        private static string Shorten(string s)
        {
            if (s == null) return "";
            return s.Length > 300 ? s.Substring(0, 300) : s;
        }

        /// <summary>User-facing text for any exception.</summary>
        public static string Describe(Exception ex)
        {
            if (ex is OperationCanceledException) return "Hết thời gian chờ hoặc đã huỷ.";
            var api = ex as MapsApiException;
            if (api != null) return api.Message;
            if (ex is HttpRequestException) return "Không kết nối được tới Google. Kiểm tra mạng.";
            return ex != null ? ex.Message : "Lỗi không xác định";
        }

        // ---------------------------------------------------------------
        // safe navigation over Google's nested arrays
        // ---------------------------------------------------------------

        /// <summary>Walks an index path (null on any miss / wrong type).</summary>
        public static JToken At(JToken t, params int[] path)
        {
            foreach (int i in path)
            {
                var a = t as JArray;
                if (a == null || i < 0 || i >= a.Count) return null;
                t = a[i];
                if (t == null || t.Type == JTokenType.Null) return null;
            }
            return t;
        }

        public static string Str(JToken t, params int[] path)
        {
            var v = At(t, path);
            return v != null && v.Type == JTokenType.String ? (string)v : null;
        }

        public static double Num(JToken t, params int[] path)
        {
            var v = At(t, path);
            if (v == null) return double.NaN;
            if (v.Type == JTokenType.Integer || v.Type == JTokenType.Float) return (double)v;
            return double.NaN;
        }

        public static JArray Arr(JToken t, params int[] path)
        {
            return At(t, path) as JArray;
        }
    }
}
