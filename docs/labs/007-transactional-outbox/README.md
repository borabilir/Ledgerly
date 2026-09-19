# LAB-007 — DB commit oldu ama event yayınlanamadı

**Durum:** Dual-write problemi reproduce edildi; Transactional Outbox ve retry worker ile korundu.
**Mimari karar:** [ADR-0012](../../adr/0012-transactional-outbox.md)

## Problem

Başarılı transferden sonra `wallet.transfer-completed.v1` eventi notification, fraud veya read-model gibi başka bileşenlere gönderilmek isteniyor. Naif uygulama iki bağımsız yazma yapar:

```text
1. PostgreSQL transaction'ını commit et
2. Event'i broker'a gönder
```

İlk adım başarılı, ikinci adım başarısız olabilir. Database transaction'ı broker publish işlemini kapsamaz.

## Problemi deterministik reproduce et

Test publisher'ı her publish çağrısında bilerek exception fırlatacak şekilde ayarladık. Outbox öncesindeki doğrudan yayınlama akışında gözlem şuydu:

```text
HTTP sonucu:             500
Kaynak bakiye:           100 → 60
Hedef bakiye:              0 → 40
Transfer + journal:      commit edildi
Event teslimi:           başarısız
Kalıcı retry kaydı:      yok
```

Aynı `Idempotency-Key` ile HTTP retry yapmak da event'i onarmadı. Handler tamamlanmış transferin makbuzunu döndürdü ve yeni publish noktasına gelmedi. Idempotency paranın ikinci kez taşınmasını engelledi ama kayıp event problemini çözmedi.

## Root cause: dual-write

PostgreSQL ve message broker iki ayrı kaynak olduğunda şu iki işlemi normal bir local transaction ile atomik yapmak mümkün değildir:

```text
PostgreSQL commit
Broker publish
```

Sıralamayı ters çevirmek de çözüm değildir. Önce event yayınlanır, sonra database commit başarısız olursa bu kez tüketiciler gerçekleşmemiş bir transferi görür.

## Alternatifler ve trade-off

| Seçenek | Kazanç | Bedel |
|---|---|---|
| Broker'a doğrudan publish | En basit ve düşük gecikmeli akış | Commit ile publish arasında event kaybı |
| Önce publish, sonra commit | Event önce görünür | Gerçekleşmeyen işlem için hayalet event üretilebilir |
| Distributed transaction / 2PC | İki kaynağı birlikte koordine etmeyi hedefler | Broker/database desteği, sıkı bağlılık ve operasyon maliyeti |
| CDC ile transaction log okuma | Uygulama polling'i azalır | Debezium/Kafka Connect gibi ek altyapı ve operasyon bilgisi |
| Transactional Outbox | Business veri ve event niyeti tek DB transaction'ında | Polling gecikmesi, tablo bakımı ve duplicate ihtimali |

Bu aşamada mevcut PostgreSQL transaction sınırına doğal biçimde oturan Transactional Outbox seçildi.

## Karar ve veri modeli

Transfer handler artık broker'a doğrudan erişmiyor. Transfer, wallet bakiyeleri, journal ve event niyeti aynı `SaveChanges` içinde yazılıyor:

```text
PostgreSQL transaction
  ├─ source wallet update
  ├─ destination wallet update
  ├─ wallet_transfers insert
  ├─ journal_entries + postings insert
  └─ outbox_messages insert
```

`outbox_messages` alanları:

```text
id                 event kimliği
aggregate_id       TransferId
type               wallet.transfer-completed.v1
payload            JSON event içeriği
occurred_at_utc    event'in oluştuğu zaman
processed_at_utc   başarıyla publish edildiği zaman
attempt_count      kaç publish denemesi yapıldığı
last_error         son hata
```

`processed_at_utc IS NULL` olan satırlar pending kabul edilir. Pending sorgusunu hızlandırmak için partial index bulunur.

## Background worker nasıl çalışıyor?

`OutboxPublisherWorker` uygulamadan bağımsız bir thread açmaz; ASP.NET host tarafından yönetilen bir `BackgroundService` olarak çalışır:

```text
Pending mesajları sırayla oku
        ↓
Publisher'a gönder
   ├─ başarılı → processed_at_utc yaz, last_error temizle
   └─ hatalı   → attempt_count artır, last_error yaz
        ↓
Bir sonraki polling turunda pending mesajları tekrar dene
```

Test ortamında worker kapalıdır; `OutboxProcessor` doğrudan çağrılarak zamanlama bağımsız, deterministik test yapılır. Development ortamında worker varsayılan olarak açıktır.

## Çözüm testi

Testte publisher önce kapalı, sonra erişilebilir duruma getirildi:

```text
1. Transfer isteği                      → 200 OK
2. Transfer + journal + outbox          → birlikte commit
3. İlk outbox publish denemesi          → hata
4. attempt_count=1, last_error dolu      → event hâlâ pending
5. Publisher düzeltilir
6. İkinci deneme                        → başarılı
7. attempt_count=2, processed_at dolu   → tamamlandı
```

Aynı HTTP isteğinin idempotent retry'ı ikinci outbox satırı üretmedi.

```powershell
docker compose up -d
dotnet test tests/Ledgerly.IntegrationTests/Ledgerly.IntegrationTests.csproj `
  --filter "Lab=TransactionalOutbox" `
  --logger "console;verbosity=minimal"
```

## Neden exactly-once değil?

Broker publish başarılı olduktan hemen sonra uygulama `processed_at_utc` yazamadan çökerse mesaj sonraki turda tekrar yayınlanır:

```text
publish başarılı
        ↓
uygulama çöktü
        ↓
outbox hâlâ pending
        ↓
aynı event tekrar publish edilir
```

Outbox event kaybını önler ama **at-least-once delivery** sağlar. Duplicate event mümkündür. Tüketicinin `EventId` üzerinden idempotent olması gerekir; bu, sıradaki Inbox/Idempotent Consumer senaryosudur.

## Mevcut sınırlar

- Gerçek RabbitMQ adaptörü henüz yok; şimdilik logging publisher ve test publisher kullanılıyor.
- Retry için exponential backoff ve jitter yoktur.
- Sürekli hata veren mesajlar için dead-letter politikası yoktur.
- Birden fazla application instance aynı pending mesajı okuyup duplicate publish edebilir.
- İşlenmiş outbox satırları için retention/arşivleme politikası yoktur.

Bu sınırlar saklanmıyor; sonraki lab'ların problem kaynağı olacak.

## Mülakatta nasıl anlatırım?

> Transfer commit'i ile broker publish işleminin iki ayrı write olduğunu, publisher'ı kontrollü şekilde hata verdirerek gösterdim. Transfer ve journal database'de kalırken event kayboldu; aynı idempotency key ile HTTP retry da event'i onarmadı. Event payload'ını transferle aynı PostgreSQL transaction'ında outbox tablosuna yazdım. Background worker pending mesajları publish ediyor, hatayı ve deneme sayısını kalıcı tutup retry ediyor. Böylece event kaybetmiyoruz; ancak publish sonrası işaretleme başarısız olabileceği için teslimat at-least-once ve consumer'ın idempotent olması gerekiyor.
