# LAB-002 — Journal'ın Yarım Kaydedilmesi

**Durum:** Reproduction ve tek-save çözümü doğrulandı.

**Tarih:** 2026-09-16

## Problem

Bir journal başlığı, Debit 10 ve Credit 10 satırından oluşsun. İkinci satır var olmayan hesaba işaret etsin. İlk satır kaydedilmiş olsa bile sonuçta bu journal'ın hiçbir satırının kalmamasını istiyoruz.

Bu lab, önceden production'da çalışan bir deposit hatasının kaydı değildir. Yeni ledger persistence sınırında iki kayıt stratejisini kontrollü karşılaştırır. Bilinçli bozuk yöntem yalnızca test kodundadır.

## Testler ve tekrar çalıştırma

`tests/Ledgerly.IntegrationTests/Ledger/JournalAtomicityTests.cs`:

- SeparateSaves_WhenSecondPostingFails_ShouldReproducePartialJournal
- SingleUnitOfWork_WhenSecondPostingFails_ShouldRollBackEntireJournal

macOS/zsh/Bash ve PowerShell için tek satırlık komut:

```bash
docker compose up -d
dotnet test tests/Ledgerly.IntegrationTests/Ledgerly.IntegrationTests.csproj --filter 'Lab=JournalAtomicity' --logger 'console;verbosity=detailed'
```

SDK PATH'te değilse dotnet yerine ~/.dotnet/dotnet kullanılabilir. PostgreSQL ledgerly_tests database'i ve migration'lar mevcut fixture ile hazırlanır.

## Deney 1: Ayrı save'ler

```text
Journal başlığı INSERT → SaveChanges → commit
Debit 10 INSERT        → SaveChanges → commit
Credit 10 INSERT       → SaveChanges → olmayan hesap, hata
```

Gözlenen kanıt:

```text
Mode: separate saves
Successful INSERT commands: journal_entries, postings
SQLSTATE: 23503; constraint: fk_postings_account_currency
Fresh connection: journals=1, postings=1
```

Root cause: her save kendi kayıt sınırını bitirmişti. Üçüncü save'in hatası ilk iki commit'i geri alamaz. Dengesiz kaydın varlığı factory kontrolüyle tek başına önlenemedi; bozuk writer modeli parçalayarak kaydetti.

Bu testin geçmesi davranışın doğru olduğunu söylemez. Yanlış yöntemin sonucunun deterministik gösterildiğini söyler.

## Deney 2: Tek Unit of Work

```text
Repository: journal ve iki posting'i hazırla
Tek SaveChanges:
  journal INSERT → başarılı
  ilk posting INSERT → başarılı
  ikinci posting INSERT → 23503
  transaction rollback
```

Gözlenen kanıt:

```text
Mode: single unit of work
Successful INSERT commands: journal_entries, postings
SQLSTATE: 23503; constraint: fk_postings_account_currency
Fresh connection: journals=0, postings=0
```

## Kanıtı nasıl ayırıyoruz?

Test writer context'inde MaxBatchSize(1), komutları ayrı gönderir. DbCommandInterceptor başarılı INSERT komutlarını gözler. Bu sayede “hiçbir insert denenmedi” durumu rollback kanıtı sayılmaz. Uygulamanın normal batching ayarı değiştirilmez.

Testin etrafında outer transaction yoktur. Seed wallet/account önce commit edilir. Sonuç yeni DbContext/bağlantı üzerinden okunur; sayımlar cleanup'tan önce yapılır. Önceden kaydedilen hesap hâlâ vardır, wallet bakiyesi sıfırdır. Test yalnızca kendi GUID'leriyle oluşturduğu kayıtları finally'de ilişkilerin ters sırasıyla temizler.

## Karar ve alternatif

Birden fazla save'i explicit transaction'a almak da geçerli çözümdür. Şu an bütün graph önceden bilindiği için aynı scoped DbContext + tek SaveChanges daha küçük çözümdür. [ADR-0006](../../adr/0006-ledger-persistence-and-atomic-save.md).

## Sınırlar

Bu test bakiye harcama, gerçek transfer, eşzamanlı double-spending, ağ kopması veya commit sonucunun belirsiz kalmasını kapsamaz. Database'e doğrudan SQL yazan kullanıcı dengesiz satırlar oluşturabilir veya posting silebilir. Cross-row denge ve kalıcı append-only koruma henüz yoktur.

16 Eylül doğrulamasında iki lab testi geçti. Tüm solution sonucu 46 Domain + 4 Application + 40 IntegrationTests = 90 passed, 0 failed, 0 skipped.
