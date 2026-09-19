# 11 — Transfer idempotency

## Başlangıç

Transfer double-spending'e karşı korunuyordu; fakat aynı başarılı isteğin cevap kaybı sonrası tekrar gönderilmesi iki ayrı finansal hareket sayılıyordu. Transferin kendi kalıcı kimliği de yoktu.

## Yapılanlar

- Korumasız sistemde aynı 100 TRY isteğinin iki journal oluşturup hedefi 200 TRY artırdığı gerçek HTTP testiyle gösterildi.
- `Idempotency-Key` transfer API'sinde zorunlu hâle getirildi.
- `TransferId`, request payload'ı ve sonuç makbuzunu saklayan `wallet_transfers` tablosu eklendi.
- Transfer kaydı, iki wallet değişikliği ve journal aynı transaction'a alındı.
- Unique source-wallet/key index'i ile paralel tekrarlar korundu.
- Aynı payload replay, farklı payload conflict, eksik key ve paralel retry test edildi.

[Problem ve testler](../labs/006-transfer-idempotency/README.md), [karar ve trade-off](../adr/0011-transfer-idempotency.md).

## Sonraki aday

Transfer artık kalıcı kimliğe sahip; fakat hâlâ senkron olarak tamamlanıyor. Sonraki senaryoda transfer durum makinesi, dış servis timeout'u veya transactional outbox ile “DB commit oldu ama event yayınlanamadı” problemi ele alınabilir.
