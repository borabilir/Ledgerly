# ADR-0005 — LedgerAccount İçin Açık Oluşturma Metotları

**Durum:** Accepted

**Tarih:** 2026-09-16

## Bağlam

Dengeli posting listesi hazır, fakat AccountId'nin temsil ettiği hesap tanımlı değil. İlk test yatırma örneğinde wallet'a bağlı müşteri yükümlülüğü ve wallet'a bağlı olmayan simüle edilmiş fon varlığı gerekiyor.

## Alternatifler

| Seçenek | Fayda | Bedel |
|---|---|---|
| AccountId olarak WalletId kullanmak | Az kavram | Wallet'ı olmayan karşı hesabı ifade edemez; ürün ve muhasebe kimliklerini birleştirir |
| Ayrı wallet-account ve funding-account sınıfları | Roller tip seviyesinde ayrılır | Ortak alanlar ve ileride mapping tekrar eder |
| Tek sınıf, genel Create(type, walletId, ...) | Esnek | Geçersiz tür/ilişki kombinasyonlarına ek guard gerekir |
| Tek sınıf, iki anlamlı factory | Geçerli rol kombinasyonları açık | Yeni roller geldikçe metotlar veya model yeniden değerlendirilmeli |

## Karar

Tek LedgerAccount entity'si; CreateForWallet ve CreateTestFunding factory'leri. Type factory tarafından seçilir. LedgerAccountType yalnızca bugün kullanılan Asset/Liability değerlerini içerir. WalletId nullable'dır; yalnızca test fon hesabı oluşturulurken null olur. Public setter, genel constructor, generic entity/repository, hesap hiyerarşisi veya ayrı servis eklenmez.

## Sonuçlar ve sınırlar

Yanlış tür/wallet kombinasyonu public oluşturma yolunda kurulamaz. Domain mevcut Currency'yi kullanır; EF bağımlılığı yoktur. Tutar veya Balance alanı eklenmez. LedgerAccount.Id muhasebe kimliğidir, WalletId ilişki referansıdır.

Factory'ler database sorgulamaz: wallet'ın gerçekten varlığı, currency eşleşmesi, tekillik ve account'a ait posting'lerin saklanması garanti edilmez. Bunlar Application/Infrastructure aşamasında kurulacak. Bu adımın latency/throughput ölçümü yok; yeni operasyon bileşeni yok.

Yeni asset/liability rolleri, hesap kapatma davranışı veya bir wallet'ın birden çok ledger hesabına ihtiyacı doğarsa model yeniden değerlendirilecek. Kalıcı şema ve transaction sınırının kararı sonraki milestone'dur.
