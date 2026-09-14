# 02 — Create Wallet HTTP API

**Durum:** Tamamlandı  
**Tarih:** 2026-09-14

## Hedef

Çalışan Create Wallet Application akışını bir HTTP sözleşmesiyle dış dünyaya açmak ve isteğin controller'dan gerçek PostgreSQL'e kadar ilerlediğini otomatik testle doğrulamak.

Bu milestone'da yeni bir business kuralı eklenmedi. API katmanı, mevcut use-case'in transport adapter'ı olarak ele alındı.

## Başlangıç durumu

- `CreateWalletHandler` fake ve gerçek repository ile test edilebiliyordu.
- PostgreSQL mapping, migration, repository ve Unit of Work çalışıyordu.
- API projesi DI composition root görevini üstlenmişti.
- Template'ten gelen `WeatherForecast` endpoint'i vardı.
- Create Wallet HTTP request/response sözleşmesi ve merkezi hata formatı yoktu.

## Uygulanan akış

```text
POST /api/wallets
  -> CreateWalletRequest
  -> WalletsController
  -> CreateWalletCommand
  -> CreateWalletHandler
  -> IWalletRepository + IUnitOfWork
  -> LedgerlyDbContext
  -> PostgreSQL
  -> CreateWalletResponse + HTTP status
```

API; Domain'e, EF Core'a veya SQL'e doğrudan ulaşmaz. Controller yalnızca HTTP modelini Application command'ına çevirir ve sonucu HTTP response'una dönüştürür.

## HTTP sözleşmesi

Endpoint:

```http
POST /api/wallets
Content-Type: application/json

{
  "ownerId": "7d8ea830-5bd0-4f5f-bdc8-9d3c413ea55e",
  "currencyCode": "TRY"
}
```

Başarılı response:

```http
HTTP/1.1 201 Created
Content-Type: application/json

{
  "walletId": "..."
}
```

Request ve response modelleri API katmanında tutulur. Bunlar Domain entity'si değildir ve doğrudan `Wallet` serialize edilmez. Böylece dış sözleşme ile iç model birbirinden bağımsız değişebilir.

### Neden `Location` header yok?

`201 Created` ile çoğunlukla oluşturulan kaynağın adresi `Location` header içinde verilir. Ancak henüz `GET /api/wallets/{id}` endpoint'i yoktur. Çalışmayan bir URL üretmek yerine şimdilik yalnızca ID dönüldü. Query endpoint'i eklendiğinde `CreatedAtAction` ile gerçek adres üretilecektir.

## İki doğrulama sınırı

API input formatı ve business invariant aynı şey değildir:

```text
API contract validation
  -> currencyCode zorunlu ve tam 3 karakter

Domain validation
  -> ownerId boş olamaz
  -> currency desteklenen bir kod olmalı
```

`[ApiController]`, Data Annotation ihlalinde action çalışmadan otomatik `400 Bad Request` üretir. `TR` gibi iki karakterli bir kod bu sınırda reddedilir.

`Guid.Empty` veya formatı doğru fakat desteklenmeyen bir currency ise Domain/Application akışına ulaşır ve domain doğrulaması tarafından reddedilir.

Bu ayrımın amacı business kuralını yalnızca HTTP katmanına hapsetmemektir. Aynı use-case gelecekte message consumer veya job tarafından çağrılsa da Domain kuralları korunur.

## Merkezi hata dönüşümü

Controller içinde her exception için `try/catch` yazılmadı. `IExceptionHandler` kullanan merkezi middleware hataları RFC uyumlu `ProblemDetails` response'una çevirir:

| Uygulama sonucu | HTTP | Başlık |
|---|---:|---|
| `ArgumentException` | 400 | `Invalid request` |
| `WalletAlreadyExistsException` | 409 | `Wallet already exists` |
| Beklenmeyen exception | 500 | `An unexpected error occurred` |

Örnek duplicate response:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.10",
  "title": "Wallet already exists",
  "status": 409,
  "detail": "Owner '...' already has a 'TRY' wallet."
}
```

Beklenmeyen hatada exception detayı client'a açılmaz; server loguna exception ile birlikte yazılır.

### Bilinçli başlangıç trade-off'u

Şu anda Domain bazı geçersiz girdiler için genel `ArgumentException` kullanıyor ve API bunu `400`e çeviriyor. Hata çeşitleri arttığında `InvalidCurrencyException` gibi anlamlı hata tipleri veya Result modeli daha kesin bir sözleşme sağlayabilir. Gerçek ihtiyaç oluşmadan ek hata hiyerarşisi kurulmadı.

## OpenAPI ve Scalar

.NET OpenAPI document üretimi korunurken tarayıcıdan kullanılabilen Scalar arayüzü eklendi:

```text
Scalar UI    -> https://localhost:7092/scalar/v1
OpenAPI JSON -> https://localhost:7092/openapi/v1.json
```

Bu endpoint'ler `Development` ve otomatik doğrulama için `IntegrationTests` ortamlarında map edilir; Production'da açık değildir. Scalar, test runner ekranı değildir; HTTP endpoint'lerini keşfetmek ve elle istek göndermek için etkileşimli API dokümantasyonudur.

`WeatherForecast` örneği silindi ve `Ledgerly.Api.http` gerçek Create Wallet isteğiyle güncellendi.

## Functional integration test

`WebApplicationFactory<Program>`, Kestrel portu açmadan gerçek ASP.NET Core pipeline'ını test içinde ayağa kaldırır. Bunun çalışabilmesi için top-level `Program`, test assembly'sine görünür bir partial type ile tamamlandı:

```csharp
app.Run();

public partial class Program;
```

Test akışı:

```text
HttpClient
  -> routing
  -> model binding ve validation
  -> exception middleware
  -> controller
  -> handler
  -> EF Core
  -> gerçek ledgerly_tests PostgreSQL database'i
```

Test factory, `appsettings.IntegrationTests.json` dosyasındaki bağlantıyı API host'una verir ve test başlangıcında migration'ları uygular. `LEDGERLY_TEST_DB_CONNECTION_STRING` tanımlıysa bu değer önceliklidir.

HTTP testleri:

1. Geçerli istek `201` döner ve wallet gerçekten database'de bulunur.
2. Aynı owner/currency ikinci kez gönderildiğinde `409 ProblemDetails` döner.
3. Boş owner ID domain doğrulamasından `400 ProblemDetails` döner.
4. İki karakterli currency API contract validation'dan `400 ValidationProblemDetails` döner.

Testler benzersiz owner ID üretir ve oluşturdukları satırı `finally` bloğunda temizler. Persistence testleriyle aynı xUnit collection içinde seri çalıştırıldıkları için paylaşılan test database'inde birbirlerini etkilemezler.

## Komutlar

API'yi çalıştırmak:

```powershell
docker compose up -d
dotnet dev-certs https --trust # yalnızca ilk lokal kurulumda gerekiyorsa
dotnet run --project src/Ledgerly.Api/Ledgerly.Api.csproj --launch-profile https
```

Örnek isteği IDE'den göndermek için `src/Ledgerly.Api/Ledgerly.Api.http` kullanılabilir.

Yalnızca HTTP testleri:

```powershell
dotnet test tests/Ledgerly.IntegrationTests/Ledgerly.IntegrationTests.csproj `
  --filter "FullyQualifiedName~CreateWalletHttpTests"
```

Tüm testler:

```powershell
dotnet test Ledgerly.slnx
```

## VS Code geliştirme deneyimi

Repository'deki `.vscode` dizini ekipçe paylaşılabilen run/debug desteğini içerir:

```text
launch.json     -> Ledgerly.Api HTTPS debug profili
tasks.json      -> Docker, build, test ve migration komutları
extensions.json -> C# Dev Kit ve REST Client önerileri
```

`Ledgerly.Api (HTTPS)` profili başlatıldığında `preLaunchTask` sırasıyla PostgreSQL'i başlatır, local .NET araçlarını restore eder ve development database migration'larını uygular. Migration komutu API'yi de build eder. API, `Properties/launchSettings.json` içindeki `https` profilini kullanır; böylece `dotnet run`, Visual Studio ve VS Code aynı port ve environment ayarlarını paylaşır.

API hazır olduğunda `serverReadyAction`, Scalar sayfasını `https://localhost:7092/scalar/v1` adresinde açar. Breakpoint'ler controller, handler, repository ve exception handler boyunca kullanılabilir.

Testler için iki yol vardır:

- C# Dev Kit Testing görünümünde testin yanındaki Run/Debug komutları
- `Terminal > Run Task` altında `Ledgerly: test all` veya `Ledgerly: test integration`

`.vscode` altındaki kişisel olmayan üç dosya `.gitignore` tarafından özellikle source control'e dahil edilir. Kullanıcıya özel VS Code ayarları repository'ye eklenmez.

HTTPS developer certificate makineye özgüdür ve repository'de tutulmaz. Eksikse `Ledgerly: trust HTTPS certificate` task'ı bir kez kullanıcı tarafından çalıştırılır; işletim sisteminin güven onayı otomatikleştirilmez.

## Doğrulama kanıtı

- API ve bütün solution 0 warning, 0 error ile build edildi.
- 12 Domain testi başarılı.
- 2 Application testi başarılı.
- 2 doğrudan persistence, 4 HTTP functional ve 1 API dokümantasyon integration testi başarılı.
- Toplam 21 test başarılı.
- HTTP testleri ayrı `ledgerly_tests` PostgreSQL database'ini kullanıyor.

## Şu anda garanti edilen ve edilmeyen

Garanti edilen baseline:

- Tekil Create Wallet isteği doğru katmanlardan geçerek kaydedilir.
- Sıralı duplicate istek kullanıcıya `409` olarak döner.
- Bilinen geçersiz girdiler tutarlı `400` formatına çevrilir.

Henüz garanti edilmeyen:

- Aynı owner/currency için iki eşzamanlı isteğin ikisinin de deterministik şekilde `201/409` sonucu alması
- Database unique constraint exception'ının kontrollü application hatasına çevrilmesi
- Idempotency key ile aynı isteğin güvenle tekrar gönderilmesi
- Authentication ve authorization
- Rate limiting ve observability

## Sonraki adım

İki paralel Create Wallet isteğinin `ExistsAsync` kontrolünü aynı anda geçebildiği yarış koşulunu kontrollü biçimde reproduce etmek. Ardından PostgreSQL unique constraint ihlalini yakalayıp ikinci isteği `500` yerine deterministik `409 Conflict` sonucuna çevirmek.
