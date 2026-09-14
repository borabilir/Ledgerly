# Test Stratejisi ve TDD Yaklaşımı

**Durum:** Accepted
**Tarih:** 2026-09-03
**Son güncelleme:** 2026-09-13

## Amaç

Ledgerly'de testler geliştirme bittikten sonra eklenen bir kontrol listesi değildir. Özellikle finansal domain kuralları için testler, beklenen davranışı tanımlayan executable dokümantasyondur.

Başarı yalnızca yüksek code coverage yüzdesi değildir. Öncelik; kritik invariant'ların, hata senaryolarının ve sistem sınırlarının doğru test türüyle doğrulanmasıdır.

## Genel geliştirme akışı

Bir vertical slice mümkün olduğunca şu sırayla ilerler:

```text
Problem veya use-case
  -> Beklenen davranış ve acceptance criteria
  -> Domain testi: Red
  -> Minimum implementasyon: Green
  -> Refactor
  -> Application orchestration
  -> Persistence ve dış adaptörler
  -> Integration/API testleri
  -> Dokümantasyon
```

TDD her satır için zorunlu değildir. Saf domain davranışlarında test-first; veritabanı, HTTP ve broker gibi sınırlarında ise gerçek bileşenle integration test ağırlıklı yaklaşım kullanılır.

## Red, Green, Refactor

### Red

Henüz desteklenmeyen davranış testle ifade edilir ve testin doğru nedenle başarısız olduğu görülür. Compile failure da ilk döngüde Red kabul edilebilir.

### Green

Testi geçirecek minimum üretim kodu yazılır. Bu aşamada gelecekte gerekebilir düşüncesiyle yeni abstraction veya özellik eklenmez.

### Refactor

Tüm testler yeşil kalırken isimler, tekrarlar ve yapı iyileştirilir. Refactor yeni davranış eklemez.

## İlk örnek: Create Wallet

İlk davranış testi geçerli değerlerle oluşturulan wallet'ın doğru başlangıç durumunu tanımladı:

```csharp
[Fact]
public void Create_WhenValuesAreValid_ShouldCreateActiveWalletWithZeroBalance()
{
    var ownerId = Guid.NewGuid();
    var createdAtUtc = new DateTimeOffset(
        2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    var currency = Currency.FromCode("TRY");
    var wallet = Wallet.Create(ownerId, currency, createdAtUtc);

    Assert.NotEqual(Guid.Empty, wallet.Id);
    Assert.Equal(ownerId, wallet.OwnerId);
    Assert.Equal(currency, wallet.Currency);
    Assert.Equal(WalletStatus.Active, wallet.Status);
    Assert.Equal(0m, wallet.Balance);
    Assert.Equal(createdAtUtc, wallet.CreatedAtUtc);
}
```

İkinci test şu invariant'ı executable hale getirdi:

> Owner'ı olmayan bir wallet oluşturulamaz.

```csharp
[Fact]
public void Create_WhenOwnerIdIsEmpty_ShouldThrowArgumentException()
{
    var createdAtUtc = new DateTimeOffset(
        2026, 9, 3, 12, 0, 0, TimeSpan.Zero);
    var currency = Currency.FromCode("TRY");

    var exception = Assert.Throws<ArgumentException>(() =>
        Wallet.Create(Guid.Empty, currency, createdAtUtc));

    Assert.Equal("ownerId", exception.ParamName);
}
```

Test önce `No exception was thrown` mesajıyla Red oldu. `Wallet.Create` içine owner ID guard clause'u eklendikten sonra Green oldu. Bu sıra, testin gerçekten eksik davranışı yakaladığını kanıtladı.

## xUnit kavramları

### Fact

`[Fact]`, parametre almayan tek bir senaryoyu xUnit test runner'a test olarak işaretler.

```csharp
[Fact]
public void Create_WhenOwnerIdIsEmpty_ShouldThrowArgumentException()
```

### Theory

`[Theory]`, aynı davranışı birden fazla veriyle doğrular. Blank currency kontrolünde kullanılabilir:

```csharp
[Theory]
[InlineData("")]
[InlineData(" ")]
[InlineData("   ")]
public void FromCode_WhenCodeIsBlank_ShouldThrowArgumentException(
    string code)
{
    Assert.Throws<ArgumentException>(() => Currency.FromCode(code));
}
```

### Assert

`Assert`, gerçek sonucu beklenen değerle karşılaştırır. Assertion başarısızsa test başarısız olur.

```csharp
Assert.Equal(expected, actual);
Assert.NotEqual(unexpected, actual);
Assert.True(condition);
Assert.Throws<ArgumentException>(() => operation());
```

`Assert.Throws`, operasyonu lambda üzerinden kendisi çalıştırır; böylece atılan exception'ı yakalayıp tipini doğrulayabilir.

## Arrange, Act, Assert

Testler mümkün olduğunca üç bölüme ayrılır:

```text
Arrange -> Test verisini ve bağımlılıkları hazırla
Act     -> Test edilen tek davranışı çalıştır
Assert  -> Gözlemlenebilir sonucu doğrula
```

Exception testlerinde Act ve Assert, `Assert.Throws` nedeniyle aynı ifadede birleşebilir.

## Test isimlendirme convention'ı

Başlangıç formatı:

```text
Method_WhenCondition_ShouldExpectedResult
```

Örnek:

```text
Create_WhenOwnerIdIsEmpty_ShouldThrowArgumentException
```

Bu format framework zorunluluğu değildir. Ama test çıktısında hangi davranışın hangi koşulda bozulduğunu açıklayan yaşayan bir cümle üretir. Tutarlılık, belirli bir isimlendirme ekolünden daha önemlidir.

## Katmanlara göre test yaklaşımı

| Sınır | Baskın test türü | Ne doğrulanır? |
|---|---|---|
| Domain | Unit test, çoğunlukla TDD | Invariant, state transition, para hesapları |
| Application | Unit veya küçük component test | Use-case orchestration, port çağrıları, sonuçlar |
| Infrastructure | Integration test | EF mapping, transaction, constraint, gerçek PostgreSQL davranışı |
| API | Integration/functional test | HTTP contract, status code, validation, serialization |
| Messaging | Integration ve scenario test | Outbox, duplicate delivery, ordering, consumer idempotency |
| Performans/arıza | Lab, load ve chaos test | Darboğaz, timeout, recovery ve ölçek davranışı |

Her sınıfa unit test yazmak hedef değildir. Örneğin EF Core mapping'i mock'larla taklit etmek yerine gerçek PostgreSQL ile integration test etmek daha değerlidir.

## Test kalitesi kuralları

- Test davranışı doğrular; private implementasyon detayına bağlanmaz.
- Test deterministik ve birbirinden bağımsız olmalıdır.
- Tek bir başarısızlık nedeni kolayca okunabilmelidir.
- Boş veya assertion içermeyen placeholder testler başarı kanıtı sayılmaz ve silinir.
- Kritik hata yolları yalnızca happy path kadar önemlidir.
- Coverage metriği kör hedef değildir; korunması gereken risklerin test edilip edilmediğini sorgulamak için kullanılır.
- Bir bug düzeltildiğinde önce bug'ı reproduce eden test eklenir.

## Test çalıştırma komutları

Bütün domain testleri:

```powershell
dotnet test tests/Ledgerly.Domain.Tests/Ledgerly.Domain.Tests.csproj
```

Bir test sınıfı:

```powershell
dotnet test tests/Ledgerly.Domain.Tests/Ledgerly.Domain.Tests.csproj --filter "FullyQualifiedName~Ledgerly.Domain.Tests.Wallets.WalletCreateTests"
```

Tek test:

```powershell
dotnet test tests/Ledgerly.Domain.Tests/Ledgerly.Domain.Tests.csproj --filter "FullyQualifiedName~Create_WhenOwnerIdIsEmpty_ShouldThrowArgumentException"
```

Test adlarını listeleme:

```powershell
dotnet test tests/Ledgerly.Domain.Tests/Ledgerly.Domain.Tests.csproj --list-tests
```

Application testleri:

```powershell
dotnet test tests/Ledgerly.Application.Tests/Ledgerly.Application.Tests.csproj
```

Docker'daki gerçek PostgreSQL'i kullanan integration testleri:

```powershell
docker compose up -d

dotnet test tests/Ledgerly.IntegrationTests/Ledgerly.IntegrationTests.csproj `
  --filter "Category=Integration"
```

Test bağlantısı gerektiğinde environment variable ile değiştirilebilir:

```powershell
$env:LEDGERLY_TEST_DB_CONNECTION_STRING = "Host=localhost;Port=5432;Database=ledgerly_tests;Username=ledgerly;Password=ledgerly_dev"
```

## Mevcut durum

- Wallet oluşturma, owner ve currency kuralları ile UTC normalizasyonu 12 domain test case'iyle doğrulandı.
- Create Wallet happy-path ve duplicate orchestration davranışları 2 Application testiyle doğrulandı.
- Create Wallet persistence akışı gerçek PostgreSQL kullanan 2 integration testiyle doğrulandı.
- Domain, Application ve Integration test projelerindeki boş `UnitTest1.cs` template testleri kaldırıldı.
- Integration testleri migration'ı uygular ve her testin verisini transaction rollback ile temizler.
- Integration testleri varsayılan olarak development DB'den ayrı `ledgerly_tests` database'ini kullanır.
- Unique constraint'in eşzamanlı istek yarışındaki davranışı ileride ayrı bir concurrency lab'ında reproduce edilecektir.
