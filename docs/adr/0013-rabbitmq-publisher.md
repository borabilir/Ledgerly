# ADR-0013 — Outbox eventleri için RabbitMQ publisher

**Durum:** Kabul edildi — 2026-09-19

## Bağlam

Transactional Outbox transfer eventi ile business verisini aynı PostgreSQL transaction'ında saklıyor ve broker geçici olarak erişilemezken event kaybını önlüyordu. Ancak ilk sürüm yalnızca logging/test publisher kullanıyordu. Outbox mesajının uygulama dışındaki bir consumer'a teslim edilebileceği gerçek broker topolojisi ve adaptörü yoktu.

## Karar

Message broker olarak RabbitMQ; .NET adaptörü olarak resmi `RabbitMQ.Client` kullanılacaktır. Uygulama `wallet.transfer-completed.v1` mesajını durable `ledgerly.events` topic exchange'ine event type routing key'iyle gönderir. `ledgerly.transfer-history` durable queue'su bu routing key ile exchange'e bağlanır.

Mesajlar persistent yayınlanır ve channel üzerinde publisher confirms etkinleştirilir. Connection ve channel uzun ömürlü tutulur; erişim tek publisher örneği içinde serialize edilir. Publish başarısız olduğunda bağlantı sıfırlanır ve hata yutulmaz. Böylece Outbox processor satırı pending bırakıp sonraki turda yeniden deneyebilir.

Development topolojisi Docker Compose ile yönetilir. Integration test API'sinde background worker ve gerçek publisher kapalı kalır; RabbitMQ adaptörü ayrı bir integration testinde doğrudan gerçek broker ile doğrulanır.

## Alternatifler

Doğrudan HTTP basittir fakat producer ile consumer'ı zaman ve erişilebilirlik açısından birbirine bağlar. Kafka replay ve yüksek throughput bakımından güçlüdür ancak mevcut queue tabanlı öğrenme senaryosu için daha yüksek operasyon maliyetine sahiptir. Yönetilen cloud broker'lar production için adaydır fakat lokal öğrenme ortamında sağlayıcı bağımlılığı oluşturur.

## Sonuçlar ve sınırlar

Outbox mesajları artık uygulama dışındaki kalıcı bir kuyruğa teslim edilebilir. Broker'ın geçici hatası transfer request'ini geri almaz; Outbox retry davranışı devam eder. Publisher confirm broker kabulünü kanıtlar, consumer işlemesini kanıtlamaz.

At-least-once teslim nedeniyle duplicate event hâlâ mümkündür. Consumer, Inbox veya eşdeğer idempotency mekanizması kullanmalıdır. İlk sürümde consumer, acknowledgement yönetimi, dead-letter queue, backoff/jitter, TLS/secrets ve multi-instance outbox claim mekanizması yoktur.

Ayrıntılı deney: [LAB-008](../labs/008-rabbitmq-publisher/README.md).
