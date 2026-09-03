# 00 — Project Charter

**Durum:** Başlangıç taslağı  
**Tarih:** 2026-09-03

## Projenin adı

**Ledgerly — Digital Wallet & Payment Platform**

## Projenin amacı

Ledgerly, kullanıcıların dijital cüzdanlarını yönetebildiği ve güvenli para transferleri gerçekleştirebildiği bir ödeme/finans platformudur.

Ürün tarafındaki temel yetenekler:

- Cüzdan oluşturma
- Para yatırma
- Para çekme
- Başka bir cüzdana para gönderme
- Bakiye görüntüleme
- İşlem geçmişi görüntüleme

Öğrenme tarafındaki amaç, yalnızca CRUD yapan bir Wallet API geliştirmek değildir. Basit bir sistemden başlayarak finansal doğruluk, concurrency, idempotency, dayanıklılık, mesajlaşma ve ölçeklenebilirlik problemlerini kontrollü senaryolarla ortaya çıkarmak; ardından çözüm alternatiflerini deneyerek sistemi aşamalı biçimde geliştirmektir.

## Öğrenme yaklaşımı

Proje baştan production-grade veya gereksiz derecede kompleks kurulmayacaktır. Her senaryo mümkün olduğunca şu süreçle ele alınacaktır:

```text
Baseline
  -> Problem
  -> Problemi reproduce et
  -> Gözlem ve kanıt
  -> Root cause
  -> Alternatif çözümler
  -> Trade-off
  -> Karar
  -> Implementasyon
  -> Test ve doğrulama
  -> Dokümantasyon
```

Bir teknoloji veya pattern ancak gerçek bir problem tarafından gerekçelendirildiğinde eklenir. Bir senaryonun doğru sonucu yeni bir teknoloji eklememek de olabilir.

## Başlangıç kapsamı

İlk çalışan sürüm aşağıdaki yeteneklerle sınırlıdır:

- Kullanıcı ve wallet oluşturma
- Test amaçlı para yatırma
- Wallet-to-wallet para transferi
- Bakiye görüntüleme
- İşlem geçmişi görüntüleme
- Tek para birimiyle çalışma: TRY
- Double-entry ledger ile finansal kayıt tutma
- Temel domain, integration ve API testleri
- Lokal geliştirme ortamının tekrar üretilebilir olması

## Başlangıç varsayımları

Bu varsayımlar bir ADR veya yeni bir business gereksinimiyle değiştirilebilir:

- Wallet bakiyesi negatif olamaz; overdraft desteklenmez.
- Tamamlanan finansal kayıtlar değiştirilmez veya silinmez.
- İade ve iptal, eski kaydı güncellemek yerine reversal kaydı oluşturur.
- Finansal source of truth ilişkisel ledger kayıtlarıdır.
- Kullanıcıya gösterilen bakiye, ledger'dan doğrudan veya bir projection üzerinden hesaplanır.
- Gerçek banka ve kart ağı yerine kontrol edilebilir simülatörler kullanılır.
- Güvenlik ve finansal doğruluk cache veya process-local state'e emanet edilmez.

## İlk sürümün kapsamı dışında

- Gerçek para, banka veya kart entegrasyonu
- Gerçek PCI DSS, KYC veya AML uyumluluk süreci
- Döviz dönüşümü ve kur yönetimi
- Çoklu para birimi
- İşlem ücretleri ve komisyon dağıtımı
- Kripto para veya blockchain
- Active-active multi-region çalışma
- Kubernetes
- Baştan ayrıştırılmış çok sayıda mikroservis
- Full Event Sourcing
- Kafka, MongoDB ve Redis'in problem oluşmadan eklenmesi

## Başlangıç business invariant'ları

1. Her journal entry dengeli olmalıdır.
2. Aynı para birimindeki debit ve credit toplamları eşit olmalıdır.
3. Overdraft kapalıyken bir wallet kullanılabilir bakiyesinden fazlasını harcayamaz.
4. Tamamlanan ledger kayıtları güncellenemez veya silinemez.
5. Reversal işlemi önceki kaydı değiştirmez; yeni ve ters yönlü posting'ler üretir.
6. Aynı business operation birden fazla finansal etki oluşturmamalıdır.
7. Bir transfer ya tamamlanmalı ya da teşhis edilebilir bir hata/terminal durumuna ulaşmalıdır.
8. Aynı idempotency key farklı bir payload için tekrar kullanılamamalıdır.

Bu invariant'lar ilerleyen aşamalarda executable testlere dönüştürülecektir.

## Hedeflenen öğrenme kapsamı

- Domain-Driven Design ve bounded context'ler
- Modüler monolith'ten mikroservislere geçiş
- Double-entry ledger ve finansal veri modelleme
- SQL transaction, isolation ve locking
- Concurrency ve double-spending
- Idempotent API ve consumer tasarımı
- Event-driven architecture
- Transactional Outbox ve Inbox
- CQRS ve MongoDB read modelleri
- Redis cache ve distributed coordination deneyleri
- Saga, compensation ve reconciliation
- Dış servis resilience yöntemleri
- Observability, incident response ve postmortem
- Load, stress ve chaos testleri
- Horizontal scaling ve darboğaz analizi

## Başarı ölçütleri

Proje, yalnızca endpoint'ler çalıştığında başarılı sayılmaz. Aşağıdaki çıktılar beklenir:

- Kritik invariant'ların otomatik testlerle doğrulanması
- Her eklenen mimari pattern için belgelenmiş bir problem ve reproduce adımı
- Değişiklik öncesi ve sonrası karşılaştırılabilir sonuçlar
- Önemli kararlar için ADR
- Failure senaryoları için tekrar çalıştırılabilir lab'ler
- Operasyonel senaryolar için runbook ve postmortem
- Mimariyi anlatan güncel diyagramlar
- Her önemli senaryodan çıkarılmış kısa bir mülakat anlatımı
- Projenin sıfırdan başka bir makinede kurulabilmesi

## İlk milestone

İlk milestone tek process ve tek ilişkisel veritabanı üzerinde çalışan küçük, doğru ve test edilebilir bir vertical slice olacaktır:

```text
Wallet oluştur
  -> Test bakiyesi yatır
  -> A wallet'ından B wallet'ına transfer yap
  -> Bakiyeleri görüntüle
  -> İşlem geçmişini görüntüle
```

Kafka, MongoDB, Redis veya servis ayrıştırması bu milestone'un parçası değildir.

## İlk planlanan senaryolar

1. Aynı transfer isteğinin eşzamanlı olarak birden fazla kez gelmesi
2. Aynı wallet bakiyesinin iki eşzamanlı işlem tarafından harcanması
3. Milyonlarca işlem içinde transaction history sorgusunun yavaşlaması
4. Veritabanı commit'inden sonra event yayınlanamaması
5. Aynı event'in consumer'a birden fazla kez ulaşması
6. Dış banka/fraud servisinin gecikmesi veya cevap vermemesi
7. Tek instance'tan çoklu instance'a geçiş
8. Toplu maaş ödemesinin kısmen başarısız olması
9. Production endpoint'inin beklenmedik biçimde hata vermesi

Her senaryo [lab şablonu](templates/lab-template.md) kullanılarak kaydedilecektir.

## Açık kararlar

- Backend runtime ve sürümü
- Solution/repository yapısı
- İlk modül sınırları
- İlk API contract'ları
- Ledger tablo modeli
- Concurrency için başlangıç stratejisi
- Test ve local orchestration araçları

Bu kararlar kod yazılmadan önce topluca kesinleştirilmek yerine, ilk milestone için ihtiyaç oldukça ele alınacaktır.
