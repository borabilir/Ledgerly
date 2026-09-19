# LAB-005 — Wallet transfer ve double-spending

**Durum:** Baseline transfer uygulandı; aynı bakiyenin eşzamanlı harcanması gerçek PostgreSQL ile reproduce edilip korundu.
**Mimari karar:** [ADR-0010](../../adr/0010-wallet-transfer-and-double-spending.md)

## Problem

Kaynak wallet'ta 100 TRY varken iki istek aynı anda farklı hedeflere 80'er TRY göndermek isteyebilir. İkisi de diğer işlem commit etmeden 100 bakiyeyi okursa ikisi de “yeterli para var” kararını verebilir. Her iki karar kalıcı olursa kaynak yalnızca 80 azalırken hedefler toplam 160 artabilir. Aynı para iki kez harcanmış olur; buna double-spending diyoruz.

Korunacak kurallar:

```text
Kaynak ve hedef farklı wallet olmalı.
Tutar pozitif ve desteklenen hassasiyette olmalı.
Kaynak bakiye tutardan küçük olamaz.
Kaynak azalışı + hedef artışı + journal birlikte commit edilmeli.
İki rakip harcamadan en fazla biri aynı başlangıç bakiyesini kullanabilmeli.
```

Şimdilik yalnızca TRY desteklenir. Handler para birimlerinin eşitliğini kontrol eder; kur dönüşümü yapmaz.

## Baseline transfer

`POST /api/transfers` kaynak wallet ID, hedef wallet ID ve tutarı alır. `Wallet.Debit` negatif bakiyeyi domain seviyesinde engeller. Transfer handler iki wallet'ı tracked yükler, eksik wallet ledger hesaplarını hazırlar ve şu dengeli journal'ı oluşturur:

```text
Kaynak wallet liability hesabı   Debit   40 TRY
Hedef wallet liability hesabı    Credit  40 TRY
```

Liability hesabında debit, kurumun kaynak kullanıcıya borcunu azaltır; credit, hedef kullanıcıya borcunu artırır. Wallet bakiyeleri, hesaplar, journal ve posting'ler tek `SaveChanges` transaction'ında kalır.

## Problemi deterministik reproduce et

`TransferConcurrencyTests` iki ayrı DbContext'e aynı kaynak bakiyeyi okutuyor. İkisi de `100 >= 80` kararını verip farklı hedefi 80 artırıyor.

`useGuard=false` kolu yalnızca deney için `ExecuteUpdate` kullanarak EF'nin orijinal bakiye koşulunu atlar:

```text
İstek A: 100 okur → kaynak 20, hedef A 80
İstek B: 100 okur → kaynak 20, hedef B 80

Kalıcı sonuç: kaynak 20 + hedefler 160 = toplam 180
Başlangıç toplamı: 100
```

Testin bu kolda yeşil olması davranışın doğru olduğunu değil, hatanın tekrar üretildiğini gösterir. Production transfer kodu `ExecuteUpdate` kullanmaz.

```powershell
docker compose up -d
dotnet test tests/Ledgerly.IntegrationTests/Ledgerly.IntegrationTests.csproj `
  --filter "Lab=WalletTransfer" `
  --logger "console;verbosity=minimal"
```

## Root cause

“Okuduğum anda bakiye yeterliydi” bilgisi save anına kadar geçerli olmak zorunda değildir. İki istek aynı eski değerden karar verir. Yalnızca transaction kullanmak da bunu otomatik çözmez; iki transaction farklı zamanlarda aynı eski değeri okuyabilir. Yazma sırasında kararın dayandığı orijinal bakiyeyi kontrol etmek gerekir.

## Alternatifler ve trade-off

| Seçenek | Kazanç | Bedel |
|---|---|---|
| Pessimistic `FOR UPDATE` kilidi | İkinci işlem güncel bakiye için bekler | Kilit süresi, sıra standardı ve deadlock yönetimi |
| Serializable isolation + retry | Veritabanı çelişkili yürütmeyi reddeder | Retry politikası ve daha fazla abort maliyeti |
| Atomik koşullu SQL (`balance >= amount`) | Kısa, güçlü kaynak bakiye kontrolü | İki wallet, receipt ve journal transaction'ını özel SQL ile tasarlamak gerekir |
| EF optimistic concurrency | Mevcut model ve transaction sınırıyla küçük değişiklik | Çakışan isteği `409` ile reddeder; otomatik tamamlamaz |

Bu aşamada mevcut `Balance` concurrency token'ını kullandık. EF update sorgusuna okunan eski bakiyeyi ekler. İlk transfer kaynağı değiştirince ikinci update hiçbir satır bulamaz; EF hata üretir ve ikinci transaction'daki hedef artışı, yeni hesap ve journal da rollback olur.

## Gerçek HTTP doğrulaması

HTTP yarışında iki handler bütün okumalarını tamamladıktan sonra ortak bariyerden save'e bırakılır. Sonuç:

```text
1 x 200 OK
1 x 409 Conflict
kaynak bakiye 20
iki hedefin toplam bakiyesi 80
1 transfer journal'ı ve 2 posting
kaybeden hedefte hesap veya bakiye değişikliği yok
```

Normal transfer, yetersiz bakiye, aynı wallet, eksik hedef, dengeli posting'ler ve OpenAPI cevapları da integration testlerinde doğrulanır.

## Sınırlar ve sıradaki problem

Transfer henüz kendi kalıcı `Transfer` kaydına veya durum makinesine sahip değildir; journal receipt döndürür. Aynı transfer isteği farklı HTTP çağrılarıyla sırayla tekrar gelirse iki ayrı transfer sayılabilir. Test deposit için öğrendiğimiz idempotency yaklaşımını transfer kimliğiyle genelleştirmek sonraki adımdır. Auth, farklı currency, ücret, reversal ve dış sistemler kapsam dışıdır.

## Mülakatta nasıl anlatırım?

> Aynı 100 TRY'yi iki paralel 80 TRY transferinde kullandığımızda yalnızca `balance >= amount` kontrolünün yetmediğini PostgreSQL üzerinde gösterdim. Concurrency guard atlandığında kaynak 20, hedefler toplam 160 oldu ve sistemde para yaratıldı. Wallet balance'ı optimistic concurrency token olarak kullandım; ilk transfer commit edince ikinci transferin eski bakiyeye dayalı update'i reddedildi ve tüm transaction rollback oldu. Böylece kaynak, hedef ve double-entry journal atomik kaldı. Yük veya contention artarsa conditional SQL, row lock ya da serializable isolation seçeneklerini yeniden değerlendiririm.
