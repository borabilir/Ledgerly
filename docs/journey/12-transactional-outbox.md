# 12 — Transactional Outbox

## Başlangıç

Transfer kalıcı ve idempotentti; fakat tamamlanan transferi başka sistemlere güvenilir şekilde bildirecek event akışı yoktu. Database commit'inden sonra doğrudan publish denendiğinde publisher hatası transferi geri alamadı ve event için kalıcı retry bilgisi kalmadı.

## Yapılanlar

- Version'lı `wallet.transfer-completed.v1` integration event sözleşmesi eklendi.
- Publisher hatasıyla dual-write açığı gerçek HTTP ve PostgreSQL üzerinde reproduce edildi.
- `outbox_messages` tablosu, EF mapping'i ve migration eklendi.
- Event niyeti transfer, bakiye ve journal ile aynı Unit of Work'e alındı.
- `OutboxPublisherWorker` ve batch işleyen `OutboxProcessor` eklendi.
- Başarılı/başarısız deneme durumu kalıcı hâle getirildi.
- Broker hatası sonrası retry ve HTTP idempotency ile tek outbox kaydı integration testinde doğrulandı.

[Problem, reproduce ve çözüm](../labs/007-transactional-outbox/README.md), [karar ve trade-off](../adr/0012-transactional-outbox.md).

## Sonraki adım

Outbox şu an logging publisher'a bağlıdır. Sonraki adım gerçek RabbitMQ adaptörünü ve container'ını eklemek; ardından aynı event iki kez teslim edildiğinde consumer tarafında Inbox/Idempotent Consumer korumasını kurmaktır.
