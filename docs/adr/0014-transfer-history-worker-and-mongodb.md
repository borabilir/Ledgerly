# ADR-0014 — Transfer History için ayrı worker ve MongoDB read model

**Durum:** Kabul edildi — 2026-09-19

## Bağlam

Transfer eventleri artık Transactional Outbox üzerinden RabbitMQ'daki `ledgerly.transfer-history` kuyruğuna ulaşıyor. Kuyruğu tüketen bir bileşen ve kullanıcının işlem geçmişini sorgulamaya uygun bir okuma modeli henüz yok.

Ana PostgreSQL modeli finansal doğruluk, transaction ve constraint ihtiyaçlarına göre tasarlandı. İşlem geçmişi ekranının ihtiyacı ise wallet'a göre filtrelenmiş, gösterime hazır transfer dokümanlarını hızlı okumaktır. Bu iki modelin aynı olmak zorunda olmadığı kabul edildi.

## Karar

Transfer history özelliği ayrı çalıştırılabilen `Ledgerly.TransferHistory.Worker` host'unda geliştirilecektir. Worker, RabbitMQ eventlerini tüketerek yalnızca kendi read model'ini MongoDB'de oluşturacaktır.

Worker, Ledgerly API'nin Domain/Application/Infrastructure projelerine referans vermeyecektir. İletişim version'lı integration event sözleşmesi üzerinden yapılacaktır. Böylece producer'ın iç domain modeli consumer'a sızdırılmaz.

MongoDB lokal geliştirmede Docker Compose ile çalıştırılır. Genel configuration'da gerçek connection string bulunmaz; development bağlantısı `appsettings.Development.json` dosyasında tutulur. Production bağlantısı environment variable veya secret store ile sağlanmalıdır.

Bu milestone yalnızca container, worker host'u, MongoDB driver bağlantısı ve startup ping kontrolünü kapsar. Consumer ve collection modeli sonraki problem adımında eklenecektir.

## Alternatifler

| Seçenek | Kazanç | Bedel |
|---|---|---|
| Ana PostgreSQL'e history tablosu eklemek | Yeni veri tabanı ve eventual consistency yok | Read model ile write model tekrar birbirine bağlanır; NoSQL pratiği oluşmaz |
| API process'i içinde consumer çalıştırmak | Daha az deployable | API ölçekleme ve consumer ölçekleme birbirine bağlanır |
| Consumer'ın Ledgerly PostgreSQL'ini doğrudan okuması | Event tüketimi gerekmez | Database ownership ve servis sınırı ihlal edilir |
| Ayrı worker + MongoDB | Bağımsız ölçekleme, event-driven read model ve document model pratiği | Yeni süreç, yeni veri tabanı ve eventual consistency yönetimi |

## Sonuçlar ve sınırlar

Transfer history artık ayrı deploy edilebilir bir bileşen ve kendine ait veri deposu sınırına sahip. Ana finansal kayıt sistemi PostgreSQL olmaya devam eder; MongoDB source of truth değildir, eventlerden yeniden üretilebilen bir read model olacaktır.

İlk sürümde worker yalnızca MongoDB bağlantısını doğrular. RabbitMQ consumer, acknowledgement, collection/index tasarımı, duplicate reproduce, Inbox/idempotency, retry ve dead-letter davranışı henüz yoktur.

Ayrıntılı kurulum: [Journey 14](../journey/14-transfer-history-worker-bootstrap.md).
