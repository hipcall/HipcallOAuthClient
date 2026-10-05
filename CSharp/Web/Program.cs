using System.Text.Json;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/callback", async (string? code, string? error, string? error_description, IHttpClientFactory httpClientFactory, IConfiguration configuration) =>
{
    if (!string.IsNullOrEmpty(error))
    {
        return Results.Content($@"
            <div style='font-family: sans-serif; text-align: center; margin-top: 50px;'>
                <h3 style='color:red;'>OAuth Hatası: {error}</h3>
                <p>{error_description}</p>
                <br/>
                <a href='/' style='padding: 10px 20px; background: #3b82f6; color: white; text-decoration: none; border-radius: 5px;'>Anasayfaya Dön</a>
            </div>", "text/html; charset=utf-8");
    }

    if (string.IsNullOrEmpty(code))
    {
        return Results.Content($@"
            <div style='font-family: sans-serif; text-align: center; margin-top: 50px;'>
                <h3>Hata: Yetkilendirme kodu (code) bulunamadı.</h3>
                <p>Bunun sebebi muhtemelen sayfaya doğrudan (login olmadan) girmeniz veya sayfayı sonradan yenilemenizdir.</p>
                <br/>
                <a href='/' style='padding: 10px 20px; background: #3b82f6; color: white; text-decoration: none; border-radius: 5px;'>Anasayfadan tekrar giriş yapmayı deneyin</a>
            </div>", "text/html; charset=utf-8");
    }

    var tokenEndpoint = "https://use.hipcall.com.tr/oauth/token";
    var clientId = configuration["OAuth:ClientId"] ?? throw new InvalidOperationException("ClientId is missing in appsettings.json");
    var clientSecret = configuration["OAuth:ClientSecret"] ?? throw new InvalidOperationException("ClientSecret is missing in appsettings.json");
    var redirectUri = configuration["OAuth:RedirectUri"] ?? throw new InvalidOperationException("RedirectUri is missing in appsettings.json");

    var httpClient = httpClientFactory.CreateClient();
    
    var requestContent = new FormUrlEncodedContent(new Dictionary<string, string>
    {
        { "client_id", clientId },
        { "client_secret", clientSecret },
        { "redirect_uri", redirectUri },
        { "grant_type", "authorization_code" },
        { "code", code }
    });

    try
    {
        var response = await httpClient.PostAsync(tokenEndpoint, requestContent);
        var jsonResponse = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            if (jsonResponse.Contains("invalid_grant") || jsonResponse.Contains("Invalid authorization code"))
            {
                return Results.Content($@"
                <div style='font-family: sans-serif; text-align: center; margin-top: 50px;'>
                    <h3 style='color:#b45309;'>Güvenlik Kodu Süresi Doldu (Sayfa Yenilendi)</h3>
                    <p>OAuth2 kuralları gereği, URL'deki giriş kodları (code) <strong>sadece bir kez</strong> kullanılabilir. Sayfayı yenilediğinizde aynı kod tekrar kullanılmaya çalışıldığı için sunucu güvenlik gereği reddetti.</p>
                    <p>Gerçek sistemlerde Token alındıktan sonra URL temizlenir ve kullanıcı temiz bir sayfaya yönlendirilir (Redirect). Böylece sayfa yenilenirse hata alınmaz. Test ortamında olduğumuz için kodu doğrudan bu URL'de işliyoruz.</p>
                    <br/>
                    <a href='/' style='padding: 10px 20px; background: #3b82f6; color: white; text-decoration: none; border-radius: 5px;'>Anasayfadan Tekrar Test Et</a>
                </div>", "text/html; charset=utf-8");
            }
            return Results.Content($"<h3 style='color:red;'>Giriş Başarısız (Token alınamadı)</h3><p>{jsonResponse}</p>", "text/html; charset=utf-8");
        }

        using var jsonDoc = JsonDocument.Parse(jsonResponse);
        var accessToken = jsonDoc.RootElement.GetProperty("access_token").GetString();
        
        var profileEndpoint = "https://use.hipcall.com.tr/api/v3/profile";
        var profileRequest = new HttpRequestMessage(HttpMethod.Get, profileEndpoint);
        profileRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        var profileResponse = await httpClient.SendAsync(profileRequest);
        var profileJsonResponse = await profileResponse.Content.ReadAsStringAsync();
        var profileStatusCode = (int)profileResponse.StatusCode;

        var contactsEndpoint = "https://use.hipcall.com.tr/api/v3/contacts?limit=10";
        var contactsRequest = new HttpRequestMessage(HttpMethod.Get, contactsEndpoint);
        contactsRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        var contactsResponse = await httpClient.SendAsync(contactsRequest);
        var contactsJsonResponse = await contactsResponse.Content.ReadAsStringAsync();
        var contactsStatusCode = (int)contactsResponse.StatusCode;

        var html = $@"
        <!DOCTYPE html>
        <html lang='tr'>
        <head>
            <meta charset='UTF-8'>
            <meta name='viewport' content='width=device-width, initial-scale=1.0'>
            <title>Müşteri Paneli</title>
            <style>
                body {{ font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; background-color: #f3f4f6; color: #1f2937; display: flex; justify-content: center; padding: 40px 20px; margin: 0; }}
                .card {{ background-color: white; border-radius: 12px; box-shadow: 0 4px 15px rgba(0,0,0,0.05); max-width: 800px; width: 100%; padding: 30px; }}
                .header {{ border-bottom: 2px solid #e5e7eb; padding-bottom: 15px; margin-bottom: 20px; text-align: center; }}
                .header-icon {{ font-size: 48px; margin-bottom: 10px; color: #10b981; }}
                h2 {{ margin: 0; color: #111827; font-size: 26px; }}
                p.subtitle {{ color: #6b7280; margin-top: 8px; font-size: 15px; }}
                
                .tabs {{ display: flex; border-bottom: 2px solid #e5e7eb; margin-bottom: 20px; }}
                .tab {{ padding: 10px 20px; cursor: pointer; font-weight: 600; color: #6b7280; border-bottom: 2px solid transparent; margin-bottom: -2px; transition: 0.2s; }}
                .tab:hover {{ color: #3b82f6; }}
                .tab.active {{ color: #3b82f6; border-bottom-color: #3b82f6; }}
                
                .tab-content {{ display: none; }}
                .tab-content.active {{ display: block; }}
                
                .data-container {{ background-color: #1f2937; border-radius: 8px; padding: 0; overflow: hidden; }}
                .data-header {{ background-color: #374151; padding: 10px 15px; color: #e5e7eb; font-size: 13px; font-weight: 600; display: flex; justify-content: space-between; }}
                .status-code {{ padding: 2px 8px; border-radius: 4px; font-size: 12px; font-weight: bold; }}
                .status-200 {{ background-color: #10b981; color: white; }}
                .status-error {{ background-color: #ef4444; color: white; }}
                pre {{ color: #a7f3d0; padding: 20px; margin: 0; overflow-x: auto; font-size: 14px; line-height: 1.6; font-family: 'Consolas', 'Monaco', monospace; max-height: 500px; }}
            </style>
        </head>
        <body>
            <div class='card'>
                <div class='header'>
                    <div class='header-icon'>✓</div>
                    <h2>Hesabınız Bağlandı</h2>
                    <p class='subtitle'>Aşağıdaki sekmelerden görüntülemek istediğiniz veriyi seçebilirsiniz.</p>
                </div>
                
                <div class='tabs'>
                    <div class='tab active' onclick='showTab(this, ""profile"")'>Profil Bilgileri</div>
                    <div class='tab' onclick='showTab(this, ""contacts"")'>Kişiler (Contacts)</div>
                </div>

                <div class='content'>
                    <div id='tab-profile' class='tab-content active'>
                        <div class='data-container'>
                            <div class='data-header'>
                                <span>/api/v3/profile</span>
                                <span class='status-code {(profileStatusCode == 200 ? "status-200" : "status-error")}'>HTTP {profileStatusCode}</span>
                            </div>
                            <pre id='jsonDisplay'></pre>
                        </div>
                    </div>
                    
                    <div id='tab-contacts' class='tab-content'>
                        {(contactsJsonResponse.Replace(" ", "").Contains("\"count\":0") || contactsJsonResponse.Replace(" ", "").Contains("\"data\":[]") || contactsStatusCode != 200 ? $@"
                        <div style='background-color:#fffbeb; color:#b45309; padding:15px; border-radius:8px; margin-bottom:15px; border-left:4px solid #f59e0b; font-size:14px; line-height:1.5;'>
                            <strong>⚠️ Veriler Neden Boş veya Hatalı Geldi?</strong>
                            <ul style='margin-top:5px; margin-bottom:0; padding-left:20px;'>
                                <li><strong>Yetki Eksikliği:</strong> Uygulamamız `contacts:read` (Kişiler) okuma iznini talep etmedi. Hipcall API, yetkisi olmayan tokenlara genelde doğrudan hata (403) atmak yerine güvenlik amaçlı (satır bazlı filtreleme yaparak) <strong>boş bir liste döndürebilir.</strong></li>
                                <li>Veya hesabınızda gerçekten hiç kayıtlı kişi bulunmuyor olabilir.</li>
                            </ul>
                        </div>" : "")}
                        <div class='data-container'>
                            <div class='data-header'>
                                <span>/api/v3/contacts?limit=10</span>
                                <span class='status-code {(contactsStatusCode == 200 ? "status-200" : "status-error")}'>HTTP {contactsStatusCode}</span>
                            </div>
                            <pre id='contactsJsonDisplay'></pre>
                        </div>
                    </div>
                </div>
            </div>
            
            <script>
                if (window.history.replaceState) {{
                    window.history.replaceState({{}}, document.title, ""/"");
                }}

                function showTab(element, tabName) {{
                    document.querySelectorAll('.tab').forEach(t => t.classList.remove('active'));
                    document.querySelectorAll('.tab-content').forEach(c => c.classList.remove('active'));
                    
                    element.classList.add('active');
                    document.getElementById('tab-' + tabName).classList.add('active');
                }}

                const profileData = {profileJsonResponse};
                const contactsData = {contactsJsonResponse};
                document.getElementById('jsonDisplay').textContent = JSON.stringify(profileData, null, 4);
                document.getElementById('contactsJsonDisplay').textContent = JSON.stringify(contactsData, null, 4);
            </script>
        </body>
        </html>";
        
        return Results.Content(html, "text/html; charset=utf-8");
    }
    catch (Exception ex)
    {
        return Results.Content($"<h3>Bir sistem hatası oluştu:</h3><p>{ex.Message}</p>", "text/html; charset=utf-8");
    }
});

app.Run();
