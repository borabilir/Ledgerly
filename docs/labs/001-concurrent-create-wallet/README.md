# LAB-001 — Eşzamanlı Create Wallet Yarışı

**Durum:** Problem deterministik olarak reproduce edildi; çözüm henüz uygulanmadı  
**İlgili milestone:** [02 — Create Wallet HTTP API](../../journey/02-create-wallet-http-api.md)  
**Git commit/tag:** `test(concurrency): reproduce duplicate wallet race`

## 1. Problem

Aynı `ownerId + currency` için iki HTTP isteği aynı anda geldiğinde Application katmanındaki `ExistsAsync` pre-check'i tek başına concurrency garantisi sağlayamaz.

Sıralı duplicate testinde ikinci istek mevcut kaydı görüp kontrollü `409 Conflict` alır. Eşzamanlı durumda ise iki istek de insert yapılmadan önce “wallet yok” sonucunu görebilir.

## 2. Korunması gereken invariant

```text
Bir owner aynı currency için en fazla bir wallet'a sahip olabilir.
```

İstenen dış davranış:

```text
2 eşzamanlı istek
  -> 1 x 201 Created
  -> 1 x 409 Conflict
  -> database'de 1 wallet
```

Reproduce anındaki mevcut dış davranış:

```text
2 eşzamanlı istek
  -> 1 x 201 Created
  -> 1 x 500 Internal Server Error
  -> database'de 1 wallet
```

Database invariant'ı korunuyor fakat API kaybeden isteği beklenen business conflict olarak sunamıyor.

## 3. Baseline

- `CreateWalletHandler`, insert öncesinde `IWalletRepository.ExistsAsync` çağırır.
- PostgreSQL'de `(owner_id, currency)` üzerinde `ux_wallets_owner_id_currency` unique index'i vardır.
- API, `WalletAlreadyExistsException` için `409`; bilinmeyen exception için `500` döndürür.
- Test gerçek ASP.NET Core pipeline'ını ve gerçek `ledgerly_tests` PostgreSQL database'ini kullanır.

## 4. Problemi deterministik reproduce etme

Yalnızca iki `PostAsJsonAsync` çağrısını peş peşe başlatmak gerçek concurrency üretse de yarışın her test koşusunda oluşacağını garanti etmez. Makine hızı veya scheduler sıralaması değiştiğinde ilk insert ikinci pre-check'ten önce tamamlanabilir ve test bazen `409` görebilir.

Bu nedenle test host'unda production `WalletRepository`, `CoordinatedWalletRepository` ile decorate edilir. Decorator gerçek sorguyu ve eklemeyi değiştirmez; yalnızca iki `ExistsAsync` çağrısını ortak bir bariyerde buluşturur:

```text
Request A -> gerçek ExistsAsync -> false -> bariyerde bekle
Request B -> gerçek ExistsAsync -> false -> bariyere ulaş
                                      |
                                      v
                              iki isteği birlikte serbest bırak
                                      |
                      Request A INSERT + Request B INSERT
```

`ConcurrentRequestGate`, `Interlocked` ile gelen request sayısını tutar ve iki katılımcı gelince `TaskCompletionSource` üzerinden ikisini serbest bırakır. On saniyelik timeout, test altyapısındaki bir hata halinde süresiz beklemeyi engeller.

Production koduna gecikme veya test flag'i eklenmez. Repository değişimi yalnızca `WebApplicationFactory.WithWebHostBuilder` ve `ConfigureTestServices` içindeki test host'una uygulanır.

Çalıştırma:

```powershell
docker compose up -d

dotnet test tests/Ledgerly.IntegrationTests/Ledgerly.IntegrationTests.csproj `
  --filter "Lab=ConcurrentCreateWallet" `
  --logger "console;verbosity=normal"
```

## 5. Gözlem ve kanıt

Test başarıyla aynı sırayı üretti:

```text
SELECT EXISTS -> false
SELECT EXISTS -> false
INSERT         -> başarılı
INSERT         -> başarısız
```

Provider hatası:

```text
Npgsql.PostgresException
SqlState:       23505
ConstraintName: ux_wallets_owner_id_currency
TableName:      wallets
```

`PostgresException`, EF Core tarafından `DbUpdateException` içine sarıldı. Merkezi API exception handler bu teknik hatayı tanımadığı için genel `500 Internal Server Error` üretti.

HTTP ve veri sonucu:

```text
201 Created:              1
500 Internal Server Error: 1
wallet row count:          1
```

Bu sonuç iki ayrı şeyi gösterir:

1. Unique index, iki satır oluşmasını engelleyerek veri invariant'ını koruyor.
2. Teknik database hatası henüz anlamlı Application/API hatasına çevrilmediği için API sözleşmesi doğru değil.

## 6. Root cause için ilk bulgu

Pre-check ile insert tek bir atomik işlem değildir. İki request farklı `DbContext` ve database bağlantıları üzerinden aşağıdaki zaman aralığına birlikte girebilir:

```text
ExistsAsync tamamlandı
        <race window>
SaveChangesAsync başladı
```

Root cause yalnızca “aynı anda iki istek geldi” değildir. Asıl neden, doğruluk kararının güncelliğini insert anına kadar garanti etmeyen bir `check-then-act` akışına güvenilmesidir.

## 7. Henüz yapılmayanlar

- Alternatif çözümlerin trade-off karşılaştırması
- PostgreSQL unique violation'ın kontrollü Application hatasına çevrilmesi
- Concurrency testinin istenen `201 + 409` sonucuna güncellenmesi
- Çözüm sonrası doğrulama ve mülakat özeti

Bu lab bilinçli olarak mevcut `500` sonucunu assertion olarak taşır. Sonraki adımda çözüm seçildikten sonra aynı test istenen sözleşmeyi doğrulayacak şekilde değiştirilecektir.

## 8. Temizleme

Test benzersiz `ownerId` üretir. Assertion başarılı veya başarısız olsa da `finally` bloğu oluşturulan wallet satırını siler ve HTTP response nesnelerini dispose eder.
