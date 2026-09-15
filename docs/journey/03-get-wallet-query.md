# 03 — Get Wallet Query ve Kaynak Adresi

**Durum:** Tamamlandı

**Tarih:** 2026-09-15

## Problem ve başlangıç

Create Wallet bir wallet oluşturup ID dönüyordu. Bu ID ile kaynağı okuyacak GET endpoint'i yoktu; dolayısıyla 201 yanıtında çalışır bir Location adresi de sunulmuyordu. Concurrency lab'ı tamamlandıktan sonra ilk query use-case'i olarak ID ile wallet okumayı seçtik.

Bu aşama yeni bir finansal işlem eklemez. Domain kuralları ve database şeması değişmez.

## Beklenen davranış

- `GET /api/wallets/{walletId}` mevcut wallet için 200 ve detayları döndürür.
- GUID biçimindeki ID bulunamazsa 404 ProblemDetails döner.
- Okuma yeni kayıt oluşturmaz ve bir save gerektirmez.
- POST 201 gövdesi `{ walletId }` olarak kalır; Location gerçek GET route'una işaret eder.
- Okuma ve yazma aynı PostgreSQL database'ini ve wallets tablosunu kullanır.

Route `{walletId:guid}` kullanır. GUID olmayan path değeri bu action'a eşleşmez; burada belgelenen `Wallet not found` gövdesi action'a ulaşan, bulunamayan GUID içindir. Guid.Empty de bulunamayan bir ID olarak ele alınır.

## Reproduce / Red kanıtı

Implementasyondan önce üç HTTP test case'i çalıştırıldı:

1. Mevcut wallet'ı GET ile okumak: 200 yerine route 404 döndü.
2. Olmayan wallet için anlamlı 404: route 404 vardı, fakat application/problem+json gövdesi yoktu.
3. POST Location adresini takip etmek: Location null geldi.

Üçü de beklenen eksik davranış nedeniyle başarısız oldu. Bu kontrollerin ardından endpoint ve query eklendi.

## Alternatifler ve karar

| Seçenek | Fayda | Bedel |
|---|---|---|
| Controller'dan DbContext sorgulamak | Kısa başlangıç | HTTP katmanını EF'e bağlar, query use-case'i görünmez kalır |
| Mevcut repository + ayrı query handler | Application EF'den bağımsız, küçük değişiklik | Read ve write portu ortak; yeni metot fake/decorator implementasyonlarına da eklenir |
| Ayrı read portu ve doğrudan DTO projection | Okuma modelini domain aggregate'ından bağımsız büyütebilir | Bu küçük sorgu için ek interface ve adapter |
| Ayrı read database/projection | Farklı okuma ihtiyaçları için bağımsız model/ölçekleme | Senkronizasyon, projection lag ve operasyon maliyeti; şu an kanıtlanmış ihtiyaç yok |

Mevcut `IWalletRepository`ye `GetByIdAsync` eklemek ve ayrı GetWalletHandler kullanmak seçildi. Yeni MediatR, generic repository, cache veya read database eklenmedi.

Bu kararın sınırı açıktır: GET için repository tracked aggregate değil, kaydetmek üzere takip edilmeyen bir snapshot döndürür. Gelecekte transfer/update use-case'leri bu metodu çağırıp otomatik tracking varsaymamalıdır. Böyle bir ihtiyaçta ayrı yükleme sözleşmesi veya read portu değerlendirilir.

## Uygulanan akış

```text
GET /api/wallets/{walletId}
  -> WalletsController.GetById
  -> GetWalletQuery
  -> GetWalletHandler
  -> IWalletRepository.GetByIdAsync
  -> WalletRepository: AsNoTracking + ID filtresi
  -> aynı LedgerlyDbContext / PostgreSQL wallets tablosu
  -> GetWalletResult
  -> GetWalletResponse / 200
```

Handler bulunamayan kayıt için null döndürür. API bunu 404 ProblemDetails yapar. Yokluk, bu query'nin beklenen sonucudur; yeni bir exception hiyerarşisi veya genel Result framework'ü eklenmedi. Mevcut duplicate exception davranışı değişmedi.

Query handler `IUnitOfWork` ve `TimeProvider` kullanmaz: kaydetmez ve yeni zaman üretmez; saklanan CreatedAtUtc değerini döndürür. CancellationToken sorguya kadar taşınır.

## CQRS ne kadar ayrıldı?

```text
CreateWalletCommand -> CreateWalletHandler -> INSERT
                                              |
                                   PostgreSQL / wallets
                                              |
GetWalletQuery      -> GetWalletHandler     -> SELECT
```

Command ve query, handler ve sonuç modelleri ayrıdır. Repository, DbContext tipi ve database ortaktır. CQRS ayrı database veya broker gerektirmez. MongoDB projection ve eventual consistency, roadmap'in ilerleyen aşamalarındadır. `ledgerly_tests` bir read database değil, integration testleri için ayrı ortamdır.

## HTTP sözleşmesi

Mevcut wallet:

```json
{
  "walletId": "7d8ea830-5bd0-4f5f-bdc8-9d3c413ea55e",
  "ownerId": "d860e2a1-b80f-43d9-bfe0-21aa5ab7a1c7",
  "currencyCode": "TRY",
  "status": "Active",
  "balance": 0,
  "createdAtUtc": "2026-09-15T12:00:00+00:00"
}
```

Status, API'de okunabilir string olarak sunulur. Application result Domain enum'unu taşır; API dönüşümü açıkça yapar. Wallet entity doğrudan serialize edilmez.

Bulunamayan wallet:

```json
{
  "title": "Wallet not found",
  "status": 404,
  "detail": "Wallet '...' was not found."
}
```

201 Location:

```csharp
return CreatedAtAction(nameof(GetById), new { walletId = result.WalletId }, response);
```

Adres elle string birleştirilerek değil gerçek route'tan üretilir. İstemci POST yanıtındaki Location'ı GET ile takip edebilir. GET sonucu, POST'taki walletId ile aynı kaynağa aittir.

## Test ve doğrulama

2026-09-15, macOS ARM64, SDK 10.0.400/runtime 10.0.11, Docker PostgreSQL 18.6-alpine ve gerçek ledgerly_tests:

```text
Domain             12 passed
Application         4 passed
IntegrationTests   17 passed
Toplam             33 passed, 0 failed, 0 skipped
```

Doğrulananlar:

- Application: Var olan wallet'ın alanları sonuca taşınır; ID ve CancellationToken repository'ye iletilir. Olmayan kayıt null olur. Fake, query'nin Add veya owner/currency pre-check çağırmasını reddeder.
- Persistence: Wallet ID ile gerçek PostgreSQL'den okunur; change tracker'a eklenmez. Okunan snapshot'ın alanı testte değiştirilip save çağrılsa da database bakiyesi değişmez.
- HTTP: Ayrı scope'ta kaydedilen wallet tüm alanlarıyla 200 döner. Başka ID, tabloda wallet olsa bile o wallet'ı dönmez. Bulunamayan ID doğru 404 ProblemDetails döner.
- POST: 201 Location'ın doğru adres olduğu ve GET ile aynı wallet'a ulaştığı doğrulanır.
- OpenAPI: GET route'u ve 200/404, POST 201 sözleşmeleri bulunur. Scalar route'u çalışır.
- Önceki concurrency 201/409, duplicate ve hata çeviri testleri yeşil kalır.

IntegrationTests içindeki 17 case'in 3'ü önceki hata-enjeksiyon kontrolleridir; tamamı gerçek provider arızası deneyi değildir. Bu milestone browser UI veya yüksek yük benchmark'ı içermez.

## Çalıştırma

macOS / zsh / Bash, Ledgerly kökünde:

```bash
docker compose up -d
dotnet test Ledgerly.slnx
dotnet run --project src/Ledgerly.Api/Ledgerly.Api.csproj --launch-profile https
```

Kullanıcı SDK kurulumu PATH'te görünmüyorsa bu komutlarda `dotnet` yerine `~/.dotnet/dotnet` kullanılabilir. Testler doğrulamada tam SDK yoluyla çalıştırıldı.

Scalar: `https://localhost:7092/scalar/v1`. POST ile wallet oluştur; yanıtın Location adresini veya walletId'sini GET isteğinde kullan. `src/Ledgerly.Api/Ledgerly.Api.http` dosyasında iki istek için örnek vardır.

## Mülakat özeti

> İlk query use-case'ini mevcut PostgreSQL üzerinde ekledim. Command ve query handler'larını ayırdım; ayrı database gereksinimi olmadığı için aynı repository ve tabloyu kullandım. Read sorgusu AsNoTracking çalışıyor, handler save çağırmıyor ve API domain entity yerine response modeli dönüyor. Kayıt yokluğunu 404 ProblemDetails olarak sundum. Gerçek GET route'u oluşunca POST yanıtına CreatedAtAction ile Location ekledim ve bu adresi takip eden HTTP testi yazdım.

## Sonraki adım

Wallet oluşturma ve ID ile okuma tamamlandı. Finansal çekirdek için double-entry ledger, test bakiyesi yatırma ve transferin invariant/transaction sınırları tasarlanacak. Mevcut Balance alanını güncellemek tek başına finansal ledger implementasyonu sayılmaz.
