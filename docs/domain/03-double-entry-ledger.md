# Double-entry Ledger Domain Modeli

> Sonraki adım (2026-09-16): [LedgerAccount domain modeli](04-ledger-account.md) tamamlandı. Bu belge önceki milestone'u anlatır; hesap persistence'ı henüz yok.

**Durum:** Domain modeli uygulandı; persistence ve para hareketi use-case'leri sırada.

**Tarih:** 2026-09-15

## Neden yalnızca Balance yeterli değil?

Wallet bakiyesinin 100'den 200'e çıkması son durumu anlatır. Paranın nereden geldiğini, hangi hesapları etkilediğini ve hareketin dengeli olup olmadığını anlatmaz. Ledger bu hareketin kaydını temsil eder. Şu an API yalnızca wallet oluşturup okuyor; kayıp para üreten bir transfer endpoint'i mevcut değil. Buradaki çalışma yeni finansal modelin kurallarını tanımlıyor.

## Üç kavram

- `JournalEntry`: Tek bir finansal olayın bütün muhasebe satırlarını bir arada tutar. Kimliği olan aggregate root'tur.
- `Posting`: Hesap referansı, yön ve pozitif tutar içeren immutable value object'tir. Ayrı kimliği yoktur; journal'ın parçasıdır.
- `PostingDirection`: `Debit` veya `Credit`. Negatif tutar kullanmak yerine yönü açıkça ifade eder.

Bir journal tam olarak iki satırla sınırlı değildir. Örneğin 100 debit, 60 credit ve 40 credit birlikte dengelidir. Temel denklem:

```text
toplam debit tutarı == toplam credit tutarı
```

## Debit ve credit ne demek?

Bunlar her hesap için sabit “artış” ve “azalış” anlamına gelmez. Bir varlık hesabı debit ile artarken, yükümlülük hesabı credit ile artar. Platformun kullanıcıya borçlu olduğu tutarı gösteren müşteri hesabını yükümlülük olarak düşünürsek, Alice'ten Bob'a 100 TRY aktarımının yönleri şöyle olur. Bu muhasebe ayrımının açıklaması için [TigerBeetle Financial Accounting](https://docs.tigerbeetle.com/coding/financial-accounting/) kaynağına bakılabilir.

| Hesap | Yön | Tutar | Platform açısından yorum |
|---|---|---|---|
| Alice müşteri yükümlülüğü | Debit | 100 TRY | Alice'e borcumuz 100 azalır |
| Bob müşteri yükümlülüğü | Credit | 100 TRY | Bob'a borcumuz 100 artar |

Bu tablo tasarım örneğidir. Henüz `LedgerAccount`, hesap türleri veya transfer handler'ı kodda yoktur. `AccountId` bir muhasebe hesabı referansıdır; `WalletId` ile aynı kimlik olmak zorunda değildir.

## Kodda korunan kurallar

`Posting.Create`:

- Hesap ID'si boş olamaz.
- Enum'a cast edilerek gönderilen tanımsız yönler kabul edilmez.
- Tutar pozitif olmalıdır.
- Tutar en fazla `999999999999999.9999` olabilir ve dört ondalık basamakla kayıpsız temsil edilebilmelidir.

Son kural mevcut wallet persistence'ındaki `numeric(19,4)` kapasitesiyle uyumlu ilk tutar politikasıdır. Bu, TRY'nin dört alt birimi olduğu veya ledger tablosu oluşturulduğu anlamına gelmez. `1.00000` sayısal olarak kayıpsız olduğu için kabul edilir; `1.00001` reddedilir. `decimal.Round` yalnızca uygunluğu kontrol eder, kaydedilen tutarı yuvarlamaz. Hesap bakiyesinin toplam kapasitesi ayrıca ele alınacaktır.

`JournalEntry.Create`:

- Currency ve posting koleksiyonu null olamaz; koleksiyonda null satır olamaz.
- En az iki posting ve en az iki farklı hesap gerekir. Tek hesaba debit/credit yazan kayıt bu projenin başlangıç kuralı olarak reddedilir.
- Debit ve credit toplamları tam eşit olmalıdır. Tolerans veya sessiz yuvarlama yoktur.
- Currency journal seviyesinde tutulur; bütün satırlar o currency cinsindendir. Mevcut Currency yalnızca TRY kabul eder.
- ID üretilir, oluşturma zamanı UTC'ye çevrilir.

```csharp
var journal = JournalEntry.Create(
    Currency.FromCode("TRY"),
    [
        Posting.Create(aliceAccountId, PostingDirection.Debit, 100m),
        Posting.Create(bobAccountId, PostingDirection.Credit, 100m),
    ],
    DateTimeOffset.UtcNow
);
```

## Oluşturulduktan sonra denge bozulabilir mi?

Dışarıdan verilen koleksiyonun kopyasını alıyoruz. Çağıran kod kendi array'ini değiştirse bile journal'ın satırları değişmiyor. Dışarıya bu kopyanın read-only görünümü veriliyor; yalnızca `IReadOnlyList` tipine güvenip mutable listeyi dışarı sızdırmıyoruz. Posting alanlarının setter'ı da yok. Journal'ın satır ekleme, güncelleme veya silme metodu bulunmuyor.

Bu garanti modelin normal C# API'si için geçerlidir. Database update/delete koruması, audit veya kalıcı append-only ledger garantisi değildir; henüz ledger persistence'ı yoktur.

## Domain tek başına neyi bilemez?

Rastgele üretilmiş fakat boş olmayan bir AccountId modelden geçebilir. Hesap gerçekten var mı, o hesabın currency'si journal ile uyumlu mu, kullanıcı yetkili mi, harcanabilir bakiye yeterli mi? Bunlar henüz doğrulanmıyor.

Dengeli olmak da tek başına doğru işlem demek değildir: yanlış hesaplara dengeli kayıt yazılabilir veya aynı kayıt iki kez işlenebilir. Account modeli, authorization, idempotency ve atomik kayıt ayrı problemler olarak gelecek. Wallet.Balance bugün ledger'dan türetilmiyor; başlangıçta sıfır olan mevcut alandır.

## Doğrulama ve devam

29 yeni test tutar sınırlarını, dengeyi, çok satırlı kaydı, geçersiz girdileri ve koleksiyonun değiştirilememesini doğrular. Toplam domain testi 41 oldu. Test çalıştırma kanıtı [milestone kaydında](../journey/04-ledger-domain.md), alternatifler [ADR-0004'te](../adr/0004-journal-entry-and-postings.md).

Sonraki adım hesapların kimliğini ve türünü modelleyip journal/posting persistence sınırını kurmak; ardından test bakiyesi yatırmayı bütün satırları tek transaction'da kaydeden use-case olarak eklemek.
