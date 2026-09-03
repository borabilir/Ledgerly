# ADR-0001 — Baseline'a Modüler Tek Servisle Başlamak

**Durum:** Accepted  
**Tarih:** 2026-09-03

## Bağlam

Ledgerly'nin öğrenme kapsamı DDD, CQRS, event-driven iletişim, NoSQL ve mikroservis mimarisini içerir. Ancak proje henüz çalışan bir domain modeline, ölçülmüş trafik karakteristiğine veya doğrulanmış servis sınırlarına sahip değildir.

İlk günden çok sayıda mikroservis oluşturmak servisler arası iletişim, dağıtık transaction, broker, local orchestration, gözlemlenebilirlik ve test ortamı maliyetini aynı anda getirir. Bu karmaşıklık baseline finansal davranışları anlamayı ve sorunların root cause'unu izole etmeyi zorlaştırır.

## Karar

Ledgerly tek deploy edilen **Wallet Core** uygulamasıyla başlayacaktır. Uygulama kendi içinde Domain, Application, Infrastructure ve API katmanlarına ayrılır. Başlangıçta tek PostgreSQL veritabanı kullanılır.

```text
HTTP Client
    |
    v
Ledgerly.Api
    |
    v
Wallet Core
    |
    v
PostgreSQL
```

Bu yapı modüler monolith'e doğru evrilebilecek bir baseline'dır; dört katman dört mikroservis değildir.

## Neden mikroservis hedefi korunuyor?

Mikroservisler aşağıdaki gerçek ihtiyaçları çalışmak için hedef mimaride kalır:

- Transaction history okuma yükünü bağımsız ölçeklemek
- Notification gibi yan etkileri çekirdek transferden izole etmek
- Dış banka ve fraud entegrasyonlarının hatalarını sınırlamak
- Database ownership ve contract tasarımını uygulamak
- Outbox, idempotent consumer, saga ve eventual consistency senaryolarını üretmek
- Tek instance'tan bağımsız ölçeklenen deployment'lara geçişi gözlemlemek

Fakat bu ihtiyaçlar ortaya çıkmadan fiziksel servis ayrıştırması yapılmayacaktır.

## Alternatifler

### İlk günden mikroservisler

Gerçek dağıtık sistem problemlerini erken gösterir; ancak domain henüz doğrulanmadığı için yanlış sınır, yüksek operasyon maliyeti ve distributed monolith riski taşır.

### Katmansız tek proje

En hızlı başlangıcı sağlar; fakat domain kuralları ile veri erişimi ve HTTP ayrımını görünür tutmayı zorlaştırır.

### Modüler tek servis — seçilen

Domain sınırlarını process içinde görünür kılar, finansal invariant'ların tek transaction ile uygulanmasını sağlar ve daha sonra yapılacak ayrıştırma için karşılaştırılabilir bir baseline üretir.

## Sonuçlar ve trade-off'lar

Olumlu sonuçlar:

- İlk vertical slice daha hızlı ve kolay test edilir.
- Finansal invariant'lar tek veritabanı transaction'ında korunabilir.
- Concurrency ve idempotency problemleri altyapı gürültüsü olmadan incelenebilir.
- Gelecekteki ayrıştırmanın maliyeti ve faydası ölçülebilir.

Kabul edilen maliyetler:

- Başlangıç sistemi mikroservis deneyimi sağlamaz.
- Katman sınırlarının kod disipliniyle korunması gerekir.
- Bazı modüller ileride ayrılırken contract ve veri migrasyonu gerekecektir.

## Yeniden değerlendirme koşulları

Bu karar; CQRS read model, event broker, dış provider veya bağımsız ölçekleme senaryolarından biri ölçülebilir bir servis sınırı ürettiğinde yeniden değerlendirilecektir.
