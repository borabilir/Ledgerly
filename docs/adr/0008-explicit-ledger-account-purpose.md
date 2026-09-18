# ADR-0008 — LedgerAccount Amacını Açıkça Modellemek

**Durum:** Kabul edildi — 2026-09-17

## Problem ve gözlem

`GetTestFundingAsync` isim olarak test fonunu arıyordu; modelde “test fonu” alanı bulunmuyordu. Sorgu `WalletId == null && Type == Asset && Currency == currency` idi. Mevcut iki rolle doğru sonuç veriyordu; ancak niyet veride ifade edilmiyordu. Bu çalışma mevcut bir yanlış finansal sonucu düzelttiğini iddia etmez; rolü dolaylı özelliklerden çıkarma bağımlılığını kaldırır.

## Alternatifler

| Seçenek | Kazanç | Bedel |
|---|---|---|
| Mevcut Type/WalletId çıkarımı | Ek sütun yok | TestFunding niyeti yalnızca kodun yorumunda kalır |
| IsTest boolean | Test / değil ayrımı kolay | “Test değil” hesabın kullanım amacını açıklamaz; ortam bilgisiyle karışır |
| LedgerAccountPurpose enum | Amaç açık, sorgu ve index aynı rolü kullanır | Bir sütun, migration ve Type/Purpose/WalletId tutarlılık kuralı gerekir |
| Ayrı hesap alt sınıfları | Her rolün farklı davranışı ayrı tutulabilir | Şimdiki iki rol için ek sınıflar ve mapping karmaşıklığı |

Purpose enum seçildi: Wallet ve TestFunding. Type korunur, çünkü muhasebe sınıfıyla işteki kullanım amacı farklı bilgilerdir. Yeni servis, ağ çağrısı veya transaction sınırı eklenmez. Performans artışı iddiası yok; mevcut currency unique index'inin filtresi açık role taşınır.

## Implementasyon

Domain factory'leri get-only Purpose'ı belirler. Infrastructure Record ve Application Snapshot alanı taşır. GetTestFundingAsync Purpose + Currency filtreler. Database zorunlu amaç, izin verilen birleşimler ve currency başına tek TestFunding hesabını korur. Unique index adı ve LedgerWriteConflictException eşlemesi korunur.

`20260917155805_AddLedgerAccountPurpose` migration'ı mevcut satırları önce eski şemanın geçerli birleşimleriyle doldurur, ardından NOT NULL/check/index uygular. EF'nin otomatik ürettiği `defaultValue: 0` yaklaşımı kullanılmadı; 0 anlamlı bir rol değildir. Yeni satırlar için de varsayılan amaç yoktur.

## Test ve sonuç

Mevcut factory ve persistence testleri amaç alanını doğrulayacak şekilde güncellendi. Dokuz yeni PostgreSQL case'i rol sorgusu, altı geçersiz birleşim, eksik amaç ve veri içeren önceki şemadan upgrade/down/upgrade akışını doğrular. Migration testi rastgele isimli ayrı bir schema kullanır; paylaşılan test şemasını eski sürüme indirmez. [Sonuç](../journey/08-ledger-account-purpose.md).

## Yeniden değerlendirme koşulu

Yeni bir platform hesabı gerektiğinde enum, factory ve database kuralları birlikte genişletilir. Şimdiden hayali roller eklenmez. TestFunding rolü, Production erişim kontrolü veya gerçek/test database izolasyonunun yerine geçmez.

Kaynak: [EF Core migrations ve veri dönüşümleri](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/operations).
