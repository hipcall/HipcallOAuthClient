using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Text.Json;

namespace Desktop
{
    public partial class MainWindow : Window
    {
        private readonly string ClientId = "<BURAYA_HIPCALL_CLIENT_ID_GELECEK>";
        private readonly string RedirectUri = "<BURAYA_REDIRECT_URI_GELECEK_ORN_NGROK_VEYA_LOCALHOST>";
        private HttpClient _httpClient;

        public MainWindow()
        {
            InitializeComponent();
            _httpClient = new HttpClient();
        }

        private void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            LoginButton.IsEnabled = false;
            ResultTextBox.Text = "Tarayıcı açılıyor, giriş yapmanız bekleniyor...";

            string codeVerifier = GenerateCodeVerifier();
            string codeChallenge = GenerateCodeChallenge(codeVerifier);

            string authorizeUrl = $"https://use.hipcall.com.tr/oauth/authorize?response_type=code" +
                                  $"&client_id={ClientId}" +
                                  $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}" +
                                  $"&scope=profile+email+offline_access" +
                                  $"&state=desktop123" +
                                  $"&code_challenge={codeChallenge}" +
                                  $"&code_challenge_method=S256";

            StartListenerAndHandleFlow(codeVerifier);

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = authorizeUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                ResultTextBox.Text = $"Tarayıcı açılamadı: {ex.Message}\nLütfen şu URL'yi manuel kopyalayın:\n{authorizeUrl}";
                LoginButton.IsEnabled = true;
            }
        }

        private async void StartListenerAndHandleFlow(string codeVerifier)
        {
            try
            {
                var tcpListener = new TcpListener(IPAddress.Loopback, 5062);
                tcpListener.Start();
                
                using var client = await tcpListener.AcceptTcpClientAsync();
                using var stream = client.GetStream();
                
                byte[] buffer = new byte[4096];
                int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                string requestData = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                
                string firstLine = requestData.Split('\n')[0];
                string[] parts = firstLine.Split(' ');
                string path = parts.Length > 1 ? parts[1] : "";

                string code = "";
                string error = "";

                if (path.Contains("?"))
                {
                    string queryString = path.Substring(path.IndexOf('?') + 1);
                    string[] queryParams = queryString.Split('&');
                    foreach (var param in queryParams)
                    {
                        string[] kv = param.Split('=');
                        if (kv.Length == 2)
                        {
                            if (kv[0] == "code") code = kv[1];
                            if (kv[0] == "error") error = kv[1];
                        }
                    }
                }

                string htmlBody;
                if (!string.IsNullOrEmpty(code))
                {
                    htmlBody = "<html><body style='font-family:sans-serif; text-align:center; margin-top:50px;'>" +
                               "<h2 style='color:green;'>Giriş Başarılı!</h2>" +
                               "<p>Masaüstü uygulamasına geri dönebilir, bu sekmeyi kapatabilirsiniz.</p></body></html>";
                }
                else
                {
                    htmlBody = "<html><body style='font-family:sans-serif; text-align:center; margin-top:50px;'>" +
                               "<h2 style='color:red;'>Giriş Başarısız!</h2>" +
                               $"<p>Hata: {error}</p></body></html>";
                }

                string httpResponse = "HTTP/1.1 200 OK\r\n" +
                                      "Content-Type: text/html; charset=UTF-8\r\n" +
                                      "Connection: close\r\n" +
                                      $"Content-Length: {Encoding.UTF8.GetByteCount(htmlBody)}\r\n\r\n" +
                                      htmlBody;

                byte[] responseBytes = Encoding.UTF8.GetBytes(httpResponse);
                await stream.WriteAsync(responseBytes, 0, responseBytes.Length);
                
                tcpListener.Stop();

                if (!string.IsNullOrEmpty(code))
                {
                    Dispatcher.Invoke(() => ResultTextBox.Text = "Giriş yakalandı, Token alınıyor...");
                    await ExchangeTokenAndFetchProfile(code, codeVerifier);
                }
                else
                {
                    Dispatcher.Invoke(() =>
                    {
                        ResultTextBox.Text = $"OAuth Hatası: {error}\n\nHipcall API, hesabınızın yetkisi olmadığını söylüyor (Muhtemelen istenen scope'lar fazla geldi).";
                        LoginButton.IsEnabled = true;
                    });
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    ResultTextBox.Text = $"Dinleyici hatası: {ex.Message}";
                    LoginButton.IsEnabled = true;
                });
            }
        }

        private async Task ExchangeTokenAndFetchProfile(string code, string codeVerifier)
        {
            try
            {
                var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    { "client_id", ClientId },
                    { "grant_type", "authorization_code" },
                    { "code", code },
                    { "redirect_uri", RedirectUri },
                    { "code_verifier", codeVerifier }
                });

                var tokenResponse = await _httpClient.PostAsync("https://use.hipcall.com.tr/oauth/token", tokenRequest);
                var tokenJson = await tokenResponse.Content.ReadAsStringAsync();

                if (!tokenResponse.IsSuccessStatusCode)
                {
                    Dispatcher.Invoke(() =>
                    {
                        ResultTextBox.Text = $"Token alınamadı (Hata Kodu: {tokenResponse.StatusCode}):\n{tokenJson}";
                        LoginButton.IsEnabled = true;
                    });
                    return;
                }

                using var doc = JsonDocument.Parse(tokenJson);
                string accessToken = doc.RootElement.GetProperty("access_token").GetString() ?? "";

                Dispatcher.Invoke(() => ResultTextBox.Text = "Token başarılı! Profil verisi çekiliyor...");

                var profileReq = new HttpRequestMessage(HttpMethod.Get, "https://use.hipcall.com.tr/api/v3/profile");
                profileReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

                var profileRes = await _httpClient.SendAsync(profileReq);
                var profileJson = await profileRes.Content.ReadAsStringAsync();

                using var pDoc = JsonDocument.Parse(profileJson);
                var formattedJson = JsonSerializer.Serialize(pDoc, new JsonSerializerOptions { WriteIndented = true });

                Dispatcher.Invoke(() =>
                {
                    ResultTextBox.Text = formattedJson;
                    LoginButton.IsEnabled = true;
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    ResultTextBox.Text = $"Hata: {ex.Message}";
                    LoginButton.IsEnabled = true;
                });
            }
        }

        private string GenerateCodeVerifier()
        {
            using var rng = RandomNumberGenerator.Create();
            byte[] bytes = new byte[64];
            rng.GetBytes(bytes);
            return Base64UrlEncode(bytes);
        }

        private string GenerateCodeChallenge(string codeVerifier)
        {
            using var sha256 = SHA256.Create();
            byte[] bytes = Encoding.ASCII.GetBytes(codeVerifier);
            byte[] hash = sha256.ComputeHash(bytes);
            return Base64UrlEncode(hash);
        }

        private string Base64UrlEncode(byte[] bytes)
        {
            return Convert.ToBase64String(bytes)
                .Replace("+", "-")
                .Replace("/", "_")
                .TrimEnd('=');
        }
    }
}