# LAB-006 — Tekrarlanan transfer isteği ve idempotency

**Durum:** Aynı mantıksal transferin sıralı veya paralel tekrarında tek finansal hareket garanti edildi.
**Mimari karar:** [ADR-0011](../../adr/0011-transfer-idempotency.md)

## Problem

İstemci 100 TRY transfer ederken database commit olabilir fakat HTTP cevabı istemciye ulaşmayabilir. İstemci transferin sonucunu bilmediği için aynı isteği tekrar gönderir. Sistem tekrar denemeyi yeni işlem sanırsa para iki kez taşınır.

## Problemi reproduce et

Koruma eklenmeden önce kaynakta 250 TRY varken aynı 100 TRY isteğini art arda iki kez gönderen gerçek HTTP testi yazdık:

```text
ilk istek:   kaynak 250 → 150, hedef 0 → 100
tekrar:      kaynak 150 →  50, hedef 100 → 200
sonuç:       iki farklı journal, iki ayrı para hareketi
```

Test yeşildi; çünkü o anki sistemin hatalı davranışını doğru biçimde kanıtlıyordu. API'nin iki HTTP isteğinin aynı kullanıcı niyetine ait olduğunu anlayabileceği bir kimlik yoktu.

## Root cause

HTTP cevabının gelmemesi database'in commit olmadığı anlamına gelmez. Ayrıca request body'sinin aynı olması da tekrar kanıtı değildir; kullanıcı aynı kişiye aynı tutarı gerçekten iki kez göndermek isteyebilir. Sisteme “bu, önceki işlemin tekrarıdır” bilgisini taşıyan ayrı bir anahtar gerekir.

## Sözleşme

İstemci her yeni transfer niyeti için benzersiz bir `Idempotency-Key` üretir. Cevabı alamazsa body'yi ve anahtarı değiştirmeden yeniden gönderir:

```http
POST /api/transfers
Idempotency-Key: 2f45e8575c7d4de8b4ca1e776ab20495
Content-Type: application/json

{
  "sourceWalletId": "...",
  "destinationWalletId": "...",
  "amount": 100
}
```

- Aynı key + aynı kaynak, hedef ve tutar: yeni hareket yapılmaz; ilk kalıcı makbuz döner.
- Aynı key + farklı hedef veya tutar: `409 Conflict` döner.
- Farklı key + aynı body: yeni bir transfer niyetidir ve tekrar işlenebilir.
- Eksik, boş, başında/sonunda boşluk olan veya 128 karakteri aşan key: `400 Bad Request` döner.

Bu aşamada key'in kapsamı `(sourceWalletId, idempotencyKey)` olarak seçildi. Auth/client kimliği eklendiğinde kapsam istemci veya hesap kimliğiyle yeniden değerlendirilebilir.

## Alternatifler ve trade-off

| Seçenek | Kazanç | Bedel |
|---|---|---|
| Yalnızca request body hash'i | İstemciden key istemez | Aynı tutarlı gerçek ikinci transferi yanlışlıkla engeller |
| Cache/Redis kaydı | Hızlı lookup ve TTL | Cache kaybı ile finansal kayıt ayrışabilir; atomik commit zorlaşır |
| Genel idempotency tablosu | Endpoint'ler arasında ortak altyapı | Farklı payload ve response tiplerini erken soyutlama karmaşıklığı |
| Kalıcı `wallet_transfers` kaydı | Transfer kimliği, payload ve makbuz aynı transaction'da | Transfer için ayrı tablo ve migration gerekir |

Transferi birinci sınıf finansal işlem olarak tutan son seçenek seçildi. Test yatırmasına özel tablo genelleştirilmedi; iki use-case'in yaşam döngüsü ve gelecekteki alanları aynı olmak zorunda değil.

## Implementasyon

`wallet_transfers` kaydı şunları saklar:

```text
TransferId
SourceWalletId + IdempotencyKey   (unique)
DestinationWalletId
Amount + Currency
JournalEntryId                    (unique)
İşlem sonrası kaynak/hedef bakiyesi
CreatedAtUtc
```

Handler önce key ile tamamlanmış transferi arar. Bulursa payload'ı karşılaştırır ve eski makbuzu döner. Bulamazsa wallet bakiyeleri, journal/posting'ler ve transfer kaydı tek `SaveChanges` transaction'ında commit edilir. Böylece “para taşındı ama idempotency kaydı oluşmadı” şeklinde yarım durum bırakılamaz.

İki aynı-key isteği aynı anda ilk kontrolü geçerse database unique index'i son sözü söyler. İlk commit kazanır. Diğer istek committed kaydı okuyabiliyorsa aynı makbuzu döner; henüz okuyamıyorsa kontrollü `409` ile aynı key kullanılarak tekrar denenmesini ister.

## Test ve doğrulama

```powershell
docker compose up -d
dotnet test tests/Ledgerly.IntegrationTests/Ledgerly.IntegrationTests.csproj `
  --filter "Lab=TransferIdempotency" `
  --logger "console;verbosity=minimal"
```

Doğrulanan başlıca durumlar:

- sıralı aynı-key retry tek journal ve tek transfer kaydı bırakır;
- retry ilk `TransferId` ve bakiye makbuzunu aynen döndürür;
- aynı key'in farklı payload ile kullanımı `409` olur;
- aynı payload farklı key ile iki gerçek transfer olarak işlenir;
- anahtarsız istek hiçbir transfer yazısı bırakmadan `400` olur;
- paralel aynı-key yarışında yalnızca bir finansal hareket commit edilir.

Tüm test sonucu: 60 domain + 18 application + 82 integration = 160 başarılı test.

## Sınırlar

Idempotency, bir transferin iş kurallarına uygun veya bakiyenin yeterli olmasını sağlamaz; yalnızca aynı niyetin tekrar uygulanmasını engeller. Key saklama süresi şu an kalıcıdır; retention/archiving politikası yoktur. Auth, dış banka transferi, `Pending/Completed/Failed` durum makinesi ve asynchronous processing sonraki senaryolardır.

## Mülakatta nasıl anlatırım?

> Commit olmuş ama cevabı kaybolmuş transfer retry edildiğinde body karşılaştırmasının yeterli olmadığını önce iki kez para taşıyan HTTP testiyle gösterdim. İstemcinin ürettiği `Idempotency-Key` ile transfer payload'ını ve ilk makbuzu PostgreSQL'de sakladım. Wallet güncellemeleri, double-entry journal ve transfer kaydı aynı transaction'da commit oluyor. `(sourceWalletId, key)` unique index'i paralel tekrarları da koruyor. Aynı payload eski makbuzu alıyor, farklı payload aynı key'i kullanırsa 409 dönüyor.
