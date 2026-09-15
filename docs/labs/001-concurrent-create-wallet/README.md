# LAB-001 — Eşzamanlı Create Wallet Yarışı

**Durum:** Çözüm uygulandı ve gerçek PostgreSQL ile doğrulandı (2026-09-15)\
**İlgili milestone:** [02 — Create Wallet HTTP API](../../journey/02-create-wallet-http-api.md)\
**Reproduce commit:** `72fc271` — `test(concurrency): reproduce duplicate wallet race`\
**Çözüm commit başlığı:** `fix(wallets): translate concurrent duplicate conflicts`\
**Karar:** [ADR-0003](../../adr/0003-concurrent-create-wallet-conflict.md)

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

Reproduce aşamasında database invariant'ı korunuyordu fakat API kaybeden isteği beklenen business conflict olarak sunamıyordu. Aşağıdaki baseline ve gözlem kayıtları bu önceki davranışı korur.

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

Aynı komut reproduce commit'inde `201 + 500`, çözümde `201 + 409` assertion'larını çalıştırır. Eski davranışı incelemek için mevcut değişiklikleri resetlemek gerekmez; `72fc271` ayrı bir checkout'ta incelenebilir.

macOS / zsh / Bash:

```bash
docker compose up -d
docker compose ps
dotnet test tests/Ledgerly.IntegrationTests/Ledgerly.IntegrationTests.csproj --filter "Lab=ConcurrentCreateWallet" --logger "console;verbosity=normal"
```

PowerShell:

```powershell
docker compose up -d

dotnet test tests/Ledgerly.IntegrationTests/Ledgerly.IntegrationTests.csproj `
  --filter "Lab=ConcurrentCreateWallet" `
  --logger "console;verbosity=normal"
```

## 5. Gözlem ve kanıt — reproduce aşaması

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

## 6. Root cause

`ExistsAsync`, “sorgu anında wallet yok” der; insert'e kadar o anahtarı rezerve etmez. Kontrol ve ekleme atomik olmadığı için iki request aynı boş durumu görebilir. Bariyer bu aralığı testte kontrollü olarak oluşturur.

Unique index ikinci kalıcı satırı engeller. Aynı anahtarı ekleyen transaction henüz bitmediyse diğer insert onun sonucunu bekleyebilir: commit olursa unique violation, rollback olursa yeniden kontrol sonrası ekleme mümkündür. İlk gönderilen HTTP isteğinin kazanacağı garantisi yoktur.

API kusurunun nedeni `DbUpdateException` için anlamlı bir dönüşümün olmamasıdır. Mevcut middleware yalnızca `WalletAlreadyExistsException`ı `409` olarak tanır.

## 7. Alternatif çözümler

- **Unique constraint + exception translation:** Mevcut unique index korunur; ilgili provider hatası Application hatasına çevrilir. Yarışı engellemez, sonucunu kontrollü sunar.
- **PostgreSQL upsert:** `ON CONFLICT (owner_id, currency) DO NOTHING RETURNING id` kullanılır. Satır dönmemesi conflict olarak ele alınır. Mevcut wallet'ı güncellemek istemediğimiz için `DO UPDATE` uygun değildir. Unique index yine gerekir; hedef belirtilmeden bütün unique ihlalleri yutulmaz.
- **Serializable transaction:** Pre-check ve insert aynı Serializable transaction içine alınır. Serialization failure için bütün transaction'ın sınırlı retry politikası gerekir. Unique violation yine mümkündür; isolation seçimi kendiliğinden HTTP 409 üretmez.
- **Pessimistic locking:** İstekler aynı kilit hedefiyle sıraya alınabilir. Olmayan wallet satırı `SELECT FOR UPDATE` ile kilitlenemez; mevcut owner satırı veya advisory lock gibi başka bir düzen gerekir. Deadlock ve kilit kapsamı ele alınmalıdır.
- **Distributed lock:** Aynı owner/currency anahtarı için ortak kilit servisi kullanılabilir. Bütün yazıcıların aynı protokole uyması, lease süresi ve kaybedilen kilit sahipliğinin yönetimi gerekir. Bu tek-DB invariant'ı için unique index'in yerini almaz.

## 8. Trade-off değerlendirmesi

Aşağıdaki performans değerlendirmeleri mekanizma maliyetidir; bu lab'da load benchmark yapılmadı.

| Yaklaşım | Correctness | Latency / throughput | Karmaşıklık / operasyon |
|---|---|---|---|
| Unique + translation | DB tekilliği korur, dar mapping doğru HTTP sonucu verir | Çakışmada bekleme ve exception maliyeti; yeni koordinasyon çağrısı yok | Küçük değişiklik, ek servis yok |
| Upsert | Aynı unique index üzerinde atomik conflict kararı | Exception yolunu azaltabilir; pre-check kaldırılırsa bir sorgu azalır, çakışmada yine bekleyebilir | PostgreSQL'e özel SQL ve persistence akışı değişikliği |
| Serializable | Daha geniş okuma/yazma kurallarını koruyabilir | Conflict altında transaction retry ek yük ve gecikme yaratabilir | Transaction kapsamı, retry ve hata sınıflandırması gerekir |
| Pessimistic lock | Doğru ortak hedef ve bütün yazıcıların katılımıyla sıraya alma | Aynı kilitte kuyruk; geniş kilit kapsamı kapasiteyi düşürür | Kilit hedefi, deadlock ve bekleme yönetimi |
| Distributed lock | Lease/sahiplik protokolünün doğruluğuna bağlı | Ek ağ çağrıları ve aynı anahtarda bekleme | Ek servis, failure mode ve operasyon maliyeti |

## 9. Karar

[ADR-0003](../../adr/0003-concurrent-create-wallet-conflict.md): Unique constraint + exception translation seçildi. Tek owner/currency kuralı mevcut PostgreSQL index'iyle ifade ediliyor. Yeni bir kilit servisi veya retry düzeni eklemek gerekmiyor.

Pre-check erken duplicate yanıtı için kalır; doğruluk garantisi değildir. Yoğun duplicate trafiğinin exception maliyeti ölçülürse targeted upsert yeniden değerlendirilir.

## 10. Implementasyon

```text
CreateWalletHandler
  -> IUnitOfWork.SaveChangesAsync
  -> PostgreSQL 23505 / ux_wallets_owner_id_currency
  -> DbUpdateException
  -> Infrastructure: WalletAlreadyExistsException
  -> mevcut ApiExceptionHandler: 409 ProblemDetails
```

Değişiklikler:

- `LedgerlyDbContext` explicit `IUnitOfWork.SaveChangesAsync` içinde catch filter kullanır.
- İç hata `PostgresException`, SQLSTATE `23505` ve constraint adı tam olarak `ux_wallets_owner_id_currency` olmalıdır. Hata mesajı parse edilmez.
- Hata kaydı tek bir `Added` Wallet'a ait olmalıdır. Mevcut Create Wallet bir aggregate kaydeder; belirsiz batch hatalarında owner/currency tahmin etmek yerine orijinal hata korunur.
- Owner ve currency başarısız EF entry'sinden alınır. Application exception'ı orijinal `DbUpdateException`ı `InnerException` olarak korur.
- Index adı mapping ile çeviri arasında aynı sabitten okunur. Index'in veritabanındaki adı değişmedi; yeni migration gerekmedi.
- Application'a EF Core veya Npgsql bağımlılığı eklenmedi. API'nin mevcut 409 mapping'i kullanıldı.
- Diğer database hataları değiştirilmeden yükselir; mevcut API handler bunları 500 olarak sunar.

Bu çeviri Application'ın kullandığı `IUnitOfWork` sınırındadır. Doğrudan `DbContext.SaveChangesAsync` çağıran altyapı kodu EF hatalarını almaya devam eder. Başarısız request sona erer ve scoped context dispose edilir; aynı context ile retry yapılmaz. Çoklu aggregate/batch use-case'i eklendiğinde hata-attribution kararı ayrıca değerlendirilmelidir.

## 11. Test ve doğrulama

2026-09-15, macOS ARM64, SDK `10.0.400`, runtime `10.0.11`, Docker Compose PostgreSQL `18.6-alpine`, gerçek `ledgerly_tests` üzerinde:

1. Çözümden önce eski characterization testi çalıştırıldı: 1 passed (`201 + 500 + tek wallet`); logda SQLSTATE ve constraint adı doğrulandı.
2. Aynı test yalnızca `409` beklentisine çevrildi: 1 failed. Hata, beklenen 409 yerine 500 gelmesiydi. Bu, regression assertion'ının kusuru yakaladığını gösterdi.
3. Çeviri eklendikten sonra tüm solution testleri çalıştırıldı: **28 passed, 0 failed, 0 skipped**.

| Test projesi | Başarılı test case |
|---|---:|
| Domain | 12 |
| Application | 2 |
| IntegrationTests | 14 |

IntegrationTests projesindeki 14 case'in 11'i PostgreSQL/HTTP entegrasyonunu, 3'ü `SaveChangesInterceptor` ile kontrollü hata enjeksiyonunu kullanır. Enjekte edilen testler gerçek provider arızası veya network testi olarak sunulmaz.

Yeni/güncellenen kontroller:

- Aynı bariyerli HTTP testi: tam bir 201, tam bir 409; doğru ProblemDetails status/title/detail; DB'de tek wallet.
- Gerçek owner/currency unique ihlali: doğru Application exception alanları ve korunmuş provider hata zinciri.
- Gerçek `pk_wallets` ihlali: SQLSTATE yine 23505 olsa da `DbUpdateException` korunur.
- Gerçek numeric overflow (22003): duplicate kabul edilmez.
- Aynı constraint adında farklı SQLSTATE, PostgreSQL dışı inner exception ve inner exception olmayan hata: aynı `DbUpdateException` nesnesi korunur.

```bash
dotnet test Ledgerly.slnx
dotnet test tests/Ledgerly.IntegrationTests/Ledgerly.IntegrationTests.csproj --filter "FullyQualifiedName~WalletPersistenceExceptionTests"
```

SDK PATH'te bulunmuyorsa macOS kullanıcı kurulumu için `~/.dotnet/dotnet` kullanılabilir. Bu doğrulamada test komutu tam SDK yoluyla çalıştırıldı.

## 12. Sonuç

| Ölçülen davranış | Önce | Sonra |
|---|---|---|
| Başarılı HTTP isteği | 1 × 201 | 1 × 201 |
| Kaybeden HTTP isteği | 1 × 500 | 1 × 409 |
| Kalıcı wallet sayısı | 1 | 1 |

Unique index veriyi zaten koruyordu. Çözüm API sözleşmesini tamamladı. Bu çalışma idempotency, transfer/double-spending veya ölçülmüş yüksek yük garantisi sağlamaz.

## 13. Öğrendiklerim

- Application pre-check rezervasyon değildir.
- Request'leri test başlatır; gate yalnızca sorgu ile insert arasındaki ilerlemeyi kontrol eder.
- `Interlocked` ortak sayacı atomik artırır; wallet tekilliğini sağlayan PostgreSQL index'idir.
- `await` ile beklenen ortak Task'ı ikinci katılımcı tamamlar. Timeout gate'i açmaz; `WaitAsync` TimeoutException ile sona erer.
- Characterization testinin yeşil olması kusurun yokluğu anlamına gelmez.
- SQLSTATE tek başına business hatası seçmek için yeterli değildir.

## 14. Mülakat özeti

> Aynı owner ve currency için iki Create Wallet isteğinin pre-check'i birlikte geçebildiğini, gerçek PostgreSQL kullanan HTTP testinde bir bariyerle reproduce ettim. Unique index tek satırı koruyordu fakat kaybeden istek 500 alıyordu. Upsert, Serializable ve kilitleme seçeneklerini değerlendirdim. Kural tek bir index ile korunduğu için en küçük çözüm olarak Infrastructure'da yalnızca ilgili 23505/constraint çiftini WalletAlreadyExistsException'a çevirdim. Aynı test artık bir 201, bir 409 ve tek kayıt doğruluyor. Başka unique ihlalleri ve database hatalarının yanlışlıkla duplicate olarak sunulmadığını da test ettim.

## 15. Temizleme

HTTP testi benzersiz owner ID kullanır ve `finally` içinde ilgili satırları silip response'ları dispose eder. Persistence hata testleri ayrı transaction açar ve sonunda rollback yapar. Testler development `ledgerly` veritabanına yazmaz.

## Kaynaklar

- [PostgreSQL unique kontrolü](https://www.postgresql.org/docs/18/index-unique-checks.html)
- [PostgreSQL INSERT / ON CONFLICT](https://www.postgresql.org/docs/18/sql-insert.html)
- [PostgreSQL transaction isolation](https://www.postgresql.org/docs/18/transaction-iso.html)
- [PostgreSQL explicit locking](https://www.postgresql.org/docs/18/explicit-locking.html)
