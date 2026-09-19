# ADR-0009 — Test yatırmasında kalıcı idempotency key

**Durum:** Kabul edildi — 2026-09-19

## Bağlam

Bir yatırma commit olup HTTP yanıtı kaybolduğunda istemci aynı işlemi yeniden gönderebilir. Mevcut optimistic concurrency yalnızca çakışan eski bakiye yazımlarını engeller; sıralı retry iki kez para yatırır. [LAB-004](../labs/004-test-deposit-idempotency/README.md) bu davranışı gerçek PostgreSQL üzerinde gösterdi.

## Karar

Development/IntegrationTests ortamındaki test yatırması için `Idempotency-Key` HTTP başlığı zorunlu. İstemci aynı mantıksal işlem için aynı key'i kullanır. `(wallet_id, key)` PostgreSQL primary key'dir. Amount ve ilk makbuz da bu satırda tutulur. Key kaydı, wallet bakiyesi ve ledger journal/posting'leriyle aynı `SaveChanges` transaction'ında yazılır.

Aynı key ve aynı amount ilk makbuzu `200` ile tekrar döner; aynı key farklı amount `409` verir. Key yoksa `400`; yeni key yeni yatırmadır. Key wallet kapsamında ve bu aşamada süresizdir.

## Neden bu seçenek?

Body'ye `OperationId` eklemek de mümkündü, fakat bu HTTP retry senaryosunda header daha küçük sözleşme değişikliğidir. Tutar üzerinden deduplication meşru aynı tutarlı iki işlemi karıştırır. Bellek içi cache restart ve çoklu instance'da kaybolur. PostgreSQL kaydı mevcut transaction ve unique constraint kapasitesini kullanır.

## Sonuçlar ve sınırlar

Her retry için bir DB okuması ve yeni işlem için ek bir satır yazımı vardır. Kayıtların süresiz tutulması depolama büyümesi yaratır; temizlik/retention politikası ileride işlem kimliği ve audit gereksinimleriyle birlikte seçilecek. Paralel aynı-key isteklerinde kaybeden transaction geri alınır ve kazananın makbuzu okunur. PostgreSQL'in ilgili deadlock kodu da kontrol edilen write conflict'e çevrilir; başka beklenmedik DB hataları replay olarak gizlenmez.

Kazananın kaydı henüz görünmüyorsa çakışan istek `409` alabilir; istemci aynı key ile tekrar dener. Bu karar gerçek banka entegrasyonunda “exactly once” garantisi iddia etmez. Dış sistemin kendi idempotency anahtarı, bilinmeyen commit sonucu ve mutabakat ayrı senaryolardır. Test endpoint'i Production'da kapalı kalır.
