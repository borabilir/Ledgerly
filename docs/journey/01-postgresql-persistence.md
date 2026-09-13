# 01 — PostgreSQL Persistence Baseline

**Durum:** Tamamlandı  
**Tarih:** 2026-09-14

## Hedef

Create Wallet use-case'ini yalnızca in-memory fake'lerle çalışan bir Application akışından çıkarıp gerçek PostgreSQL'e kadar bağlamak.

Bu milestone production'daki bütün veri problemlerini çözmez. Sonraki concurrency, idempotency ve resilience senaryolarının reproduce edilebileceği küçük ve gözlemlenebilir bir persistence zemini kurar.

## Başlangıç durumu

- `Wallet` aggregate'i ve `Currency` value object'i Domain katmanında bulunuyordu.
- `CreateWalletHandler`, `IWalletRepository` ve `IUnitOfWork` portlarını kullanıyordu.
- Application testlerinde repository ve Unit of Work fake implementasyonlarla temsil ediliyordu.
- Gerçek database adapter'ı, migration ve persistence integration testi yoktu.

## Uygulanan sıra

```text
PostgreSQL container
  -> EF Core ve Npgsql
  -> LedgerlyDbContext
  -> Wallet mapping
  -> Repository ve Unit of Work adapter'ları
  -> Dependency Injection
  -> InitialCreate migration
  -> Gerçek PostgreSQL integration testleri
```

## PostgreSQL container

Repository kökündeki `compose.yml`, lokal geliştirme için tek bir PostgreSQL container'ı çalıştırır:

```powershell
docker compose up -d
docker compose ps
```

Container'ın `healthy` olması yalnızca process'in çalıştığını değil, PostgreSQL'in bağlantı kabul edebildiğini gösterir.

Repository'deki `ledgerly_dev` parolası yalnızca izole lokal geliştirme credential'ıdır. Production bağlantısı source control dışında environment variable veya secret store üzerinden verilmelidir.

## DbContext ve mapping

`LedgerlyDbContext`, Infrastructure katmanında bulunur ve Application'ın `IUnitOfWork` portunu uygular.

`Wallet` eşlemesi `IEntityTypeConfiguration<Wallet>` ile ayrı dosyada tutulur. Böylece Domain modeli EF Core attribute'larına bağımlı olmaz ve `OnModelCreating` büyümez.

```text
Wallet.Id            -> uuid, primary key
Wallet.OwnerId       -> uuid
Wallet.Currency      -> varchar(3), value conversion
Wallet.Status        -> integer
Wallet.Balance       -> numeric(19,4)
Wallet.CreatedAtUtc  -> timestamp with time zone
```

`Currency`, davranış taşıyan bir value object olmasına rağmen PostgreSQL'de tek bir string kolonda tutulur. EF Core value converter iki yönlü dönüşümü yapar:

```text
Currency.Code -> database string
database string -> Currency.FromCode(code)
```

Domain'in ürettiği `Guid` değerleri korunur; bu nedenle `Id` için `ValueGeneratedNever` kullanılır.

## Tutarlılık kuralı

Bir owner'ın aynı currency için birden fazla wallet'ı olmaması hem Application pre-check'i hem de database unique index'iyle temsil edilir:

```text
ux_wallets_owner_id_currency (owner_id, currency)
```

Pre-check kullanıcıya anlaşılır hata vermek için yararlıdır ancak tek başına concurrency güvenliği sağlamaz. İki istek kontrolü aynı anda geçebilir. Unique index son savunma hattıdır; yarış durumu ve database exception dönüşümü ayrı bir concurrency lab'ında ele alınacaktır.

## Repository, Unit of Work ve DI

Application yalnızca portları bilir:

```text
IWalletRepository
IUnitOfWork
```

Infrastructure bu portları gerçek adapter'lara bağlar:

```text
IWalletRepository -> WalletRepository
IUnitOfWork        -> LedgerlyDbContext
```

Kayıtlar scoped yapılır. Aynı HTTP request veya DI scope içindeki repository ve Unit of Work aynı `LedgerlyDbContext` instance'ını kullanır:

```text
CreateWalletHandler
  -> WalletRepository.Add(wallet)
  -> LedgerlyDbContext change tracker
  -> IUnitOfWork.SaveChangesAsync()
  -> aynı LedgerlyDbContext
  -> PostgreSQL transaction
```

Composition root API'nin `Program.cs` dosyasıdır. Infrastructure kendi registration ayrıntısını `AddInfrastructure` extension metodu üzerinden dışarı açar.

## Migration

Local EF aracı repository manifestinde sabitlenir:

```powershell
dotnet tool restore
```

İlk migration:

```powershell
dotnet ef migrations add InitialCreate `
  --project src/Ledgerly.Infrastructure/Ledgerly.Infrastructure.csproj `
  --startup-project src/Ledgerly.Api/Ledgerly.Api.csproj `
  --output-dir Persistence/Migrations `
  -- --environment Development
```

Database'e uygulama:

```powershell
dotnet ef database update `
  --project src/Ledgerly.Infrastructure/Ledgerly.Infrastructure.csproj `
  --startup-project src/Ledgerly.Api/Ledgerly.Api.csproj `
  -- --environment Development
```

Model ile snapshot'ın senkron kontrolü:

```powershell
dotnet ef migrations has-pending-model-changes `
  --project src/Ledgerly.Infrastructure/Ledgerly.Infrastructure.csproj `
  --startup-project src/Ledgerly.Api/Ledgerly.Api.csproj `
  -- --environment Development
```

## Integration test yaklaşımı

Testler EF Core InMemory provider kullanmaz. Gerçek PostgreSQL şu davranışları birlikte doğrular:

- DI registration
- Handler orchestration
- Repository sorgusu
- EF Core mapping ve value conversion
- `SaveChangesAsync`
- PostgreSQL'e yazma ve tekrar okuma
- Duplicate wallet pre-check'i

Test fixture başlangıçta `Database.MigrateAsync()` çağırır. Her test ayrı transaction açar ve sonunda rollback yapar. Böylece test gerçek database davranışını kullanırken kalıcı test verisi bırakmaz.

```powershell
docker compose up -d

dotnet test tests/Ledgerly.IntegrationTests/Ledgerly.IntegrationTests.csproj `
  --filter "Category=Integration"
```

Varsayılan lokal bağlantı gerektiğinde şu environment variable ile override edilebilir:

```text
LEDGERLY_TEST_DB_CONNECTION_STRING
```

### Neden henüz Testcontainers yok?

| Seçenek | Avantaj | Bedel |
|---|---|---|
| Mevcut Docker Compose PostgreSQL | Az bağımlılık, kurulum kolay, mevcut container tekrar kullanılır | Test öncesi container'ın ayrıca çalıştırılması gerekir |
| Testcontainers | İzole ve otomatik container yaşam döngüsü, CI için güçlü | Yeni paket, her test koşusunda container başlangıç maliyeti ve ek kurulum |

Başlangıçta Compose seçildi. Test ortamı paylaşımı veya CI güvenilirliği gerçek bir probleme dönüştüğünde Testcontainers yeniden değerlendirilecektir.

## Karşılaşılan tooling problemleri

### Local tool restore edilmemişti

Belirti:

```text
Run "dotnet tool restore" to make the "dotnet-ef" command available.
```

Root cause: `dotnet-tools.json` aracı ve sürümü tanımlar ancak yeni makinede aracı otomatik indirmez.

Çözüm:

```powershell
dotnet tool restore
```

### EF Design paketi startup project'te değildi

Belirti:

```text
Your startup project 'Ledgerly.Api' doesn't reference
Microsoft.EntityFrameworkCore.Design.
```

Root cause: Migration Infrastructure assembly'sinde üretilse de EF CLI host'u startup project olan API üzerinden kurar.

Karar: `Microsoft.EntityFrameworkCore.Design` package reference API projesine taşındı ve `PrivateAssets=all` olarak tutuldu.

### Migration üretildikten sonra stale assembly kullanıldı

Belirti:

```text
No migrations were found in assembly 'Ledgerly.Infrastructure'.
```

Root cause: Migration dosyası oluşturulduktan sonra database update `--no-build` ile çalıştırıldı. Diskteki yeni kaynak kod henüz Infrastructure DLL'ine derlenmemişti.

Çözüm: Önce build çalıştırmak veya database update sırasında `--no-build` kullanmamak.

## Doğrulama kanıtı

- PostgreSQL container `healthy` durumunda.
- `wallets` ve `__EFMigrationsHistory` tabloları oluştu.
- `InitialCreate` migration'ı uygulanmış durumda.
- EF modeli ile migration snapshot arasında pending değişiklik yok.
- 12 Domain, 2 Application ve 2 PostgreSQL Integration testi başarılı.
- Build 0 warning ve 0 error ile tamamlanıyor.

## Bilinçli olarak ertelenenler

- API endpoint ve HTTP contract testi
- Concurrency yarışının reproduce edilmesi
- Unique constraint exception'ının domain/application hatasına çevrilmesi
- Idempotency key
- Retry ve transient error politikaları
- Outbox ve event broker
- Testcontainers

## Sonraki adım

Create Wallet vertical slice'ını HTTP üzerinden erişilebilir yapmak ve API contract'ını functional test ile doğrulamak.
