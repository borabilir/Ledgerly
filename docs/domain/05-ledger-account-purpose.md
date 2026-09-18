# Hesabın Türü ve Amacı: Type ile Purpose

## Purpose nedir?

Purpose, bir hesabı ne için açtığımızı söyler. Type ise hesabın muhasebe türüdür. Bu iki soru aynı değildir.

| Hesap | Type: muhasebe türü | Purpose: kullanım amacı | WalletId |
|---|---|---|---|
| Müşteri cüzdanının hesabı | Liability | Wallet | İlgili wallet'ın ID'si |
| Test fon hesabı | Asset | TestFunding | null |

Asset demek tek başına “test fonu” demek değildir. Önceki modelde yalnızca iki hesap rolü bulunduğu için wallet'a bağlı olmayan Asset'i test fonu olarak yorumluyorduk. Artık bu amacı açıkça kaydediyoruz.

## Nasıl oluşturulur?

`LedgerAccountPurpose` iki değer içerir: Wallet = 1, TestFunding = 2. Factory metotları doğru birleşimi kendileri seçer:

```csharp
LedgerAccount.CreateForWallet(walletId, currency, now);
// Type = Liability, Purpose = Wallet

LedgerAccount.CreateTestFunding(currency, now);
// Type = Asset, Purpose = TestFunding
```

Kullanıcıdan serbest Type/Purpose birleşimi alınmaz. Constructor private, Purpose get-only'dir. Purpose müşterinin veya wallet'ın test ortamında bulunduğunu gösteren bir güvenlik izni değildir; muhasebe hesabının rolüdür.

## Repository neyi arıyor?

```csharp
account.Purpose == LedgerAccountPurpose.TestFunding
    && account.Currency == currency
```

`GetTestFundingAsync` artık başka alanlardan rol çıkarmıyor. `LedgerAccountSnapshot` da Purpose bilgisini taşır; okunan hesabın amacı görülebilir.

## Database kuralları

- Purpose zorunludur ve varsayılan değeri yoktur. Unutulan alan sessizce TestFunding yapılmaz.
- `ck_ledger_accounts_purpose_type_wallet`: Wallet amacı yalnızca Liability + dolu WalletId; TestFunding amacı yalnızca Asset + boş WalletId ile geçerlidir. Tanımsız enum değerleri de reddedilir.
- Wallet başına en fazla bir hesap kuralı devam eder.
- `ux_ledger_accounts_test_funding_currency` artık `purpose = 2` filtresiyle çalışır. Currency başına yalnızca bir TestFunding hesabı bulunabilir. Index adı değişmediği için mevcut dar conflict çevirisi çalışmaya devam eder.

## Mevcut veriler nasıl taşındı?

Yeni purpose sütunu önce nullable eklenir. Eski şemanın izin verdiği iki birleşimden anlamı hesaplanır: wallet'a bağlı Liability → Wallet; wallet'a bağlı olmayan Asset → TestFunding. Sonra sütun zorunlu olur ve yeni check/index kuralları eklenir.

Bu eşleme yalnızca önceki şemanın iki rolüyle sınırlıdır; “her Asset her zaman test hesabıdır” kuralı değildir. Yeni hesap ID'leri oluşturulmaz; mevcut wallet, account, journal ve posting bağlantıları korunur.

## Sınır

Şu an gerçek banka veya başka bir platform hesabı rolü eklemedik. İleride yeni rol gerektiğinde enum, factory ve database check kuralı birlikte genişletilecek. Purpose eklenmesi gerçek banka entegrasyonu, test wallet sınıflandırması veya idempotency eklemez.

[Karar ve alternatifler](../adr/0008-explicit-ledger-account-purpose.md), [test ve migration kanıtı](../journey/08-ledger-account-purpose.md).
