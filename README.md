# Ledgerly

Ledgerly, dijital cüzdan ve ödeme problemlerini küçük bir başlangıç sistemi üzerinde senaryo bazlı olarak incelemek için geliştirilen bir öğrenme projesidir.

Proje doğrudan production-grade bir mikroservis platformu olarak başlamaz. Her mimari yetenek, gerçek bir problem yeniden üretildikten ve alternatifleri değerlendirildikten sonra eklenir.

Dokümantasyonun başlangıç noktası: [Project Charter](docs/00-project-charter.md)

## Çalışma döngüsü

```text
Baseline
  -> Problem
  -> Problemi reproduce et
  -> Gözlem ve kanıt
  -> Root cause
  -> Alternatif çözümler
  -> Trade-off
  -> Karar
  -> Implementasyon
  -> Test ve doğrulama
  -> Dokümantasyon
```

Detaylı kayıt düzeni için [dokümantasyon rehberine](docs/README.md) bakın.

## Uygulamayı çalıştırma

```powershell
docker compose up -d
dotnet tool restore
dotnet dev-certs https --trust # yalnızca ilk lokal kurulumda gerekiyorsa
dotnet ef database update `
  --project src/Ledgerly.Infrastructure/Ledgerly.Infrastructure.csproj `
  --startup-project src/Ledgerly.Api/Ledgerly.Api.csproj `
  -- --environment Development
dotnet run --project src/Ledgerly.Api/Ledgerly.Api.csproj --launch-profile https
```

Development ortamında API arayüzü `https://localhost:7092/scalar/v1`, OpenAPI belgesi ise `https://localhost:7092/openapi/v1.json` adresindedir.

İlk endpoint:

```http
POST /api/wallets
Content-Type: application/json

{
  "ownerId": "7d8ea830-5bd0-4f5f-bdc8-9d3c413ea55e",
  "currencyCode": "TRY"
}
```

POST başarılı olduğunda 201 gövdesinde walletId ve Location header'ında gerçek kaynak adresi döner. Bu adresi GET ile takip edebilirsiniz:

```http
GET /api/wallets/{walletId}
```

Mevcut wallet için 200 ve detaylar, bulunamayan GUID için 404 ProblemDetails döner. Okuma ve yazma aynı PostgreSQL database'ini kullanır. Ayrıntılar [Get Wallet milestone](docs/journey/03-get-wallet-query.md) belgesinde.

Tüm testler:

```powershell
dotnet test Ledgerly.slnx
```

## VS Code ile çalıştırma ve debug

Önerilen eklentileri yükledikten sonra **Run and Debug** görünümünden `Ledgerly.Api (HTTPS)` profilini seçip `F5`e basın. Bu profil PostgreSQL container'ını başlatır, local .NET araçlarını restore eder, migration'ları development database'ine uygular, API'yi debugger ile açar ve hazır olduğunda Scalar sayfasını tarayıcıda gösterir.

- `F5`: debugger ile çalıştırır.
- `Ctrl+F5`: debugger olmadan çalıştırır.
- `Ctrl+Shift+B`: varsayılan solution build task'ını çalıştırır.
- `Terminal > Run Task`: PostgreSQL, test ve migration task'larını listeler.
- Sol menüdeki **Testing** görünümü: xUnit testlerini tek tek veya toplu çalıştırıp debug eder.

İlk HTTPS kullanımında developer certificate eksikse `Terminal > Run Task > Ledgerly: trust HTTPS certificate` seçeneğini kullanın veya terminalden bir kez `dotnet dev-certs https --trust` çalıştırın.

## Ledger domain modeli

Dengeli `JournalEntry` ve immutable `Posting` modeli ile ledger persistence tamamlandı. Development/test ortamında [test bakiyesi yatırma](docs/journey/07-test-deposit.md), tüm ortamlarda ise wallet-to-wallet TRY transfer endpoint'i hazırdır. [Model ve örnekler](docs/domain/03-double-entry-ledger.md), [ledger domain test kanıtı](docs/journey/04-ledger-domain.md).

`LedgerAccount` ile wallet yükümlülük hesabı ve simüle edilmiş test fon hesabının domain modeli de hazır. POST `/api/wallets` yalnızca wallet oluşturur; eksik ledger hesapları ilk test yatırmasında hazırlanır. [Hesap modeli ve sınırları](docs/domain/04-ledger-account.md).

Hesap/journal/posting tabloları ve atomik kayıt için [persistence belgesine](docs/architecture/03-ledger-persistence.md), yarım kayıt deneyini çalıştırmak için [LAB-002](docs/labs/002-journal-atomicity/README.md) sayfasına bakın.

Hesabın muhasebe türü `Type`, kullanım amacı ise `Purpose` ile tutulur. Test fonu sorgusu açıkça `TestFunding` amacını arar. [Type ve Purpose ayrımı](docs/domain/05-ledger-account-purpose.md).

Test yatırmasında tekrar deneme için `Idempotency-Key` zorunludur: aynı key ve tutar ilk makbuzu döner, farklı tutar `409` verir. [Senaryo, karar ve testler](docs/labs/004-test-deposit-idempotency/README.md).

Wallet-to-wallet TRY transferi kaynak ve hedef bakiyeyi dengeli journal ile tek transaction'da değiştirir. Aynı kaynak bakiyeyi harcayan paralel transferlerden yalnızca biri commit edilir. [Double-spending deneyi](docs/labs/005-wallet-transfer-double-spending/README.md).
