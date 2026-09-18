# LAB-003 — Test Yatırması ve Kaybolan Bakiye Artışı

## Problem nedir?

İki işlem aynı eski bakiyeye göre yeni bakiye hesaplarsa, ikinci yazma ilk artışı silebilir. Buna lost update, yani kaybolan güncelleme denir.

```text
A okur: 0       B okur: 0
A hesaplar: 10  B hesaplar: 20
A yazar: 10     B yazar: 20
Son bakiye: 20; iki yatırma başarıyla işlenseydi 30 olmalıydı.
```

## Önceki davranışın kanıtı

Concurrency token eklenmeden önce iki ayrı DbContext ile yukarıdaki sıra gerçek `ledgerly_tests` PostgreSQL database'inde çalıştırıldı. İki context de 0 okudu, EF üzerinden 10 ve 20 yazdı. Test çıktısı: `Both requests read 0; deposits 10 + 20; persisted balance: 20,0000`.

Bu, henüz bulunmayan bir deposit endpoint'inin üretim hatası değildi; ekleyeceğimiz read-modify-write yönteminin kontrollü denemesiydi. Reproduce deterministiktir: iki okuma da ilk yazmadan önce biter. İşletim sisteminin thread zamanlamasına dayanmaz.

## Deneyi bugün nasıl koruyoruz?

`DepositConcurrencyTests.TwoStaleBalances_ShouldDemonstrateWhyOriginalBalanceMatters` iki case içerir:

- `useGuard=false`: Bilinçli bozuk kontrol örneği. Önceden hesaplanan bakiyeler `ExecuteUpdate` ile yalnızca ID koşuluyla yazılır. ExecuteUpdate tracked concurrency token'ını otomatik uygulamaz. Son bakiye 20; testin yeşil olması doğru finansal davranış değil, hatanın reproduce edilmesidir.
- `useGuard=true`: Aynı okumalar ve tutarlar; normal tracked SaveChanges yolu kullanılır. A'nın 10 yazması başarılıdır. B'nin eski 0 koşuluyla yazması reddedilir. Son bakiye 10; ikinci yatırma **başarılı sayılmaz**. Domain'e uygun conflict üretilir. 30 olması için ikinci yatırmanın yeni ve güvenli bir işlem olarak tamamlanması gerekir.

Guard'ın kabaca yaptığı SQL:

```sql
UPDATE wallets SET balance = 20
WHERE id = @walletId AND balance = 0;
```

Root cause tek başına transaction eksikliği değil; yazmanın dayandığı eski değerin kontrol edilmemesidir. [Alternatifler ve karar](../../adr/0007-test-deposit-and-balance-concurrency.md).

## Gerçek HTTP akışında neyi kontrol ediyoruz?

`TestDepositHttpTests` içindeki yarışlar iki handler'ı SaveChanges öncesindeki bariyerde bekletir. Böylece ikisi de okumalarını tamamlar, ardından ayrı DbContext'lerle birlikte kaydetmeye ilerler. Timeout 10 saniyedir; timeout olursa gate başarı sayılmaz, test başarısız olur.

Üç senaryo: mevcut hesaplarda aynı wallet bakiyesi; mevcut fon hesabıyla aynı wallet'a ilk ledger hesabının açılması; iki farklı wallet için ortak fon hesabının ilk kez açılması.

Her senaryoda tam 1 HTTP 200, 1 HTTP 409; denenen iki journal'dan yalnızca biri ve iki posting kalır. Wallet bakiyelerinin toplamı yalnızca başarılı receipt'in tutarına eşittir. İki farklı wallet senaryosunda kaybedenin yeni ledger hesabı da kalmaz.

Ayrı rollback testinde test decorator'ı ikinci posting'in account ID'sini olmayan bir ID yapar. Gerçek PostgreSQL foreign key bunu reddeder; HTTP 500 gelir. Yeni bağlantıda wallet 0, yeni hesaplar/journal/posting'ler yoktur. Hata test tarafından veriye enjekte edilir; PostgreSQL reddi gerçektir, network arızası simülasyonu değildir. DB komutlarının hangi sırada yürüdüğüne dair bu testten ayrıca bir iddia çıkarmıyoruz; daha önceki [journal atomicity lab'ı](../002-journal-atomicity/README.md) başarılı INSERT'lerden sonraki rollback'i ayrıca kanıtlar.

## Çalıştırma — macOS / zsh

```bash
docker compose up -d
dotnet test tests/Ledgerly.IntegrationTests/Ledgerly.IntegrationTests.csproj \
  --filter 'Lab=TestDeposit' \
  --logger 'console;verbosity=detailed'
```

## Sınır

Bu çalışma idempotency sağlamaz. Aynı başarılı HTTP isteğini iki kez yollamak iki ayrı yatırma yapar. Transfer, para çekme, overdraft ve bilinmeyen commit sonucu sonraki çalışmalardır.
