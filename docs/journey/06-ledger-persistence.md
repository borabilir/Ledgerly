# 06 — Ledger Persistence ve Atomik Journal Kaydı

**Durum:** Tamamlandı; deposit/transfer API'si sırada.

**Tarih:** 2026-09-16

## Başlangıç ve problem

LedgerAccount, JournalEntry ve Posting yalnızca bellekte oluşturulabiliyordu; 67 test geçiyordu. İlk persistence testi repository sözleşmeleri henüz olmadığı için derlenemedi. Schema kurulduktan sonra kontrollü ayrı-save deneyi ikinci posting hatasında 1 journal/1 posting bıraktı. Tek-save yöntemi aynı hatada 0/0 bıraktı. Ayrıntılı kanıt [LAB-002](../labs/002-journal-atomicity/README.md).

## Değişiklik

Application'a ILedgerAccountRepository ve IJournalEntryRepository ile okuma snapshot'ları eklendi. Infrastructure'a üç satır modeli, mapping'leri ve iki repository eklendi. Domain modelinin immutable API'si değişmedi.

`20260916032929_AddLedgerPersistence` migration'ı üç ledger tablosunu, composite currency ilişkilerini, hesap unique/check constraint'lerini ve posting satır kontrollerini kurar. Wallet'ın (id, currency) alternate key'i ilişkiyi destekler. Migration test database'inde fixture tarafından, development ledgerly database'inde EF komutuyla uygulandı. Mevcut wallet verisi veya API sözleşmesi değişmedi; hesap backfill/seed yapılmadı.

## Test ve kanıt

23 yeni PostgreSQL case'i eklendi:

- 2 okuma/yazma: birden çok satır, aynı Posting instance'ının tekrarı, sıra/tutar/kimlik/zaman round-trip; tracking yokluğu; read-only sonuç; save öncesi görünmezlik ve commit sonrası yeni scope'ta görünürlük.
- 2 duplicate: wallet hesabı ve test fon hesabı tekilliği; ilgili hatalar yanlışlıkla WalletAlreadyExistsException'a çevrilmez.
- 17 constraint: olmayan wallet/account/journal, currency uyuşmazlıkları, yanlış tür/wallet kombinasyonları, tutar/yön/sıra kuralları, duplicate sıra ve referans verilen üst kayıtların silinmesinin reddi.
- 2 atomicity: bozuk ayrı save ile yarım kayıt ve tek Unit of Work ile gerçek rollback.

```text
Domain             46 passed
Application         4 passed
IntegrationTests   40 passed
Toplam             90 passed, 0 failed, 0 skipped
```

40 integration case içindeki, önceki milestone'lardan gelen 3 hata-enjeksiyon kontrolü gerçekte network arızası üretmez; yeni 23 case gerçek PostgreSQL kullanır. SDK 10.0.400. EF has-pending-model-changes kontrolü migration ile modelin eşleştiğini doğruladı.

Test geliştirilirken iki gözlem netleştirildi: currency testinde zaten hesabı olan wallet kullanmak önce unique ihlaline neden oluyordu; ilişkiyi izole etmek için hesabı olmayan wallet kullanıldı. ON DELETE RESTRICT ihlali PostgreSQL'de 23001 döndürdü; eksik referansın 23503 koduyla karıştırılmadı.

## Çalıştırma

```bash
docker compose up -d
dotnet tool restore
dotnet ef database update --project src/Ledgerly.Infrastructure/Ledgerly.Infrastructure.csproj --startup-project src/Ledgerly.Api/Ledgerly.Api.csproj -- --environment Development
dotnet test Ledgerly.slnx
```

## Sonraki adım

Test bakiyesi yatırma use-case'i: hesapları nasıl hazırlayacağımız, mevcut wallet'lara hesap açma davranışı, test fon hesabı ve wallet bakiyesiyle ledger'ın aynı transaction sınırında nasıl tutulacağı kararlaştırılacak. Şu an doğrudan repository üzerinden kalıcı kayıt mümkündür; yeni finansal HTTP endpoint'i veya otomatik bakiye değişimi yoktur.

**Takip:** Burada planlanan test yatırması [07. milestone](07-test-deposit.md) ile tamamlandı. Yukarıdaki sonuçlar persistence adımının tarihsel kaydıdır.
