# Servis Sınırları ve Bounded Context Adayları

**Durum:** Başlangıç hipotezi  
**Tarih:** 2026-09-03

## Amaç

Bu belge Ledgerly'nin iş yeteneklerini, olası bounded context'lerini ve ileride ayrı deploy edilebilir servislere dönüşebilecek sınırları tanımlar. Buradaki servis haritası başlangıçtan kurulacak fiziksel mikroservislerin listesi değildir. Sınırlar, sistem geliştikçe elde edilen coupling, tutarlılık, ölçek ve hata izolasyonu kanıtlarıyla doğrulanacaktır.

## Sınır belirleme ölçütleri

Bir yeteneği ayrı bounded context veya servis olarak değerlendirmeden önce şu sorular sorulur:

1. **Neler birlikte atomik olmak zorunda?** Aynı transaction içinde başarılı veya başarısız olması gereken kurallar mümkün olduğunca aynı tutarlılık sınırında tutulur.
2. **Verinin sahibi kim?** Her veri yalnızca bir bounded context tarafından yazılır. Başka context'ler veriyi API, event veya projection üzerinden tüketir.
3. **İş dili ve kurallar ayrışıyor mu?** Aynı kelime farklı context'lerde farklı anlam taşıyorsa veya farklı uzmanlık gerektiriyorsa ayrı model ihtimali güçlenir.
4. **Bağımsız ölçekleme ihtiyacı var mı?** Okuma ve yazma yükleri ya da latency hedefleri belirgin biçimde farklıysa ayrıştırma değer kazanabilir.
5. **Hata izolasyonu gerekli mi?** Bir yeteneğin arızası çekirdek para hareketini durdurmamalıysa asenkron ve ayrı bir sınır düşünülebilir.
6. **Bağımsız değişim nedeni var mı?** Farklı iş kuralları, release sıklığı veya sahiplik modeli bağımsız deployment için gerekçe oluşturabilir.

Entity veya tablo başına servis oluşturmak bir sınırlandırma yöntemi değildir. `UserService`, `WalletService`, `BalanceService` ve `TransactionService` gibi aşırı parçalı bir yapı, ağ üzerinden birbirine bağımlı bir distributed monolith üretebilir.

## Başlangıç context haritası

| Bounded context adayı | Sahip olduğu sorumluluk | İlk aşamadaki durum |
|---|---|---|
| Wallet Core | Wallet yaşam döngüsü, bakiye, para yatırma/çekme, iç transfer ve ledger kayıtları | İlk geliştirilecek çekirdek |
| Transaction History | Kullanıcı odaklı işlem geçmişi ve sorgu projection'ları | CQRS aşamasında MongoDB ile eklenecek |
| Risk/Fraud | Transfer değerlendirme, risk kararı ve dış fraud sağlayıcısı adaptasyonu | Resilience senaryosunda eklenecek |
| Payment Gateway | Banka/kart sağlayıcısı üzerinden dış para giriş-çıkış akışları | Dış entegrasyon aşamasında eklenecek |
| Notification | E-posta, push ve benzeri yan etkiler | Event-driven aşamada consumer olarak eklenecek |
| Identity/Customer | Kimlik doğrulama, profil ve ileride KYC süreçleri | Finansal çekirdek doğrulandıktan sonra değerlendirilecek |

## İlk sınır: Wallet Core

İlk deploy edilebilir uygulama Wallet Core'dur. Şu yetenekleri birlikte sahiplenir:

- Wallet oluşturma ve durum yönetimi
- Tek para birimiyle bakiye yönetimi
- Test amaçlı para yatırma
- Para çekme
- Wallet-to-wallet transfer
- Double-entry ledger kayıtları
- Bakiye ve işlem geçmişi sorguları

Wallet ve ledger başlangıçta ayrılmaz. Bir iç transfer sırasında kaynak wallet'ın azalması, hedef wallet'ın artması ve dengeli ledger kayıtlarının oluşması tek atomik transaction gerektirir. Bunları ilk günden ağ sınırıyla ayırmak, henüz temel domain doğrulanmadan distributed transaction problemi üretir.

## Ayrıştırma kararı için kanıtlar

Bir modül ancak aşağıdaki kanıtlardan biri veya birkaçı oluştuğunda ayrı servise aday olur:

- Bağımsız deployment ihtiyacı
- Ölçülmüş ve farklı bir ölçekleme profili
- Ayrı veri sahipliği ve zayıf tutarlılık gereksinimi
- Hata izolasyonu ihtiyacı
- Net bir bounded context dili ve iş kuralı seti
- Sürekli değişen bağımsız entegrasyonlar
- Mevcut process içi bağımlılıkların açık contract'a dönüştürülebilmesi

> Başlangıç servis haritası bir hedef yön gösterir; fiziksel ayrıştırma için tek başına gerekçe oluşturmaz.
