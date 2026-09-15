# DDD ve Create Wallet Domain Modeli

**Durum:** Uygulandı
**Tarih:** 2026-09-13

> Bu belge Create Wallet milestone'unun tarihsel anlatımıdır. Wallet persistence ve concurrency çözümü sonraki adımlarda tamamlandı. 2026-09-15'te [JournalEntry/Posting domain modeli](03-double-entry-ledger.md) eklendi; toplam domain test sayısı 41 oldu. Aşağıdaki 12 test ve “henüz” listeleri ilk milestone'a aittir.

## Amaç

Bu belge Domain-Driven Design yaklaşımının Ledgerly'de şu ana kadar nasıl kullanıldığını, hangi kararların gerçekten kodda bulunduğunu ve hangi yapıların henüz yalnızca plan olduğunu kaydeder.

DDD tek başına bir klasör yapısı veya framework değildir. Temel amaç, iş alanındaki kavramları ve kuralları kodun merkezine yerleştirmektir. Ledgerly'de bunu iki seviyede ele alıyoruz:

- Stratejik DDD: bounded context ve servis sınırlarını keşfetmek.
- Taktik DDD: entity, value object, aggregate ve invariant gibi yapı taşlarıyla domain modelini kurmak.

## Şu anki sınır

Ledgerly henüz mikroservislere ayrılmış değildir. İlk deployable, Wallet Core modülünü barındıran bir modüler monolith'tir. Wallet Core başlangıçta wallet oluşturma davranışına sahiptir; ledger, transfer ve transaction history modelleri henüz uygulanmamıştır.

```text
Wallet Core
└── Wallet aggregate
    ├── Wallet entity / aggregate root
    ├── Currency value object
    └── WalletStatus enum
```

## Uygulanan taktik DDD yapı taşları

| Kavram | Ledgerly karşılığı | Kodda nasıl görünür? |
|---|---|---|
| Entity | `Wallet` | Değişmeyen bir `Id` ile tanımlanır |
| Aggregate Root | `Wallet` | Oluşturma kurallarına tek giriş noktasıdır |
| Value Object | `Currency` | Kimliği yoktur, değer bazlı eşitlik kullanır |
| Factory Method | `Wallet.Create` | Geçerli başlangıç durumunu tek yerde kurar |
| Invariant | Geçerli owner, currency ve başlangıç durumu | Private constructor, private setter ve guard clause'larla korunur |
| Ubiquitous Language | Wallet, owner, currency, balance | Kod ve dokümanlarda aynı terimler kullanılır |

## Wallet neden entity?

İki wallet aynı owner, currency ve bakiyeye sahip olsa bile farklı kimliklere sahip ayrı nesnelerdir. Bu nedenle eşitliği yalnızca alan değerleriyle değil, yaşam döngüsü boyunca korunan `Id` ile düşünülür.

```csharp
public Guid Id { get; private set; }
public Guid OwnerId { get; private set; }
public Currency Currency { get; private set; }
```

Setter'ların private olması dış kodun nesneyi rastgele değiştirmesini engeller. State değişiklikleri ileride `Deposit`, `Withdraw` veya `Suspend` gibi domain davranışları üzerinden yapılacaktır.

## Wallet neden aggregate root?

Şu anki transaction ve consistency sınırı Wallet'tır. Uygulama katmanı wallet oluştururken constructor'a doğrudan erişmez; aggregate'ın factory metodunu çağırır:

```csharp
var wallet = Wallet.Create(
    ownerId,
    currency,
    createdAt
);
```

Aggregate root olma rolü C# anahtar kelimesi değildir. Şu anda şu kararlarla görünür hale gelir:

- Constructor private'tır.
- Geçerli oluşturma yolu `Wallet.Create` metodudur.
- Repository sözleşmesi yalnızca `Wallet` root'u üzerinden çalışır.
- State setter'ları dışarıya kapalıdır.
- Domain testleri root'un gözlemlenebilir davranışını doğrular.

Henüz `IAggregateRoot` marker interface'i veya ortak `Entity` base class'ı eklemedik. Bunlar domain davranışını kendiliğinden sağlamaz. Generic repository veya architecture testleri gibi somut bir ihtiyaç doğarsa yeniden değerlendirilecektir.

## Currency neden value object?

Currency'nin bağımsız bir kimliği ve yaşam döngüsü yoktur. `TRY`, onu taşıyan wallet'tan bağımsız olarak aynı kavramsal değerdir.

```csharp
public sealed record Currency
{
    public string Code { get; }

    public static Currency FromCode(string code)
    {
        var normalizedCode = code.Trim().ToUpperInvariant();

        if (normalizedCode != "TRY")
        {
            throw new ArgumentException(
                $"Currency code '{normalizedCode}' is not supported.",
                nameof(code));
        }

        return new Currency(normalizedCode);
    }
}
```

`record class` seçimi değer bazlı eşitlik sağlar. Bu bir C# value type değildir; heap üzerinde yaşayan bir reference type'tır. Buradaki “value object”, DDD rolünü ifade eder.

Başlangıçta yalnızca `TRY` desteklenir. Bu bilinçli kapsam daraltmasıdır; çoklu para birimi desteği gerçek bir use-case ile eklenecektir.

## Korunan oluşturma kuralları

`Wallet.Create` şu başlangıç koşullarını korur:

- `OwnerId`, `Guid.Empty` olamaz.
- `Currency`, `null` olamaz.
- Yeni wallet `Active` durumunda başlar.
- Yeni wallet bakiyesi `0` olur.
- Oluşturma zamanı UTC olarak saklanır.
- Kimlik domain factory tarafından üretilir.

```csharp
return new Wallet(
    id: Guid.NewGuid(),
    ownerId: ownerId,
    currency: currency,
    status: WalletStatus.Active,
    balance: 0m,
    createdAtUtc: createdAt.ToUniversalTime()
);
```

Bu kuralların bazıları basit input validation gibi görünse de nesnenin her oluşturma yolunda geçerli kalmasını istediğimiz için domain sınırında korunur.

## Domain ve Application sorumlulukları

Domain geçerli bir wallet'ın nasıl oluşturulacağını bilir. Aynı owner ve currency için daha önce wallet bulunup bulunmadığını bilemez; bu bilgi persistence erişimi gerektirir.

```text
Domain
└── Geçerli Wallet nasıl oluşturulur?

Application
└── Wallet zaten var mı, ne zaman oluşturulmalı ve ne zaman kaydedilmeli?
```

Bu nedenle duplicate kontrolü `CreateWalletHandler` içindedir. Eşzamanlı iki istekte bu kontrol tek başına garanti sağlamaz; kalıcı garanti PostgreSQL unique constraint ile Infrastructure aşamasında eklenecektir.

## Testlerle doğrulanan davranışlar

Domain testleri şu davranışları korur:

- Geçerli wallet doğru başlangıç durumuyla oluşturulur.
- Boş owner ID reddedilir.
- Null currency reddedilir.
- Offset içeren zaman UTC'ye normalize edilir.
- Currency kodu trim edilir ve büyük harfe çevrilir.
- Boş veya desteklenmeyen currency kodları reddedilir.

Mevcut domain test sonucu: **12 başarılı test case**.

## Henüz uygulanmayan DDD parçaları

Aşağıdakiler hedeflenen fakat henüz kodda bulunmayan yapılardır:

- Ledger ve double-entry accounting modeli
- Transfer aggregate veya process boundary kararı
- Domain event'leri
- Deposit, withdraw ve transfer davranışları
- Optimistic concurrency kontrolü
- Outbox ve integration event ayrımı
- Aggregate state'in PostgreSQL'e mapping'i

Bu yapıların her biri ilgili problem reproduce edildikten sonra eklenecektir.

## Mülakat özeti

“DDD kullandım” demek yerine şu açıklama yapılabilir:

> Wallet Core sınırında `Wallet`ı kimliği ve yaşam döngüsü olan aggregate root, `Currency`yi değer bazlı ve immutable bir value object olarak modelledim. Nesnenin geçersiz durumda oluşturulmasını private constructor, factory method ve private setter'larla engelledim. Persistence gerektiren duplicate kontrolünü domain nesnesine taşımayıp Application use-case'inde bıraktım. Aggregate marker veya base class gibi yapıları somut ihtiyaç olmadığı için başlangıçta eklemedim.
