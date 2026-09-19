# LAB-004 — Tekrarlanan test yatırması ve idempotency

**Durum:** Çözüm uygulandı; gerçek PostgreSQL ile doğrulandı.
**İlgili akış:** [07 — Test deposit](../../journey/07-test-deposit.md)
**Mimari karar:** [ADR-0009](../../adr/0009-test-deposit-idempotency.md)

## Idempotency nedir?

Bir işlemi aynı **mantıksal işlem kimliğiyle** tekrar çağırdığımızda ek bir finansal etki oluşturmamasıdır. Burada iki HTTP çağrısı olabilir, fakat tek yatırma ve tek journal kalmalıdır. Bu, tüm HTTP isteklerini tek seferde teslim etme garantisi değildir; tekrarlanan çağrıyı güvenle tanıma ve önceki sonucu geri verme davranışıdır.

## Bizim senaryomuz: Yanıt kaybolursa ne olur?

```text
İstemci: 100 TRY yatır (key=A)
Sunucu: wallet +100, journal/posting'ler kaydedildi
Yanıt: ağda kayboldu; istemci başarıyı bilmiyor
İstemci: aynı yatırmayı tekrar gönderir (key=A)
İstenen: yeni para yazılmadan ilk makbuz geri döner
```

Gerçek ağ kopmasını üretmiyoruz. İlk istek tamamlandıktan sonra aynı mantıksal isteği yeniden göndererek kopma sonrası retry'nin etkisini deterministik biçimde ölçüyoruz. Aynı cüzdana aynı tutarda *yeni* bir yatırma da meşrudur; bunun key'i farklı olmalıdır.

## Problem → reproduce → root cause

Çözüm öncesi characterization testinde aynı cüzdana iki kez `100 TRY` gönderdik. İki `200 OK`, farklı iki journal, dört posting ve `200 TRY` bakiye gördük. Testin o zamanki yeşil sonucu **hatalı mevcut davranışın kanıtıydı**; çözüm kanıtı değildi. Sonrasında testi istenen davranışı doğrulayacak şekilde güncelledik.

`TestDepositRequest` yalnızca amount taşıyordu. Her geçerli çağrı yeni bir journal ve bakiye artışı üretiyordu. Sunucunun “bu daha önce işlenmiş aynı işlem mi?” sorusunu cevaplayacak kimliği ve kalıcı sonucu yoktu. Optimistic concurrency, aynı eski bakiyeyi okuyup **eşzamanlı** yazan istekleri korur; başarıyla tamamlanan **sıralı** iki isteğin aynı iş olduğunu anlayamaz.

## Alternatifler ve trade-off

| Seçenek | Avantaj | Bedel / risk |
|---|---|---|
| `walletId + amount` ile tekrar saymak | Kolay görünür | Gerçekten istenen ikinci 100 TRY yatırmasını yanlışlıkla engeller; reddedildi |
| İstemcinin ürettiği `Idempotency-Key` HTTP başlığı | HTTP retry için açık sözleşme; body değişmez | İstemci aynı işlemde aynı key'i korumalı; sunucuda kalıcı kayıt gerekir |
| Gövdede `OperationId` | İşlem kimliği domain/mesajlaşma akışına taşınabilir | API gövdesi ve istemci sözleşmesi değişir |
| Bellek içi cache | Hızlı | Restart ve çoklu instance'da garanti kaybolur; finansal doğruluk için yeterli değil |

Bu aşamada header ve PostgreSQL kaydı seçildi. Tek veritabanı ve test amaçlı endpoint için ek servis gerektirmiyor. Daha sonra banka/ödeme sağlayıcısı eklendiğinde onların işlem kimliği ve mutabakatı ayrıca tasarlanacak.

## Karar ve HTTP sözleşmesi

İstemci her **yeni mantıksal yatırma** için yeni bir `Idempotency-Key` üretir; timeout/bağlantı hatası sonrası tekrar denerken aynı key'i kullanır. Kapsam `(walletId, key)`; farklı cüzdanlar aynı key metnini kullanabilir. Key 1–128 karakter olmalı ve başında/sonunda boşluk bulunmamalı.

| İstek | Sonuç |
|---|---|
| Key eksik/geçersiz | `400`, veri değişmez |
| Yeni key + geçerli amount | `200`, yeni journal ve makbuz |
| Aynı wallet + key + amount | `200`, **ilk makbuzun aynısı**; yeni bakiye artışı yok |
| Aynı wallet + key + farklı amount | `409`, ikinci journal yok |
| Aynı wallet + farklı key + aynı amount | Yeni yatırma; ikinci journal meşrudur |

Henüz tamamlanmamış paralel bir işlemle çakışan istek `409` da alabilir. İstemci **yeni key üretmeden** aynı key ile tekrar denediğinde, kazanan işlem commit ettiyse ilk makbuzu alır.

```http
POST /api/wallets/{walletId}/test-deposits
Idempotency-Key: client-operation-123
Content-Type: application/json

{"amount":100}
```

## Implementasyon ve garanti sınırı

`test_deposit_operations` tablosu `(wallet_id, key)` birincil anahtarıyla kaydedilmiş amount ve makbuzu tutar. Handler önce bu kaydı arar. Bulursa tutarı karşılaştırıp eski makbuzu döner; bulamazsa wallet, gerekirse ledger hesapları, journal, iki posting **ve idempotency kaydını tek `SaveChanges` transaction'ında** yazar. Böylece key'in kaydedilip paranın kaydedilmemesi veya tersi önlenir. Key tekilliğini yalnızca uygulama sorgusuna bırakmayız; PostgreSQL primary key de korur.

İki aynı-key istek aynı anda “kayıt yok” görebilir. Yazarken yalnızca biri commit eder. Diğeri unique constraint, bakiye concurrency veya PostgreSQL deadlock çakışmasıyla geri alınır; ardından kazananın commit edilmiş kaydı okunur. Tutar eşleşirse aynı makbuz döner, eşleşmezse `409` üretilir. Kazanan henüz commit etmediği için kayıt okunamıyorsa istek kontrollü `409` alır; istemci aynı key ile tekrar denemelidir. Çatışma asla sahte bir başarıya çevrilmez. Bu, bağımsız iki yeni yatırmanın güvenle otomatik retry edileceği anlamına gelmez.

Makbuzdaki `balance`, **ilk işlemin ardından görülen bakiyedir**. Sonradan başka yatırma olmuşsa replay makbuzu eski değeri gösterir; güncel bakiye için `GET /api/wallets/{walletId}` kullanılmalıdır.

## Test ve doğrulama

```powershell
docker compose up -d
dotnet test tests/Ledgerly.IntegrationTests/Ledgerly.IntegrationTests.csproj `
  --filter "Lab=TestDepositIdempotency" `
  --logger "console;verbosity=minimal"
```

Gerçek PostgreSQL HTTP testleri sıralı tekrar, eksik/uzun key, farklı tutarla key kullanımı, farklı wallet'larda aynı key, aynı-key paralel tekrar ve paralel farklı tutarları kontrol eder. Aynı-key yarışında tam bir journal ve iki posting kalır; yarış sonrası retry ilk makbuzu döner. Mevcut rollback testi artık idempotency tablosunun da boş kaldığını doğrular. Application testleri replay'in wallet yüklemeden ve save yapmadan döndüğünü gösterir. Paralel aynı-key testi arka arkaya beş koşuda da geçti.

## Neyi önledik, neyi henüz çözmedik?

Yanıtın kaybolmasından sonra aynı key ile yapılan retry'nin cüzdana ikinci kez test parası yazmasını önledik. Yeni key ile gelen istek hâlâ yeni işlemdir; istemci yanlışlıkla key değiştirirse bunu sunucu anlayamaz. Key kayıtları bu aşamada süresiz saklanır; TTL/temizlik politikası yoktur. Endpoint yalnızca Development/IntegrationTests ortamlarında açıktır. Bu çalışma dış bankada tam-bir-kez ödeme garantisi, fraud kontrolü veya banka mutabakatı sağlamaz.

## Mülakatta kısa anlatım

“Yatırma commit olup HTTP yanıtı kaybolursa istemci tekrar deneyebilir. Önce aynı isteğin iki ayrı journal ve iki bakiye artışı oluşturduğunu PostgreSQL testiyle gösterdik. İstemci kaynaklı Idempotency-Key'i wallet kapsamında kalıcı ve unique tuttuk; sonucu bakiye ve journal ile aynı transaction'da kaydettik. Commit sonrası retry aynı makbuzu döndürüyor; işlem hâlâ sürerken çakışma olursa aynı key ile yeniden denenebilir. Bu, dış bankaya yapılan işlemler için ayrıca idempotency/mutabakat gereksinimini ortadan kaldırmıyor.”
