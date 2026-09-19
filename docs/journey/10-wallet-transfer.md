# 10 — Wallet-to-wallet transfer ve double-spending

## Başlangıç

Wallet'a yalnızca test bakiyesi ekleyebiliyorduk; bakiye azaltan ve iki wallet'ı birlikte değiştiren use-case yoktu. Bu nedenle “aynı para iki kez harcanırsa ne olur?” sorusunu gerçek transfer akışında henüz cevaplamıyorduk.

## Yapılanlar

- `Wallet.Debit`, geçerli tutar ve yeterli bakiye invariant'ını ekledi.
- `TransferWalletHandler`, kaynak ve hedef wallet'ı, ledger hesaplarını ve dengeli journal'ı tek Unit of Work sınırında birleştirdi.
- `POST /api/transfers` normal transfer, 400, 404, 409 ve 500 sözleşmesiyle eklendi.
- Guard atlanan PostgreSQL deneyi aynı 100 TRY'nin iki kez harcanmasıyla toplam bakiyenin 180'e çıktığını reproduce etti.
- Gerçek HTTP yarışında iki ayrı 80 TRY transferinden yalnızca biri commit edildi; kaybedenin hedef artışı, hesabı ve journal'ı rollback oldu.

[Problem, reproduce ve test](../labs/005-wallet-transfer-double-spending/README.md), [mimari karar ve trade-off](../adr/0010-wallet-transfer-and-double-spending.md).

## Sınır ve sonraki adım

Şimdilik yalnızca TRY vardır ve transfer journal makbuzuyla temsil edilir. Kalıcı transfer durumu ve transfer retry idempotency'si yoktur. Sonraki deney aynı mantıksal transfer isteğinin bağlantı hatası sonrası yeniden gönderilmesini ele almalıdır.
