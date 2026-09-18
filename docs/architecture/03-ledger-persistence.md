# Ledger Persistence ve Kayıt Sınırı

**Durum:** Uygulandı ve gerçek PostgreSQL ile doğrulandı.

**Tarih:** 2026-09-16

## Modelden kalıcı kayda

Persistence, veriyi uygulamanın belleği dışında kalıcı saklamaktır. Domain'deki LedgerAccount, JournalEntry ve Posting kuralları korunarak Infrastructure içinde ayrı EF tablo modelleri kullanıldı. Domain dosyaları değiştirilmedi.

```text
LedgerAccount → ILedgerAccountRepository.Add → LedgerAccountRecord
JournalEntry  → IJournalEntryRepository.Add  → JournalEntryRecord + PostingRecord[]
                                      ↓
                         aynı scoped LedgerlyDbContext
                                      ↓
                          IUnitOfWork.SaveChangesAsync
                                      ↓
                           PostgreSQL transaction
```

Record son eki burada “tablo kaydı” anlamındadır; bu üç Infrastructure tipi C# record değil, internal class'tır. Domain Posting ise değiştirilemeyen C# record olarak kalır.

## Neden ayrı tablo modelleri?

EF ilişkili satırları takip eder ve okurken koleksiyonlara eleman ekler. Domain journal'ın dışarıya kapalı array'ini değiştirilebilir hâle getirmek yerine tablo nesnelerini ayrı tuttuk. Bir Posting value object'i aynı journal'da iki kez geçebilir; her occurrence yeni bir PostingRecord satırına çevrilir. Böylece aynı nesne referansını iki kez kullanmak ikinci satırın kaybolmasına neden olmaz. Bunun kalıcı okuma testi vardır.

Bedel: üç küçük tablo modeli ve açık dönüşüm kodu. Generic mapper veya yeni framework eklenmedi. Alternatifler [ADR-0006](../adr/0006-ledger-persistence-and-atomic-save.md) içinde.

## Şema

| Tablo | Anahtar | Temel veri |
|---|---|---|
| ledger_accounts | id | wallet_id nullable, type, purpose, currency, created_at_utc |
| journal_entries | id | currency, created_at_utc |
| postings | journal_entry_id + sequence | account_id, currency, direction, amount |

Sequence journal içindeki sıfırdan başlayan satır sırasıdır. Global işlem sırası, event stream version veya business operation kimliği değildir. Domain Posting'e yapay entity kimliği eklemedik. Okuma metodu satırları bu sıraya göre döndürür.

Amount numeric(19,4), zaman timestamp with time zone olarak saklanır. Currency, EF içinde mevcut Currency value object'iyle tutulur; database'e üç karakterlik kod olarak çevrilir. EF'de composite ilişkinin iki tarafındaki CLR tipleri de eşleşmelidir; sadece database sütun türünün eşit olması yeterli değildir.

## Database hangi kuralları koruyor?

| Kural | Koruma |
|---|---|
| Wallet'a bağlı hesap, aynı para birimindeki mevcut wallet'a ait olmalı | (wallet_id, currency) → wallets(id, currency) foreign key |
| Wallet başına en fazla bir müşteri hesabı | wallet_id üzerinde null olmayan kayıtları kapsayan unique index |
| Currency başına en fazla bir test fon hesabı | currency üzerinde purpose = TestFunding kayıtlarını kapsayan unique index |
| Wallet amacı Liability + dolu WalletId; TestFunding amacı Asset + boş WalletId olmalı | purpose/type/wallet_id check constraint ve zorunlu purpose sütunu |
| Posting'in hesabı ve journal'ı var ve aynı currency'de olmalı | Her iki tarafa (id, currency) üzerinden foreign key |
| Tutar pozitif, yön Debit/Credit, sıra negatif değil | Posting check constraint'leri |
| Aynı journal/sıra iki kez yazılamaz | Composite primary key |
| Kullanılan üst kayıt yanlışlıkla silinemez | Foreign key ON DELETE RESTRICT |

Composite foreign key için wallets, ledger_accounts ve journal_entries üzerinde (id, currency) alternate key'leri vardır. Id zaten tekildir; bu ek anahtarlar currency'nin de ilişkiye katılması içindir. Ek index alanı/yazma maliyeti kabul edildi.

Fon hesabı için wallet_id null olması bilinçlidir. [Purpose adımı](../domain/05-ledger-account-purpose.md) ile kullanım amacı ayrıca kaydedilir; test fonu artık yalnızca boş WalletId'den çıkarılmaz. Yeni hesap rolleri gelirse enum, factory ve check constraint birlikte genişletilir.

## Atomik kayıt

Repository Add metotları yalnızca değişiklikleri hazırlar; save çağırmaz. Journal başlığı ve tüm posting'ler bir graph olarak eklenir. Mevcut Unit of Work bir SaveChangesAsync çağırır; ilişkisel provider bu çağrıdaki değişiklikleri transaction içinde kaydeder. Bir satır başarısızsa bu çağrının değişiklikleri geri alınır. [EF Core transactions](https://learn.microsoft.com/en-us/ef/core/saving/transactions)

Birden çok SaveChanges çağrısı gerekiyorsa bunları ayrıca ortak transaction içine almak gerekir. Bu milestone'da böyle bir gereksinim olmadığı için yeni transaction manager veya explicit transaction API'si eklenmedi.

Testte statement'ların gerçekten ayrı çalıştığını gözlemek için MaxBatchSize(1) kullanılır. Bu yalnızca lab writer context'ine aittir; uygulamanın batching ayarı değiştirilmedi.

## Okuma ve hata davranışı

Repository okumaları AsNoTracking çalışır, Application'a LedgerAccountSnapshot veya JournalEntrySnapshot döndürür. Bunlar kaydetmek için takip edilen aggregate'lar değildir. Journal snapshot'ında posting'ler sıralı ve read-only olarak sunulur. Bu isim event sourcing snapshot mekanizması kurulduğu anlamına gelmez.

CancellationToken database okumasına iletilir. Wallet hesabı, fon hesabı veya journal bulunamazsa null döner. Yeni HTTP endpoint/handler eklenmedi.

Persistence ilk eklendiğinde ledger constraint ihlalleri DbUpdateException/PostgresException olarak kalıyordu. Test yatırması adımında yalnızca iki hesap unique constraint ihlali LedgerWriteConflictException/409 olarak çevrilmeye başlandı. WalletAlreadyExistsException çevirisi yalnızca wallet owner/currency constraint'ine uygulanır. Diğer hatalar bu çeviriye girmez. [Güncel karar](../adr/0007-test-deposit-and-balance-concurrency.md).

## Garanti sınırları

Journal toplamlarının eşitliği ve minimum satır sayısı Domain factory'sinde korunur. Database'e doğrudan SQL yazan biri dengesiz journal veya boş başlık oluşturabilir; bu milestone database trigger ile cross-row dengeyi zorlamaz. Lab'ın bozuk örneği bunu özellikle gösterir.

Internal EF modelleri değiştirilebilir. Public repository update/delete sunmuyor ama database seviyesinde append-only yetki/trigger koruması kurulmadı. RESTRICT, doğrudan posting silinmesini engellemez. Domain daha hassas tutarı reddeder; doğrudan SQL numeric(19,4) sütununa yazıldığında PostgreSQL yuvarlama uygulayabilir.

TRY dışındaki para birimleri Domain/API tarafından desteklenmez. Currency eşleşme negatif testleri için transaction içinde SQL ile bir USD fixture satırı kurulur; test sonunda geri alınır. Bu ürün özelliği değildir.

Persistence repository'leri kendi başına Wallet.Balance güncellemez. [Test yatırması](../journey/07-test-deposit.md) handler'ı artık bakiyeyi, eksik hesapları ve journal'ı birlikte kaydeder. POST /api/wallets otomatik ledger hesabı açmaz; hesaplar ilk yatırmada hazırlanır. Transfer, yeterli bakiye kontrolü, idempotency, kalıcı immutable ledger ve event sourcing sonraki ayrı işlerdir.

## Doğrulama

[LAB-002](../labs/002-journal-atomicity/README.md) ayrı save'lerde yarım kayıt ile tek save'de rollback sonucunu karşılaştırır. [Milestone](../journey/06-ledger-persistence.md) 90 testin kapsamını ve migration komutlarını içerir.
