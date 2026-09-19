# Architecture Decision Records

## Kararlar

- [ADR-0001 — Baseline'a Modüler Tek Servisle Başlamak](0001-baseline-moduler-monolith.md)
- [ADR-0002 — Başlangıç Teknoloji Seçimleri](0002-baslangic-teknolojileri.md)
- [ADR-0003 — Concurrent Create Wallet İçin Dar Exception Translation](0003-concurrent-create-wallet-conflict.md)
- [ADR-0004 — Dengeli Journal Entry ve Posting Modeli](0004-journal-entry-and-postings.md)
- [ADR-0005 — LedgerAccount İçin Açık Oluşturma Metotları](0005-ledger-account-factories.md)
- [ADR-0006 — Ledger Persistence ve Tek Save Sınırı](0006-ledger-persistence-and-atomic-save.md)
- [ADR-0007 — Test Yatırması ve Bakiye Çakışması](0007-test-deposit-and-balance-concurrency.md)
- [ADR-0008 — LedgerAccount Amacını Açıkça Modellemek](0008-explicit-ledger-account-purpose.md)
- [ADR-0009 — Test Yatırmasında Kalıcı Idempotency Key](0009-test-deposit-idempotency.md)
- [ADR-0010 — Wallet Transferinde Optimistic Concurrency](0010-wallet-transfer-and-double-spending.md)
- [ADR-0011 — Transferde Kalıcı Idempotency Kaydı](0011-transfer-idempotency.md)
- [ADR-0012 — Transfer Eventleri İçin Transactional Outbox](0012-transactional-outbox.md)
- [ADR-0013 — Outbox Eventleri İçin RabbitMQ Publisher](0013-rabbitmq-publisher.md)

Mimari kararlar sıralı `ADR-NNN-kisa-baslik.md` dosyaları olarak kaydedilir. Yeni kayıtlar için [ADR şablonu](../templates/adr-template.md) kullanılır.
