# CQRS ve Create Wallet Application Akışı

**Durum:** Command, persistence ve HTTP adaptörleri uygulandı; query tarafı planlandı
**Tarih:** 2026-09-13

## Amaç

Bu belge Create Wallet use-case'inin Application katmanında nasıl orkestre edildiğini ve kullanılan mimari kararları açıklar.

## Mimari bağlam

Ledgerly şu anda tek deployable ile başlayan modüler bir monolith'tir. Kod bağımlılıkları Clean Architecture / Onion Architecture ilkelerine göre içeri doğru yönlendirilir:

```text
API ────────────────> Application ─────> Domain
 │                         ▲
 └──> Infrastructure ──────┘
              │
              └────────────────────────> Domain
```

- Domain hiçbir dış katmana bağımlı değildir.
- Application, Domain'i kullanır ve ihtiyaç duyduğu portları tanımlar.
- Infrastructure bu portları PostgreSQL, EF Core ve diğer adaptörlerle uygular.
- API composition root göreviyle bağımlılıkları bağlar ve HTTP sözleşmesini sunar.

## CQRS'i şu anda nasıl kullanıyoruz?

CQRS, write ve read use-case'lerini farklı modellerle ele alma yaklaşımıdır. Şu an yalnızca ilk command tarafı uygulanmıştır:

```text
CreateWalletCommand
        ↓
CreateWalletHandler
        ↓
Domain + persistence portları
        ↓
CreateWalletResult
```

Henüz ayrı read database, projection veya query handler yoktur. Bu nedenle “tamamen ayrılmış CQRS sistemi” değil, CQRS yönünde açık bir use-case ayrımı uygulanmıştır.

## Command

```csharp
public sealed record CreateWalletCommand(
    Guid OwnerId,
    string CurrencyCode
);
```

Command dış sınırdan gelen niyeti ve ham veriyi taşır. `CurrencyCode` burada `string`dir; doğrulanmış domain tipi handler içinde `Currency.FromCode` ile oluşturulur.

Command'ın `record` olması onu immutable ve değer bazlı bir mesaj modeli yapar. Command entity değildir ve kimliğe dayalı bir yaşam döngüsü taşımaz.

## Handler

Handler use-case sırasını yönetir:

```text
1. Command'ı al
2. Currency value object oluştur
3. Aynı owner ve currency için wallet var mı kontrol et
4. Wallet aggregate'ını oluştur
5. Repository'ye ekle
6. Unit of Work ile transaction'ı kaydet
7. Oluşturulan WalletId'yi döndür
```

Handler iş kuralını yeniden yazmaz. Geçerli wallet oluşturma sorumluluğunu domain factory'ye bırakır.

## Result neden yalnızca WalletId taşıyor?

```csharp
public sealed record CreateWalletResult(Guid WalletId);
```

Command sonucunda tam bir read model döndürmek yerine oluşturulan kaynağın kimliği döndürülür. API bu kimliği `201 Created` response body'sinde sunar. Henüz kaynağı okuyacak GET endpoint'i olmadığı için gerçekte çalışmayan bir `Location` header üretilmedi. Wallet detayını okuma ihtiyacı ayrı bir query use-case'i ile ele alınacaktır.

Alternatif olarak handler doğrudan `Guid` döndürebilirdi. İsimlendirilmiş result tipi, use-case sözleşmesini daha açık kılar ve ileride metadata eklenmesine alan bırakır.

## Repository portu neden Application'da?

```csharp
public interface IWalletRepository
{
    Task<bool> ExistsAsync(
        Guid ownerId,
        Currency currency,
        CancellationToken cancellationToken = default);

    void Add(Wallet wallet);
}
```

Application persistence'e ihtiyaç duyar fakat PostgreSQL veya EF Core detaylarını bilmez. Bu nedenle ihtiyaç duyduğu interface'i kendisi tanımlar. Infrastructure daha sonra bu interface'i uygular.

```text
Application: “Wallet sorgulayabilen ve ekleyebilen bir porta ihtiyacım var.”
Infrastructure: “Bu portu PostgreSQL ve EF Core ile uygularım.”
```

Bu Dependency Inversion uygulamasıdır. Repository interface'ini Infrastructure'a koymak Application'ın dış katmana bağımlı olmasına neden olurdu.

Klasik DDD projelerinde aggregate repository interface'inin Domain'de tutulduğu da görülür. Ledgerly'de repository'ye domain nesnesi değil use-case ihtiyaç duyduğu için Application katmanı seçildi.

## Unit of Work neden ayrı?

```csharp
public interface IUnitOfWork
{
    Task SaveChangesAsync(
        CancellationToken cancellationToken = default);
}
```

`IWalletRepository.Add` aggregate'ı çalışma birimine ekler; transaction'ı commit etmez. Commit sınırı `IUnitOfWork` ile açıkça Application handler tarafından kontrol edilir.

Bu ilk use-case'te tek repository olduğu için fazla görünebilir. Transfer gibi birden fazla değişikliğin aynı transaction içinde atomik kaydedileceği use-case'lerde transaction sınırını repository metodunun içine gizlememek önem kazanacaktır.

## TimeProvider neden enjekte edildi?

Handler doğrudan `DateTimeOffset.UtcNow` kullansaydı test her çalışmada farklı sonuç üretirdi. .NET'in `TimeProvider` abstraction'ı production'da gerçek zamanı, testte sabit zamanı kullanmamızı sağlar:

```csharp
var wallet = Wallet.Create(
    command.OwnerId,
    currency,
    _timeProvider.GetUtcNow()
);
```

Testte kullanılan `StubTimeProvider`, belirlenmiş zamanı döndürür. Böylece test deterministik kalır.

## Duplicate kontrolü ve yarış durumu

Handler önce `ExistsAsync` çağırır. Bu kontrol kullanıcıya erken ve anlaşılır bir hata vermek için değerlidir ancak eşzamanlı iki isteği tek başına engelleyemez:

```text
İstek A: Exists → false
İstek B: Exists → false
İstek A: Insert
İstek B: Insert
```

Kalıcı doğruluk için `(owner_id, currency)` üzerinde PostgreSQL unique constraint gerekir. Infrastructure aşamasında constraint ihlali yakalanıp aynı Application hatasına çevrilecektir.

## Hata modeli kararı

Duplicate wallet şu anda `WalletAlreadyExistsException` ile temsil edilir. API bu hatayı daha sonra `409 Conflict`e dönüştürecektir.

Alternatifler:

- Exception: Başlangıçta basit, akışı okunur; beklenen hatalarda exception maliyeti ve mapping ihtiyacı vardır.
- Result type: Beklenen başarısızlıkları açıkça modelleyebilir; ek model ve branching oluşturur.
- OneOf/discriminated union kütüphanesi: Güçlü sözleşme sunar; harici bağımlılık getirir.

Başlangıç için custom exception seçildi. Result yaklaşımı daha fazla hata türü oluştuğunda yeniden değerlendirilecektir.

## Neden MediatR kullanmadık?

Command ve handler ayrımı için MediatR zorunlu değildir. Şu anda handler doğrudan çağrılabilir. Pipeline behavior, merkezi validation veya çok sayıda handler dispatch ihtiyacı ortaya çıkarsa MediatR ya da alternatifleri trade-off ile değerlendirilecektir.

## Application testleri

Gerçek PostgreSQL yerine elle yazılmış test double'lar kullanılır:

- `FakeWalletRepository`, eklenen wallet'ı bellekte yakalar.
- `FakeUnitOfWork`, save çağrı sayısını kaydeder.
- `StubTimeProvider`, sabit zaman döndürür.

Doğrulanan senaryolar:

1. Wallet yoksa oluşturulur, eklenir, bir kez kaydedilir ve ID döner.
2. Wallet varsa `WalletAlreadyExistsException` atılır; ekleme ve save yapılmaz.

Mevcut Application test sonucu: **2 başarılı test**.

## Uygulandı ve planlandı ayrımı

### Uygulandı

- Command, handler ve result
- Currency dönüşümü
- Repository ve Unit of Work portları
- TimeProvider ile deterministik zaman
- Duplicate kontrolü ve Application exception
- Happy-path ve duplicate Application testleri
- EF Core DbContext ve PostgreSQL repository
- `(owner_id, currency)` unique constraint'i
- Transaction integration testi
- Dependency injection kaydı
- `POST /api/wallets` endpoint'i
- Exception-to-ProblemDetails mapping
- HTTP functional testleri

### Planlandı

- Yarış anındaki unique constraint exception'ını kontrollü Application hatasına çevirmek
- Query tarafı ve read model

## Sonraki adım

Eşzamanlı iki Create Wallet isteğinin pre-check'i birlikte geçtiği yarışı reproduce etmek ve PostgreSQL garantisini API'de deterministik bir `409 Conflict` sonucuna çevirmek.
