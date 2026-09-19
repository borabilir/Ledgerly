# ADR-0012 — Transfer eventleri için Transactional Outbox

**Durum:** Kabul edildi — 2026-09-19

## Bağlam

Transfer tamamlandıktan sonra integration event yayınlamak PostgreSQL commit'i ve broker publish'i olmak üzere iki bağımsız write oluşturur. Publisher hatası testinde transfer, bakiye ve journal commit edildiği hâlde event teslim edilemedi. HTTP idempotency retry'ı tamamlanmış transferi replay ettiği için kayıp event'i yeniden üretmedi.

## Karar

`TransferCompletedIntegrationEvent`, transferle aynı `LedgerlyDbContext` ve aynı `SaveChanges` transaction'ında `outbox_messages` tablosuna yazılır. Request path broker'a doğrudan publish yapmaz.

Host tarafından yönetilen `OutboxPublisherWorker`, pending satırları `OutboxProcessor` ile batch olarak okur. Başarılı publish sonrasında `ProcessedAtUtc`; başarısız publish sonrasında `AttemptCount` ve `LastError` kaydedilir. Event type CLR sınıf adına bağlanmaz; version'lı `wallet.transfer-completed.v1` sözleşmesi kullanılır.

Outbox ile broker arasında distributed transaction kurulmaz. Publish başarılı olup processed işareti yazılamazsa duplicate mümkündür. Teslimat garantisi at-least-once kabul edilir ve event kimliği consumer idempotency'sinin temeli olur.

## Alternatifler

Doğrudan publish event kaybedebilir. İşlemden önce publish hayalet event üretebilir. 2PC mevcut öğrenme aşaması için ağır ve altyapıya sıkı bağlıdır. CDC güçlü bir sonraki seçenek olsa da Debezium/Kafka Connect gibi ayrı operasyonel bileşenler gerektirir.

## Sonuçlar ve sınırlar

Transfer isteğinin başarısı broker erişilebilirliğine bağlı değildir. Event niyeti business data ile atomik ve kalıcıdır. Buna karşılık polling gecikmesi, outbox tablo büyümesi ve duplicate teslimat yönetilmelidir.

İlk sürümde gerçek RabbitMQ, backoff/jitter, dead-letter, retention ve multi-instance claim mekanizması yoktu. RabbitMQ adaptörü daha sonra [ADR-0013](0013-rabbitmq-publisher.md) ile eklendi; kalan sınırlar ölçülebilir ayrı senaryolar olarak ele alınacaktır. Ayrıntılı deney: [LAB-007](../labs/007-transactional-outbox/README.md).
