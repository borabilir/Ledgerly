# 07 — Test Bakiyesi Yatırma

**Durum:** Tamamlandı — 2026-09-16

## Başlangıç

Wallet, hesap ve journal persistence hazırdı. Bakiye değiştiren HTTP akışı yoktu. DbSet erişimleri önceki küçük düzenlemeyle isimli property'lere taşındı; ledger satır türleri ve setleri internal kaldı.

## Problem → kanıt → karar

İki eski bakiye okuması ardından koşulsuz yazma ile 10 TL artış kayboldu: 0'dan başlayan 10 ve 20 yatırmalarından sonra 20 kaldı. [LAB-003](../labs/003-test-deposit/README.md) deneyi ve HTTP yarışlarını saklar. [ADR-0007](../adr/0007-test-deposit-and-balance-concurrency.md) hesap hazırlama ve concurrency seçeneklerini değerlendirir.

## Değişiklik

- `Wallet.Credit` tutarı ve oluşacak bakiyeyi doğrular.
- `GetForUpdateAsync` tracked Wallet döndürür. İsmi satır kilidi alındığı anlamına gelmez. Mevcut GET hâlâ AsNoTracking okur.
- `TestDepositHandler` eksik hesapları hazırlar, fon debit / wallet credit journal'ı oluşturur; her şeyi tek SaveChanges ile kaydeder.
- `Balance` concurrency token'dır. Wallet'ın değişmiş bakiyesi veya iki belirli ledger hesap unique ihlali 409'a çevrilir.
- `POST /api/wallets/{walletId}/test-deposits`: Development/IntegrationTests için 200, invalid amount 400, missing wallet 404, conflict 409, unexpected failure 500. Diğer ortamlarda 404.
- Snapshot'a concurrency bilgisi ekleyen migration fiziksel schema değişikliği içermez.

## Kullanım

F5 veya development ortamında API'yi başlat; Scalar'da önce POST `/api/wallets` ile wallet oluştur. Dönen walletId ile test-deposits çağrısına `{ "amount": 100 }` gönder. GET wallet sonucunda balance 100 olur. Yanıttaki journalEntryId, muhasebe kaydıyla ilişki kurar; henüz journal GET endpoint'i yoktur.

## Test

Yeni Domain 7, Application 5, Integration 13 case: toplam 115 test (53 Domain, 9 Application, 53 Integration). Reproduction ile düzeltilmiş beklenti ayrı case'lerdir. Full solution testleri, EF model/migration tutarlılığı ve development migration uygulaması doğrulandı.

## Sonraki adım

Bu milestone tamamlandığında aynı yatırma isteğini iki kez göndermek iki kez bakiye artırıyordu. Bu problem daha sonra [09 — Test yatırmasında idempotency](09-test-deposit-idempotency.md) çalışmasında çözüldü. Sıradaki finansal senaryo transfer ve bakiye azalışıyla double-spending. Gerçek banka fonu, auth, DB append-only koruması ve event sourcing tamamlanmış sayılmaz.
