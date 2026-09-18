# Ledgerly Dokümantasyon Rehberi

Bu dizin yalnızca sistemin son hâlini değil, o hâle neden ve nasıl ulaşıldığını kaydeder. Kod ile birlikte version control altında tutulacak belgeler Ledgerly hakkındaki kanonik bilgi kaynağıdır.

## Dizinler

| Dizin | Amaç |
|---|---|
| `journey/` | Projenin kronolojik gelişimi ve milestone kayıtları |
| `domain/` | Ubiquitous language, bounded context ve business invariant'ları |
| `architecture/` | C4, sequence, veri akışı ve topoloji belgeleri |
| `adr/` | Mimari kararlar, alternatifler ve trade-off'lar |
| `labs/` | Tekrar çalıştırılabilir problem ve failure deneyleri |
| `runbooks/` | Production olaylarında uygulanacak operasyon adımları |
| `postmortems/` | Gerçek veya simüle edilmiş incident incelemeleri |
| `interview/` | Lab ve kararlardan çıkarılan kısa mülakat anlatımları |
| `templates/` | Yeni kayıtlar için standart şablonlar |

## İlk kayıt

[00 — Project Charter](00-project-charter.md), projenin amacını, kapsamını, öğrenme yöntemini ve başlangıç sınırlarını tanımlar.

## Başlangıç kararları

- [Servis Sınırları ve Bounded Context Adayları](domain/01-servis-sinirlari.md)
- [ADR-0001 — Baseline'a Modüler Tek Servisle Başlamak](adr/0001-baseline-moduler-monolith.md)
- [ADR-0002 — Başlangıç Teknoloji Seçimleri](adr/0002-baslangic-teknolojileri.md)
- [00 — Solution Bootstrap](journey/00-bootstrap.md)
- [01 — PostgreSQL Persistence Baseline](journey/01-postgresql-persistence.md)
- [02 — Create Wallet HTTP API](journey/02-create-wallet-http-api.md)
- [03 — Get Wallet Query ve Kaynak Adresi](journey/03-get-wallet-query.md)
- [LAB-001 — Eşzamanlı Create Wallet Yarışı](labs/001-concurrent-create-wallet/README.md)
- [ADR-0003 — Concurrent Create Wallet İçin Dar Exception Translation](adr/0003-concurrent-create-wallet-conflict.md)

## Kayıt kuralları

- Root cause kanıtsız tahmin olarak yazılmaz; test, trace, metric, execution plan veya veri kaydıyla desteklenir.
- Başarısız deneyler silinmez. Beklenti ile gerçek sonuç arasındaki fark kaydedilir.
- Bir pattern yalnızca çözdüğü problem gösterildikten sonra ana sisteme eklenir.
- Her lab aynı reproduce adımını değişiklikten önce ve sonra çalıştırır.
- Mimari karar değiştiğinde eski ADR silinmez; `Superseded` olarak işaretlenir.
- Büyük veya ham sonuçlar yerine tekrar üretme komutları ve anlamlı özet metrikler saklanır.
- Interview OS içindeki Ledgerly belgesi bu kayıtların mülakat odaklı özetidir; kanonik kaynak değildir.

## Finansal domain başlangıcı

- [Double-entry Ledger Domain Modeli](domain/03-double-entry-ledger.md)
- [ADR-0004 — Journal Entry ve Posting](adr/0004-journal-entry-and-postings.md)
- [04 — Ledger Domain Milestone](journey/04-ledger-domain.md)

- [LedgerAccount Domain Modeli](domain/04-ledger-account.md)
- [ADR-0005 — LedgerAccount Factory Kararı](adr/0005-ledger-account-factories.md)
- [05 — LedgerAccount Milestone](journey/05-ledger-account-domain.md)

## Ledger persistence

- [Kayıt modeli ve database kuralları](architecture/03-ledger-persistence.md)
- [LAB-002 — Journal Atomicity](labs/002-journal-atomicity/README.md)
- [06 — Ledger Persistence Milestone](journey/06-ledger-persistence.md)

## Test bakiyesi yatırma

- [07 — Test yatırması](journey/07-test-deposit.md)
- [LAB-003 — Bakiye yarışı](labs/003-test-deposit/README.md)
- [ADR-0007 — İlk kullanımda hesap açma ve concurrency](adr/0007-test-deposit-and-balance-concurrency.md)

## Hesabın amacı

- [Type ve Purpose ayrımı](domain/05-ledger-account-purpose.md)
- [ADR-0008 — Açık hesap amacı](adr/0008-explicit-ledger-account-purpose.md)
- [08 — Purpose migration ve testleri](journey/08-ledger-account-purpose.md)
