# LedgerAccount: Hareket Hangi Hesaba Ait?

**Durum:** Domain modeli uygulandı. Hesap persistence'ı ve API akışına bağlantısı henüz yok.

**Tarih:** 2026-09-16

## Problem

Posting yalnızca AccountId, Direction ve Amount tutuyordu. Boş olmayan rastgele bir ID ile posting oluşturulabiliyor; hesabın neyi temsil ettiği modelde tanımlanmıyordu. Denge kuralı hesapların kimliğini veya varlığını doğrulamaz.

Bu adım hesabın kimliğini, türünü, currency'sini ve varsa wallet ilişkisini tanımlar. Rastgele AccountId'nin gerçek database kaydı olup olmadığını doğrulama problemi persistence adımına kalır; mevcut Posting.Create hâlâ yalnızca boş ID'yi reddeder.

## Test bakiyesi örneği

Platform açısından müşteri bakiyesi müşteriye olan borcu temsil eder. Başlangıçta iki hesap kullanacağız:

| Hesap | Tür | Wallet bağlantısı | 100 TRY test yatırma örneği |
|---|---|---|---|
| Test fon hesabı | Asset | Yok | Debit 100 |
| Müşteri wallet hesabı | Liability | WalletId | Credit 100 |

Asset debit ile, liability credit ile artar. Bu ayrım için [TigerBeetle Financial Accounting](https://docs.tigerbeetle.com/coding/financial-accounting/) kaynağına bakılabilir. Test fon hesabı simüle edilen dış fon varlığıdır; gerçek banka bakiyesi, doğrulanmış ödeme veya platform geliri değildir. Gerçek banka mutabakatı bu modelle yapılmış olmaz.

## Model

`LedgerAccount` kimliği olan bir entity'dir. Alanları yalnızca okunabilir:

- `Id`: Domain tarafından üretilen muhasebe hesabı kimliği.
- `WalletId`: Müşteri hesabında zorunlu, test fon hesabında null.
- `Type`: Şimdilik Asset veya Liability.
- `Currency`: Mevcut Currency value object'i; bugün yalnızca TRY.
- `CreatedAtUtc`: UTC'ye çevrilen oluşturulma zamanı.

WalletId ile Id ayrı kavramlardır. Posting.AccountId, LedgerAccount.Id'ye işaret etmek üzere kullanılır. Account üzerinde ikinci bir OwnerId saklamıyoruz; müşteri ilişkisi wallet üzerinden kurulacak. Balance alanı veya bakiye hesaplama davranışı eklemedik.

## Neden iki oluşturma metodu?

```csharp
var walletAccount = LedgerAccount.CreateForWallet(wallet.Id, wallet.Currency, now);
var fundingAccount = LedgerAccount.CreateTestFunding(wallet.Currency, now);
```

CreateForWallet boş WalletId ve null Currency'yi reddeder; Type'ı Liability olarak belirler. CreateTestFunding null Currency'yi reddeder; Type'ı Asset, WalletId'yi null yapar. Private constructor ve get-only alanlar normal public API üzerinden farklı kombinasyon kurulmasını veya sonradan türün değiştirilmesini önler.

Genel Create(type, walletId, ...) metodu caller'ın yanlış kombinasyon göndermesine izin verip ek validasyon gerektirecekti. İki ayrı sınıf ise bugün aynı alanları ve zamanı tekrar edecekti. Tek sınıf ve anlamlı factory metotları seçildi. Bu, bütün asset hesaplarının sonsuza kadar test fonu olacağı anlamına gelmez; yeni hesap rolleri ortaya çıktığında amaç/tür ayrımı yeniden değerlendirilecek.

## Posting ile bir araya gelmesi

Aşağıdaki domain örneği nesneleri bellekte oluşturur; para yatırmaz veya database'e yazmaz:

```csharp
var walletAccount = LedgerAccount.CreateForWallet(wallet.Id, wallet.Currency, now);
var fundingAccount = LedgerAccount.CreateTestFunding(wallet.Currency, now);

var journal = JournalEntry.Create(wallet.Currency,
    [
        Posting.Create(fundingAccount.Id, PostingDirection.Debit, 100m),
        Posting.Create(walletAccount.Id, PostingDirection.Credit, 100m),
    ], now);
```

JournalEntry hesapları yüklemez veya Type'a göre bakiye hesaplamaz. İki hesabın currency uyumunu bu örnekte aynı Currency'yi vererek sağlıyoruz; bunun use-case veya database garantisi henüz yok.

## Bu adımın garantisi ve sınırı

Domain doğru başlangıç şeklini korur. WalletId'nin database'de varlığı, aynı wallet için tek hesap, currency başına tek test fon hesabı, wallet/account currency eşleşmesi ve posting/account foreign key'i henüz korunmaz. Aynı factory'yi iki kez çağırmak iki yeni hesap nesnesi oluşturur; bu metotlar find-or-create veya idempotent değildir. Mevcut POST /api/wallets otomatik ledger hesabı açmaz.

İlk kalıcı modelde wallet başına bir müşteri hesabı ve currency başına bir test fon hesabı hedefliyoruz. Bu hedefi constraint ve integration testleriyle ayrıca doğrulayacağız. Journal ile satırlarının atomik kaydı, negatif bakiye kontrolü ve event sourcing hâlâ gelecek konulardır.

Test kanıtı: [05 — LedgerAccount Domain](../journey/05-ledger-account-domain.md). Karar: [ADR-0005](../adr/0005-ledger-account-factories.md).

## Sonraki milestone

16 Eylül 2026: [Ledger persistence](../architecture/03-ledger-persistence.md) ile hesap ve journal tabloları, referans/currency/tekillik kuralları eklendi. Yukarıdaki “henüz” ifadeleri domain milestone sınırını anlatır. Otomatik hesap provisioning ve finansal endpoint hâlâ yok.
