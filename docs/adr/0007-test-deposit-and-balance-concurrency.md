# ADR-0007 — Test Yatırması, İlk Kullanımda Hesap Açma ve Bakiye Çakışması

**Durum:** Kabul edildi — 2026-09-16

## Problem ve kanıt

Wallet bakiyesi ile paranın muhasebe kaydı birlikte değişmeli. Mevcut wallet'ların ledger hesabı yok; ortak test fon hesabı da önceden oluşturulmuyor.

Ayrıca iki işlem aynı 0 bakiyesini okuyup sırasıyla 10 ve 20 yazarsa son değer 20 oluyor. Guard eklenmeden önce gerçek PostgreSQL testinde bu sonuç gözlendi. Atomic transaction tek başına eski bakiyeden yapılan hesabın doğruluğunu sağlamaz. [Deney ve yeniden çalıştırma](../labs/003-test-deposit/README.md).

## Hesap hazırlama alternatifleri

| Seçenek | Kazanç | Bedel |
|---|---|---|
| Wallet oluşturulurken ledger hesabı açmak | İlk yatırma daha az iş yapar | Create Wallet genişler; mevcut wallet'lar ve ortak fon hesabı için ek hazırlık gerekir |
| Migration veya ayrı hazırlık komutu | İstek sırasında hesap açılmaz | Yeni ve eski wallet'ları sürekli kapsayacak ayrı bir süreç gerekir |
| İlk yatırmada eksik hesapları açmak | Eski/yeni wallet aynı akıştan geçer; tek transaction yeterli | İki ilk istek hesap açarken yarışabilir; unique constraint ve conflict davranışı gerekir |

İlk yatırmada açmayı seçtik. Bu aşamada operasyonel hazırlık komutu gerektirmiyor. Ortak fon hesabı currency başına bir tane; ikinci yatırmada mevcut hesaplar kullanılıyor. Wallet oluşturmak hâlâ yalnızca wallet oluşturur.

## Bakiye yazma alternatifleri

| Seçenek | Doğruluk | Gecikme ve kapasite | Karmaşıklık / operasyon |
|---|---|---|---|
| Eski bakiyeden hesaplayıp koşulsuz yazmak | Artış kaybolabilir | Az iş; yanlış sonuç | Kabul edilmedi |
| Eski bakiyeyi UPDATE koşuluna eklemek | Değişmiş bakiye üzerine yazmayı reddeder | Çakışmasız durumda ek okuma yok; çakışmada 409 | EF concurrency token; yeni servis yok |
| SQL ile `balance = balance + amount` | Artışlar birbirini ezmez | Kısa güncelleme; aynı satırda DB kilidi | Tutar üst sınırı, receipt ve journal için ortak transaction ayrıca düzenlenmeli |
| Satırı okuyunca kilitlemek (`FOR UPDATE`) | Sonraki işlem güncel bakiyeyi okur | Bekleyen istekler, uzun transaction ve olası deadlock | Açık transaction ve kilit sırası gerekir |
| Serializable transaction | Çelişen işlemlerden biri iptal olabilir | Çakışmada yeniden deneme maliyeti | Retry ve hata sınıflandırması gerekir |

Bu adım için EF'nin optimistic concurrency desteğini seçtik. Bu, okurken kilit almak yerine kaydederken okunan değerin hâlâ geçerli olup olmadığını kontrol etmektir. `Balance.IsConcurrencyToken()` eski bakiyeyi UPDATE koşuluna ekler. Hiç satır güncellenmezse EF `DbUpdateConcurrencyException` üretir. Infrastructure bunu `LedgerWriteConflictException` olarak Application diline çevirir.

Balance token bu **yalnızca pozitif yatırma** akışında yeterli. İleride bakiye azalır ve eski değerine dönerse aradaki değişim bu token ile anlaşılmaz; buna ABA denir. Transfer, durum değişimi veya genel wallet sürüm kontrolü eklenirken ayrı version/xmin ve korunacak kurallar yeniden değerlendirilecek. Şu an tüm wallet alanları için concurrency koruması iddia edilmiyor.

## Karar ve HTTP sözleşmesi

`POST /api/wallets/{walletId}/test-deposits` yalnızca Development ve IntegrationTests ortamlarında işlem yapar. Diğer ortamlarda 404 döner. JSON gövdesi `{ "amount": 100 }`; currency wallet'tan gelir. Yanıt 200: walletId, journalEntryId, currencyCode, amount, balance. Bu balance ilgili işlemin sonucudur; yanıt ulaştığında başka bir işlem onu değiştirmiş olabilir.

Domain `Wallet.Credit` pozitif tutar, en fazla dört ondalık ve numeric(19,4) bakiye sınırını korur; yuvarlamaz. Eksik wallet 404, geçersiz tutar 400. Handler önce wallet'ı bulur; olmayan wallet için tutar doğrulamasından önce 404 gelir.

Tracked wallet + gerekirse iki yeni hesap + journal + iki posting tek `IUnitOfWork.SaveChangesAsync` içinde kaydedilir. Repositories ayrı save yapmaz. Bir adım reddedilirse bu save içindeki tüm değişiklikler geri alınır. Request sona erince DbContext atılır; rollback bellekteki nesneleri eski hâline getirmiş sayılmaz.

Yalnızca wallet concurrency hatası ve PostgreSQL 23505 ile şu iki constraint `LedgerWriteConflictException` olur:

- `ux_ledger_accounts_wallet_id`
- `ux_ledger_accounts_test_funding_currency`

Create Wallet'ın eski constraint çevirisi korunur. Foreign key, posting primary key, bağlantı hatası ve diğer DbUpdateException'lar bu çeviriye girmez. API bunları ayrıntıyı gizleyen 500 olarak karşılar.

## Sonuçlar ve sınırlar

Çakışmada 409 döndürülür; otomatik retry yok. Ortak fon hesabını ilk kez açan farklı wallet'lar da bir defalık çakışabilir. Başarılı istekleri daha önce işlememiş gibi tekrar göndermek yeni bir yatırmadır. Network timeout veya 500 sonrasında kör retry güvenli değildir: commit'in sonucunun belirsiz olduğu durumlar idempotency adımında ele alınacak.

Bu test parasıdır; gerçek banka, kimlik doğrulama, yetkilendirme, ödeme sağlayıcısı ve banka mutabakatı içermez. Ortam kontrolü, gerçek para için güvenlik modelinin yerine geçmez. Ledger'ın DB düzeyinde değiştirilemezliği ve satırlar arası denge kontrolü önceki sınırlarıyla devam eder.

Concurrency token veritabanında yeni sütun gerektirmez. `GuardWalletBalanceConcurrency` migration'ının Up/Down metotları bu nedenle boştur; model snapshot'ı token bilgisini taşır.

## Doğrulama

Domain tutar/sınır; Application hesap hazırlama/journal/tek save; HTTP gerçek PostgreSQL round-trip, bakiye yarışı, hesap açma yarışları, rollback, Production 404 ve OpenAPI testleri. [Milestone](../journey/07-test-deposit.md).

Kaynaklar: [EF concurrency](https://learn.microsoft.com/en-us/ef/core/saving/concurrency), [EF transactions](https://learn.microsoft.com/en-us/ef/core/saving/transactions).
