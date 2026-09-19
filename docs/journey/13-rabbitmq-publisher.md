# 13 — RabbitMQ Publisher

## Başlangıç

Transfer eventi Transactional Outbox içinde güvenle saklanıyor, background worker tarafından retry ediliyor fakat yalnızca logging publisher'a gönderiliyordu. Gerçek bir broker ve consumer'ın okuyabileceği queue yoktu.

## Yapılanlar

- Docker Compose'a healthcheck ve kalıcı volume içeren RabbitMQ management container'ı eklendi.
- Resmi `RabbitMQ.Client` paketi eklendi.
- `RabbitMqOptions` ile broker ve topoloji ayarları configuration'a taşındı.
- Uzun ömürlü connection/channel kullanan `RabbitMqIntegrationEventPublisher` yazıldı.
- Durable topic exchange, durable transfer-history queue ve version'lı routing binding'i oluşturuldu.
- Persistent publish, mandatory routing ve publisher confirms etkinleştirildi.
- Publish hatasının Outbox processor'a taşınması ve bağlantının sonraki retry için yenilenmesi sağlandı.
- Gerçek RabbitMQ container'ına publish edip mesajı queue'dan okuyan integration testi eklendi.
- VS Code infrastructure task'ı PostgreSQL ile RabbitMQ'yu birlikte başlatacak şekilde adlandırıldı.

[Problem ve doğrulama](../labs/008-rabbitmq-publisher/README.md), [karar ve trade-off](../adr/0013-rabbitmq-publisher.md).

## Sonraki adım

Queue şu anda mesajı saklıyor fakat onu işleyen bir consumer yok. Sonraki senaryo, aynı event birden fazla kez teslim edildiğinde yan etkinin birden fazla uygulanmasını önce reproduce etmek; ardından Inbox/Idempotent Consumer ile korumaktır.
