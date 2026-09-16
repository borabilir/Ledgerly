# 05 — LedgerAccount Domain Modeli

**Durum:** Hesap domain modeli tamamlandı; persistence sırada.

**Tarih:** 2026-09-16

## Problem, kanıt ve karar

JournalEntry ile debit/credit dengesi korunuyordu, fakat hesabı tanımlayan bir domain tipi yoktu. AccountId boş olmayan herhangi bir GUID olabiliyordu. Root cause, bu aşamaya kadar sadece hareket satırı ve bütününün modellenmiş olmasıydı; çalışan deposit API'sinde bir arıza reproduce edilmedi.

Önce LedgerAccountTests yazıldı. İlk koşu CS0103 ile derlenemedi: LedgerAccount ve LedgerAccountType henüz yoktu. Bu compile-time Red kanıtıdır; başarısız bir runtime assertion deneyi değildir. [ADR-0005](../adr/0005-ledger-account-factories.md) alternatiflerini değerlendirip tek entity ve iki factory seçtik.

## Implementasyon

- CreateForWallet: dolu WalletId, Liability türü, Currency ve UTC zaman.
- CreateTestFunding: WalletId null, Asset türü, Currency ve UTC zaman.
- Her hesap yeni, bağımsız Id alır. Alanları get-only'dir.
- Boş WalletId ve null Currency reddedilir.

Kod: `src/Ledgerly.Domain/Ledger/LedgerAccount.cs`, `LedgerAccountType.cs`.

Yeni Application handler'ı, repository, migration veya endpoint eklenmedi. Mevcut Create Wallet otomatik hesap oluşturmaz; eski wallet'lar için backfill bu aşamada yapılmaz. JournalEntry/Posting'in hesap varlığını doğrulama davranışı değişmedi.

## Test

2026-09-16, SDK 10.0.400 ile tüm solution çalıştırıldı. Mevcut integration testleri gerçek ledgerly_tests PostgreSQL database'ini kullandı:

```text
Domain             46 passed
Application         4 passed
IntegrationTests   17 passed
Toplam             67 passed, 0 failed, 0 skipped
```

5 yeni domain testi: geçerli wallet hesabının kimliği/ilişkisi/türü/currency'si/UTC zamanı; boş WalletId reddi; wallet hesabında null Currency reddi; geçerli fon hesabının wallet'sız Asset olması ve UTC zamanı; fon hesabında null Currency reddi. Hesapların kalıcılığı veya database constraint'leri henüz test edilmiş değildir. Önceki 17 integration case'in 3'ü exception injection kontrolleridir.

Yalnızca yeni testler (Docker gerekmez):

```bash
dotnet test tests/Ledgerly.Domain.Tests/Ledgerly.Domain.Tests.csproj --filter 'FullyQualifiedName~LedgerAccountTests'
```

Tüm testler:

```bash
docker compose up -d
dotnet test Ledgerly.slnx
```

SDK PATH'te görünmüyorsa `~/.dotnet/dotnet` kullanılabilir; doğrulama tam SDK yoluyla yapıldı.

## Sonraki problem

Hesapları ve journal/posting'leri PostgreSQL'e bağlamak: wallet ve hesap referanslarının varlığı, currency uyumu ve hesap tekilliği. Ardından bir posting kaydı başarısız olduğunda journal'ın tamamının rollback olması gerektiğini gerçek database testiyle göstermek. Test bakiyesi yatırma use-case'i bunun üzerine kurulacak.
