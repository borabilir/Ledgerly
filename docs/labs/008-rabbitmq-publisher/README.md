# LAB-008 — Outbox mesajını gerçek RabbitMQ'ya yayınlamak

**Durum:** RabbitMQ container'ı ve gerçek publisher adaptörü eklendi; mesajın bağlı kuyruğa ulaştığı integration testiyle doğrulandı.  
**Mimari karar:** [ADR-0013](../../adr/0013-rabbitmq-publisher.md)

## Problem

Transactional Outbox, transfer eventi kaybolmasın diye event niyetini PostgreSQL'de güvenle saklıyordu. Ancak `LoggingIntegrationEventPublisher` yalnızca mesajı log'a yazıyordu. Başka bir servis veya worker'ın okuyabileceği gerçek bir mesaj kuyruğu yoktu.

Bu nedenle şu soru henüz cevaplanmamıştı:

> Outbox worker başarılı dediğinde mesaj gerçekten broker tarafından kabul edilip bir kuyruğa yönlendirilmiş mi?

## Kavramlar

- **Broker:** Uygulamaların birbirine doğrudan bağlı olmadan mesaj bırakıp alabildiği aracı sistemdir. Bu lab'da broker RabbitMQ'dur.
- **Exchange:** Publisher mesajı doğrudan kuyruğa değil exchange'e gönderir. Exchange, routing kurallarına göre mesajı uygun kuyruklara dağıtır.
- **Queue:** Consumer'ın daha sonra okuyacağı mesajların beklediği yerdir.
- **Binding:** Bir exchange ile queue arasındaki yönlendirme kuralıdır.
- **Routing key:** Mesajın hangi binding ile eşleşeceğini belirleyen anahtardır. Biz event type değerini kullanıyoruz.
- **Publisher confirm:** Broker'ın publish edilen mesajı kabul ettiğini publisher'a bildirmesidir.

Ledgerly topolojisi:

```text
OutboxPublisherWorker
        |
        v
RabbitMqIntegrationEventPublisher
        |
        | routing key: wallet.transfer-completed.v1
        v
topic exchange: ledgerly.events
        |
        | binding
        v
queue: ledgerly.transfer-history
```

## Neden ihtiyaç duyduk?

Log'a yazmak bir entegrasyon değildir. Notification, fraud veya işlem geçmişi gibi bir bileşen mesajı daha sonra tüketmek isterse kalıcı ve uygulamadan bağımsız bir teslim noktasına ihtiyaç duyar.

RabbitMQ olmadan:

- API ile consumer doğrudan HTTP üzerinden konuşmak zorunda kalabilir.
- Consumer geçici olarak kapalıysa olay kaybolabilir veya transfer isteği başarısız olabilir.
- Birden fazla consumer eklemek producer'ı değiştirmeyi gerektirebilir.
- Mesaj bir süreç içi log satırından ibaret kalır.

## Alternatifler ve trade-off

| Seçenek | Güçlü yanı | Bedeli |
|---|---|---|
| Doğrudan HTTP çağrısı | Basit ve anlık cevap | Sıkı bağlılık; hedef servis kapalıysa request etkilenir |
| PostgreSQL tablosunu consumer'ın okuması | Yeni altyapı gerekmez | Servisler aynı veritabanına bağlanır ve sahiplik sınırı bozulur |
| RabbitMQ | Routing, queue, acknowledgement ve olgun .NET istemcisi | Ayrı altyapı ve operasyon bilgisi gerekir |
| Kafka | Yüksek throughput, replay ve uzun event saklama | Bu aşamadaki command/event teslim ihtiyacı için daha ağırdır |
| Cloud queue/service bus | Yönetilen altyapı | Sağlayıcı bağımlılığı ve lokal geliştirme farkı yaratır |

Bu aşamada öğrenme hedefi, düşük kurulum maliyeti ve queue tabanlı teslim modeli nedeniyle RabbitMQ seçildi.

## Kurulum

`compose.yml` dosyasına `rabbitmq:4.3.6-management` servisi eklendi:

```yaml
rabbitmq:
  image: rabbitmq:4.3.6-management
  ports:
    - "5672:5672"
    - "15672:15672"
  volumes:
    - ledgerly_rabbitmq_data:/var/lib/rabbitmq
```

- `5672`: uygulamanın AMQP bağlantısı için kullanılır.
- `15672`: RabbitMQ yönetim ekranıdır.
- Named volume: container yeniden oluşturulsa bile broker verisini korur.
- Healthcheck: broker bağlantı kabul edecek duruma gelmeden servisi healthy saymaz.
- Kullanıcı adı ve parola compose environment değişkenleriyle verilir; development varsayılanları `ledgerly / ledgerly_dev` değerleridir.

Çalıştırma:

```powershell
docker compose up -d
docker compose ps rabbitmq
```

Yönetim ekranı: `http://localhost:15672`

## Uygulamada nasıl kullandık?

Infrastructure katmanına resmi `RabbitMQ.Client` paketi ve `RabbitMqIntegrationEventPublisher` eklendi. Ayarlar `RabbitMqOptions` üzerinden configuration'dan okunuyor. RabbitMQ genel `appsettings.json` içinde güvenli varsayılan olarak kapalı; lokal bağlantı bilgileri `appsettings.Development.json` içinde açık. Başka ortamlarda gerçek adres ve secret'lar ortam değişkenleri veya secret store ile verilmelidir.

Publisher şunları yapıyor:

1. Bağlantı ve channel'ı her mesaj için yeniden açmak yerine uzun ömürlü tutuyor.
2. Durable topic exchange ve durable queue oluşturuyor.
3. Queue'yu `wallet.transfer-completed.v1` routing key'iyle exchange'e bağlıyor.
4. Mesajı persistent olarak yayınlıyor.
5. Publisher confirm açık olduğu için broker kabul etmezse publish başarılı sayılmıyor.
6. Publish hatasında bozuk connection/channel kapatılıyor ve exception Outbox processor'a bırakılıyor.
7. Outbox processor mesajı işlenmiş olarak işaretlemiyor; sonraki polling turunda tekrar deniyor.

```text
RabbitMQ açık
  -> publish confirm gelir
  -> Outbox ProcessedAtUtc doldurulur

RabbitMQ kapalı / publish başarısız
  -> publisher exception fırlatır
  -> AttemptCount ve LastError güncellenir
  -> Outbox satırı pending kalır
  -> sonraki turda tekrar denenir
```

Integration test ortamındaki API worker'ı kapalı tutulmaya devam ediyor. Böylece mevcut Outbox testleri zamanlamaya bağlı kalmıyor. RabbitMQ publisher testi ise adaptörü doğrudan gerçek container'a bağlayıp ayrı bir test exchange ve queue kullanıyor.

## Doğrulama

```powershell
docker compose up -d rabbitmq
dotnet test tests/Ledgerly.IntegrationTests/Ledgerly.IntegrationTests.csproj `
  --configuration Release `
  --filter "Lab=RabbitMqPublisher"
```

Test şunları doğrular:

- Gerçek RabbitMQ bağlantısı kurulabiliyor.
- Mesaj topic exchange'e publish ediliyor.
- Binding mesajı beklenen queue'ya yönlendiriyor.
- Payload değişmeden geliyor.
- `MessageId`, event type ve content type metadata'sı korunuyor.
- Mesaj persistent işaretleniyor.

## Sağladığımız garanti ne kadar?

Publisher confirm, mesajın broker tarafından kabul edildiğini söyler; consumer'ın mesajı işlediğini söylemez. Ayrıca broker kabul ettikten sonra uygulama `ProcessedAtUtc` yazamadan çökerse aynı event tekrar publish edilebilir.

Bu yüzden uçtan uca garanti hâlâ **at-least-once**'dur. Consumer `EventId` üzerinden idempotent olmak zorundadır.

## Mevcut sınırlar

- `ledgerly.transfer-history` kuyruğunu okuyacak consumer henüz yok; mesajlar kuyrukta bekler.
- Consumer acknowledgement ve Inbox/Idempotent Consumer henüz uygulanmadı.
- Dead-letter queue ve poison-message politikası yok.
- Retry için exponential backoff ve jitter yok.
- Lokal kullanıcı/parola yalnızca development içindir; secret store ve TLS henüz yok.
- Birden fazla API instance'ının aynı outbox satırını claim etmesini engelleyen mekanizma henüz yok.

## Mülakatta nasıl anlatırım?

> Transactional Outbox event niyetini güvenle saklıyordu ama publisher yalnızca log'a yazıyordu. Docker Compose ile RabbitMQ ekledim ve Infrastructure katmanında gerçek publisher adaptörü oluşturdum. Publisher mesajı durable topic exchange'e event type routing key'iyle, persistent olarak gönderiyor ve publisher confirm bekliyor. Hata olursa exception Outbox processor'a dönüyor; satır pending kalıp tekrar deneniyor. Gerçek broker kullanan integration testinde mesajın bağlı kuyruğa ulaştığını ve metadata'sını doğruladım. Bunun consumer'ın işlediği anlamına gelmediğini ve sistemin hâlâ at-least-once olduğunu özellikle ayırıyorum; sıradaki adım idempotent consumer ve Inbox pattern.
