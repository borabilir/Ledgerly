# ADR-0011 — Transferde kalıcı idempotency kaydı

**Durum:** Kabul edildi — 2026-09-19

## Bağlam

Bir transfer database'de commit olduğu hâlde HTTP cevabı kaybolabilir. İstemcinin aynı isteği tekrar göndermesi mevcut sistemde ikinci journal ve ikinci bakiye hareketi oluşturuyordu. Request body eşitliği tek başına tekrar anlamına gelmez; aynı tutarlı iki gerçek transfer mümkündür.

## Karar

`POST /api/transfers` için `Idempotency-Key` zorunludur. Key mevcut kimlik modelinde kaynak wallet kapsamında benzersizdir. Transfer, kendine ait server-generated `TransferId` taşıyan kalıcı `wallet_transfers` kaydıyla temsil edilir.

Transfer kaydı; istek kimliğini, kaynak/hedefi, tutarı, currency'yi, journal kimliğini ve döndürülen bakiye makbuzunu tutar. Bu kayıt wallet değişiklikleri ve journal ile aynı Unit of Work transaction'ında yazılır. Unique `(sourceWalletId, idempotencyKey)` index'i paralel ilk istek yarışını database seviyesinde kapatır.

Aynı key ve aynı payload ilk sonucu replay eder. Aynı key farklı hedef veya tutarla gelirse `409` döner. İkinci istek yarış nedeniyle save edemezse committed kayıt okunur; bulunursa replay edilir, bulunamazsa retry edilebilir bir conflict korunur.

## Alternatifler

Body hash'i gerçek tekrar ile aynı içerikli yeni transferi ayıramaz. Redis hızlıdır fakat finansal commit ile idempotency kaydını atomik tutmak için ek protokol ister. Genel idempotency tablosu ileride yararlı olabilir; test deposit ve transfer use-case'lerini bugün ortak payload/response şemasına zorlamak erken soyutlamadır.

## Sonuçlar

Transfer artık yalnızca journal kimliğiyle değil, kendi `TransferId` değeriyle izlenir. Bir tablo, migration ve repository eklendi. Key'ler şu an süresiz saklanır. Auth geldiğinde key scope'u, operasyonel ihtiyaçlar netleştiğinde retention politikası yeniden ele alınacaktır.

Deney ve kanıt: [LAB-006](../labs/006-transfer-idempotency/README.md).
