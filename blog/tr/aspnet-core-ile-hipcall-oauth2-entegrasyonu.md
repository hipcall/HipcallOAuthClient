---
title: "ASP.NET Core ile Hipcall OAuth2 entegrasyonu"
description: "ASP.NET Core ile Hipcall OAuth2 entegrasyonu kurun, token takası yapın ve profil verisini arka uç üzerinden çekin."
slug: aspnet-core-ile-hipcall-oauth2-entegrasyonu
lang: tr
locales: [en, tr]
pubDate: 2026-10-05
categories: [developers]
intent: informational
translationKey: how-to-integrate-hipcall-oauth2-in-aspnet-core
tags: [oauth2, dotnet, authentication, getting-started]
authors: [hipcall-team]
featured: false
draft: true
task: 05
status: review
---

## Genel bakış

OAuth2 Authorization Code akışı, web uygulamanızın Hipcall kullanıcılarının verilerine onların izniyle erişmesini sağlar. Kullanıcı Hipcall'da oturum açar, uygulamanıza bir yetkilendirme kodu döner ve arka ucunuz bu kodu bir erişim belirteci (access token) ile takas eder.

Bu sayfada Hipcall panelinde OAuth2 uygulaması oluşturmayı, ASP.NET Core Minimal API ile token takasını yapmayı ve elde edilen belirteçle profil verisini çekmeyi anlatıyoruz.

```mermaid
sequenceDiagram
    participant K as Kullanıcı
    participant U as Uygulama (Arka Uç)
    participant H as Hipcall
    K->>U: Giriş butonuna tıklar
    U->>H: /oauth/authorize yönlendirmesi
    H->>K: Oturum açma ekranı
    K->>H: Onay verir
    H->>U: /callback?code=abc123
    U->>H: POST /oauth/token (code + client_secret)
    H->>U: access_token
    U->>H: GET /api/v3/profile
    H->>U: Profil JSON verisi
```

## Başlamadan önce

Şunlara ihtiyacınız var:

- .NET 8 SDK veya üstü (`dotnet --version` ile kontrol edin).
- Dışarıdan erişilebilir bir HTTPS adresi. Yerel ortamda ngrok kullanın:
  ```bash
  ngrok http 5062
  ```
- Hipcall panelinde uygulama oluşturma yetkisi.

### OAuth2 uygulaması oluşturma

1. Settings > Developer (Ayarlar > Geliştirici) bölümüne gidin.
2. Yeni bir OAuth2 uygulaması oluşturun.
3. Detayları girin:
   - **Ad:** Uygulamanız için tanımlayıcı bir isim verin.
   - **Redirect URI:** ngrok adresinizi yazın. Örnek: `https://your-tunnel.ngrok-free.dev/callback`.
   - **Application Type:** Web Application seçin.
4. Oluşturulan **Client ID** ve **Client Secret** değerlerini kaydedin. Client Secret yalnızca bir kez gösterilir.

Ortam değişkenlerini tanımlayın:

```bash
export HIPCALL_CLIENT_ID="..."
export HIPCALL_CLIENT_SECRET="..."
export HIPCALL_REDIRECT_URI="https://your-tunnel.ngrok-free.dev/callback"
```

## Adım 1: Yetkilendirme URL'sini oluşturun

Kullanıcıyı Hipcall'ın oturum açma ekranına yönlendirmek için aşağıdaki URL yapısını kullanın:

```
https://use.hipcall.com.tr/oauth/authorize?response_type=code
  &client_id=$HIPCALL_CLIENT_ID
  &redirect_uri=$HIPCALL_REDIRECT_URI
  &scope=profile+email+offline_access
  &state=rastgele_bir_deger
```

| Parametre | Açıklama |
|---|---|
| `response_type` | Her zaman `code`. |
| `client_id` | Panelden aldığınız Client ID. |
| `redirect_uri` | Panelde kayıtlı Redirect URI ile birebir aynı olmalı. |
| `scope` | İstenen izinler. Boşluk yerine `+` ile ayırın. |
| `state` | CSRF koruması. Her isteğe özgü rastgele bir değer üretin ve callback'te doğrulayın. |

## Adım 2: Token takası

Kullanıcı onay verdikten sonra Hipcall, tarayıcıyı `redirect_uri` adresinize `?code=...` parametresiyle yönlendirir. Bu kodu erişim belirteci ile takas etmek için `/oauth/token` adresine POST isteği gönderin:

```bash
curl -X POST https://use.hipcall.com.tr/oauth/token \
  -d "client_id=$HIPCALL_CLIENT_ID" \
  -d "client_secret=$HIPCALL_CLIENT_SECRET" \
  -d "redirect_uri=$HIPCALL_REDIRECT_URI" \
  -d "grant_type=authorization_code" \
  -d "code=YETKILENDIRME_KODU"
```

## Adım 3: Profil verisini çekin

Elde ettiğiniz erişim belirteci ile API'ye istek atın:

```bash
curl -H "Authorization: Bearer $ACCESS_TOKEN" \
  https://use.hipcall.com.tr/api/v3/profile
```

## Betiğin tamamı

Aşağıdaki ASP.NET Core Minimal API uygulaması, callback'i karşılar, token takasını yapar ve profil verisini çeker:

```csharp
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpClient();
var app = builder.Build();

var clientId = Environment.GetEnvironmentVariable("HIPCALL_CLIENT_ID")
    ?? throw new InvalidOperationException("HIPCALL_CLIENT_ID ortam değişkeni bulunamadı.");
var clientSecret = Environment.GetEnvironmentVariable("HIPCALL_CLIENT_SECRET")
    ?? throw new InvalidOperationException("HIPCALL_CLIENT_SECRET bulunamadı.");
var redirectUri = Environment.GetEnvironmentVariable("HIPCALL_REDIRECT_URI")
    ?? throw new InvalidOperationException("HIPCALL_REDIRECT_URI bulunamadı.");

app.MapGet("/callback", async (string? code, string? error,
    IHttpClientFactory httpClientFactory) =>
{
    if (!string.IsNullOrEmpty(error))
        return Results.Content($"OAuth hatası: {error}", "text/plain");

    if (string.IsNullOrEmpty(code))
        return Results.Content("Yetkilendirme kodu bulunamadı.", "text/plain");

    var httpClient = httpClientFactory.CreateClient();

    var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
    {
        { "client_id", clientId },
        { "client_secret", clientSecret },
        { "redirect_uri", redirectUri },
        { "grant_type", "authorization_code" },
        { "code", code }
    });

    var tokenResponse = await httpClient.PostAsync(
        "https://use.hipcall.com.tr/oauth/token", tokenRequest);
    var tokenBody = await tokenResponse.Content.ReadAsStringAsync();

    if (!tokenResponse.IsSuccessStatusCode)
    {
        Console.WriteLine($"Token hatası ({tokenResponse.StatusCode}): {tokenBody}");
        return Results.Content($"Token alınamadı: {tokenBody}", "text/plain");
    }

    using var tokenDoc = JsonDocument.Parse(tokenBody);
    var accessToken = tokenDoc.RootElement.GetProperty("access_token").GetString();

    var profileReq = new HttpRequestMessage(HttpMethod.Get,
        "https://use.hipcall.com.tr/api/v3/profile");
    profileReq.Headers.Authorization =
        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

    var profileResponse = await httpClient.SendAsync(profileReq);
    var profileBody = await profileResponse.Content.ReadAsStringAsync();

    if (!profileResponse.IsSuccessStatusCode)
    {
        Console.WriteLine($"Profil hatası ({profileResponse.StatusCode}): {profileBody}");
        return Results.Content($"Profil alınamadı: {profileBody}", "text/plain");
    }

    return Results.Content(profileBody, "application/json");
});

app.Run();
```

## Başarılı cevap

Token takası başarılı olduğunda `/oauth/token` şu yapıda bir JSON döner:

```json
{
  "access_token": "g2gDbQAAACQ1NjI1...",
  "token_type": "Bearer",
  "expires_in": 7200,
  "refresh_token": "g2gDbQAAACQ3YTlk...",
  "scope": "profile email offline_access",
  "created_at": 1727884800
}
```

`/api/v3/profile` endpoint'inin cevabı:

```json
{
  "data": {
    "id": 4200,
    "email": "dev@example.com",
    "name": "Mehmet Y.",
    "time_zone": "Europe/Istanbul"
  }
}
```

## Hata aldığınızda

### invalid_grant

Token takası sırasında aşağıdaki cevabı alırsanız:

```json
{
  "error": "invalid_grant",
  "error_description": "The provided authorization grant is invalid, expired, revoked, does not match the redirection URI used in the authorization request, or was issued to another client."
}
```

İki olası neden vardır:

**Kod zaten kullanıldı.** OAuth2 yetkilendirme kodları tek kullanımlıktır. Tarayıcıda sayfa yenilendiğinde aynı `code` parametresi sunucuya tekrar gönderilir ve sunucu kodu reddeder. Sayfa yüklendikten sonra URL'deki `code` parametresini temizleyin:

```html
<script>
    if (window.history.replaceState) {
        window.history.replaceState({}, document.title, "/");
    }
</script>
```

Bu kod, tarayıcı geçmişini bozmadan adres çubuğunu kök dizine (`/`) döndürür. Sayfa yenilendiğinde sunucuya kod gitmez.

**Redirect URI uyuşmazlığı.** Token isteğindeki `redirect_uri`, panelde kayıtlı adresle birebir eşleşmelidir. Sondaki eğik çizgi (`/`) farkı bile uyuşmazlık sayılır.

### CORS hatası

Tarayıcıdan `fetch` veya `XMLHttpRequest` ile doğrudan `use.hipcall.com.tr/oauth/token` adresine istek atarsanız, tarayıcı CORS politikası gereği isteği engeller:

```
Access to fetch at 'https://use.hipcall.com.tr/oauth/token'
from origin 'https://your-app.com' has been blocked by CORS policy.
```

Token takasını her zaman C# arka ucunuz üzerinden yapın. Tarayıcı tarafında API çağrısı yapmayın.

### 401 Unauthorized

Erişim belirtecinin süresi dolmuşsa API, HTTP 401 döner:

```json
{
  "error": "unauthorized",
  "error_description": "The access token is invalid or has expired."
}
```

`refresh_token` kullanarak yeni bir erişim belirteci alın. Yenileme belirteci almak için ilk yetkilendirme isteğinde `offline_access` scope'unu eklemeniz gerekir.

## Kimlik bilgilerinizi güvende tutun

- Client Secret panelde yalnızca bir kez gösterilir. Kaybederseniz mevcut anahtarı silip yenisini oluşturun.
- Client Secret'ı kaynak koduna yazmayın. Ortam değişkeni veya güvenli bir yapılandırma yöneticisi (Azure Key Vault, AWS Secrets Manager) kullanın.
- Client Secret sızdıysa panelden derhal silin ve yeni bir uygulama oluşturun. Eski belirteçler geçersiz olur.
- Erişim belirteçlerini istemci tarafında (localStorage, cookie) saklamayın. Token'ı sunucu oturumunda tutun.

## Sonraki adımlar

- `refresh_token` ile belirteç yenileme akışını ekleyin.
- `/api/v3/contacts` endpoint'i ile kişi listesini çekin.
- [Hipcall API Referansı](https://use.hipcall.com.tr/api-docs/) sayfasından tüm endpoint'leri inceleyin.
- Sorularınızı [Hipcall Topluluk](https://community.hipcall.com/) platformunda paylaşın.
