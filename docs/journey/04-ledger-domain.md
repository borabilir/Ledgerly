# 04 — Ledger Domain Modeli

> Sonraki adım (2026-09-16): [LedgerAccount domain modeli](05-ledger-account-domain.md) tamamlandı. Bu belge önceki milestone'u anlatır; hesap persistence'ı henüz yok.

**Durum:** Domain adımı tamamlandı; ledger persistence henüz yok.

**Tarih:** 2026-09-15

## Problem ve başlangıç

Get Wallet sonunda wallet oluşturma/okuma ve concurrency conflict çözümü hazırdı; toplam 33 test geçiyordu. Wallet.Balance vardı, ancak para hareketinin bütününü ve debit/credit dengesini temsil eden model yoktu. Bu eksikliğin nedeni yeni finansal use-case'lere henüz başlanmamış olmasıydı.

100 debit / 90 credit gibi dengesiz girdiyi reddetmek ve 100 debit / 60 + 40 credit gibi çok satırlı kaydı kabul etmek istedik. Alternatifler ve maliyetler [ADR-0004](../adr/0004-journal-entry-and-postings.md) içinde. Journal altında immutable posting'ler seçildi.

## Red kanıtı ve sınırı

Önce PostingTests ve JournalEntryTests yazıldı. Filtreli ilk çalıştırma CS0234/CS0246 hatalarıyla derlenemedi: Ledger namespace'i ve model tipleri henüz yoktu. Bu, eksik modele ait compile-time Red kanıtıdır; mevcut transfer API'sinde kayıp para reproduce edildiği veya assertion'ların o aşamada çalıştığı anlamına gelmez.

## Implementasyon

- `src/Ledgerly.Domain/Ledger/PostingDirection.cs`
- `src/Ledgerly.Domain/Ledger/Posting.cs`
- `src/Ledgerly.Domain/Ledger/JournalEntry.cs`

Pozitif ve dört basamakla kayıpsız temsil edilen tutarlar; en az iki farklı hesap; tam debit/credit eşitliği; UTC zaman; immutable satırlar ve koleksiyon kopyası eklendi. Kuralların gerekçesi [domain belgesinde](../domain/03-double-entry-ledger.md).

API, Application, Infrastructure ve migration'lara yeni ledger davranışı eklenmedi. Wallet bakiyesi güncellenmiyor; hesap referansının database'de varlığı bu modelde kontrol edilmiyor.

## Green ve regresyon kanıtı

macOS ARM64, SDK 10.0.400, runtime 10.0.11, PostgreSQL 18.6-alpine / ledgerly_tests ile tüm solution çalıştırıldı:

```text
Domain             41 passed
Application         4 passed
IntegrationTests   17 passed
Toplam             62 passed, 0 failed, 0 skipped
```

29 yeni domain case'i: valid debit/credit, boş hesap, tanımsız yön, sıfır/negatif/fazla hassas veya kapasite üstü tutar; desteklenen sınırlar; dengeli iki/çok satır; decimal kesir dengesi; küçük/büyük denge farkları; tek yön/tek hesap/eksik veya null satırlar; currency null; UTC; dış koleksiyon değişikliğinin journal'ı bozamaması.

Mevcut Create/Get Wallet ve concurrency 201/409 testleri geçti. IntegrationTests projesindeki 17 case'in 3'ü önceki exception injection kontrolleridir. Yeni ledger modeli için database garantisi test edilmedi; persistence henüz bulunmuyor.

## Tekrar çalıştırma

macOS / zsh / Bash, repository kökünde, yalnızca yeni domain testleri (Docker gerekmez):

```bash
dotnet test tests/Ledgerly.Domain.Tests/Ledgerly.Domain.Tests.csproj --filter 'FullyQualifiedName~Ledgerly.Domain.Tests.Ledger'
```

Bütün testler:

```bash
docker compose up -d
dotnet test Ledgerly.slnx
```

SDK PATH'te görünmüyorsa `dotnet` yerine `~/.dotnet/dotnet` kullanılabilir. Doğrulamada tam SDK yolu kullanıldı.

## Sonraki adım

LedgerAccount ve journal/posting persistence'ını tasarlamak, ardından test bakiyesi yatırma akışında tüm finansal satırları tek transaction'da kaydetmek. Kalıcı immutable ledger, idempotency, double-spending, reversal ve gerçek transfer henüz tamamlanmadı.
