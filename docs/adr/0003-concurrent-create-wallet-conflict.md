# ADR-0003 — Concurrent Create Wallet İçin Dar Exception Translation

**Durum:** Accepted\
**Tarih:** 2026-09-15\
**İlgili lab:** [LAB-001](../labs/001-concurrent-create-wallet/README.md)

## Bağlam

İki Create Wallet isteği aynı owner/currency için pre-check'i birlikte geçebiliyor. PostgreSQL unique index tek wallet kuralını koruyor; fakat kaybeden isteğin `DbUpdateException`ı API'de 500 oluyor. Bariyerli HTTP testi bu durumu gerçek PostgreSQL ile reproduce etti.

## Karar sürücüleri

- Tek wallet invariant'ını bütün API instance'larında korumak.
- Kaybeden duplicate isteğe 409 sunmak; diğer kayıt hatalarını gizlememek.
- Application'ı EF Core ve Npgsql'den bağımsız tutmak.
- Mevcut tek-aggregate akışına gereksiz koordinasyon altyapısı eklememek.

## Alternatifler

| Seçenek | Fayda | Kabul edilmesi gereken bedel |
|---|---|---|
| Unique + exception translation | Mevcut doğruluk garantisini doğru HTTP semantiğine bağlar | Çakışmada exception maliyeti ve provider hata sınıflandırması |
| Targeted ON CONFLICT DO NOTHING | Duplicate'i exception üretmeden ele alabilir | PostgreSQL'e özel insert yolu ve EF kayıt akışı değişikliği |
| Serializable | Daha geniş transaction invariant'larına uygun | Transaction retry, ek conflict maliyeti; tek başına 409 çözümü değil |
| Pessimistic locking | Ortak kilit üzerinden sıralama | Olmayan wallet yerine kilit hedefi bulma, bekleme ve deadlock yönetimi |
| Distributed lock | Instance'lar arası koordinasyon | Ek servis, ağ çağrısı, lease/sahiplik failure mode'ları |

Performans maddeleri ölçülmüş benchmark sonucu değildir. Ayrıntılı değerlendirme lab'dadır.

## Karar

Unique index korunacak. `LedgerlyDbContext` içindeki explicit `IUnitOfWork.SaveChangesAsync`, yalnızca `PostgresException` SQLSTATE 23505 ve `ux_wallets_owner_id_currency` eşleşmesini `WalletAlreadyExistsException`a çevirecek. Hata tek bir yeni Wallet entry'sine ait değilse orijinal hata korunacak.

Owner/currency entry'den alınacak; provider zinciri `InnerException` olarak korunacak. Constraint adı mapping ve hata filtresinde aynı sabit olacak. Mevcut API exception handler 409 üretmeye devam edecek. Pre-check kalacak; yeni lock, retry, migration veya generic error-mapping framework'ü eklenmeyecek.

## Sonuçlar

Olumlu: Bir 201, bir 409 ve tek wallet aynı bariyerli testle doğrulandı. Primary key unique ihlali ve diğer hatalar gizlenmiyor. Application'a provider bağımlılığı taşınmıyor.

Bedel: Infrastructure bu business conflict'i tanıyor ve constraint adına bağlı. Doğrudan DbContext save çağrıları bu port dönüşümünü kullanmıyor. Başarısız scoped context ile tekrar deneme yapılmıyor.

## Yeniden değerlendirme koşulları

- Duplicate trafiği ve exception maliyeti ölçülmüş darboğaz olursa targeted upsert.
- Batch veya çoklu aggregate save gelirse başarısız entity'yi güvenilir belirleme stratejisi.
- Transfer gibi daha geniş invariant'lar gelirse transaction/isolation/locking kararı.
- Aynı isteğin tekrarında aynı başarılı sonucu döndürme gereksinimi gelirse ayrı idempotency tasarımı.

## Doğrulama

Reproduce testi önce 201/500 ile geçti; 409 beklentisi çözüm olmadan başarısız oldu. Çözüm sonrası `dotnet test Ledgerly.slnx`: 28 passed (12 Domain, 2 Application, 14 IntegrationTests), 0 failed. Ortam ve test türlerinin ayrımı lab'da kayıtlıdır.
