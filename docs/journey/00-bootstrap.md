# 00 — Solution Bootstrap

**Durum:** Planlandı; komutlar çalıştırıldıktan sonra doğrulanacak  
**Tarih:** 2026-09-03

## Hedef

İlk hedef çalışan finansal özellik eklemek değil, Wallet Core için küçük ve derlenebilir bir solution sınırı oluşturmaktır.

## Planlanan solution yapısı

```text
src/
  Ledgerly.Api/
  Ledgerly.Application/
  Ledgerly.Domain/
  Ledgerly.Infrastructure/
tests/
  Ledgerly.Domain.Tests/
  Ledgerly.IntegrationTests/
```

Bu projeler bağımsız servis değildir. Tek deploy edilen Wallet Core uygulamasının katmanlarıdır.

## Bağımlılık kuralları

```text
Api ----------> Application
 |                   |
 +--> Infrastructure v
                  Domain
```

- Domain başka bir projeye referans vermez.
- Application yalnızca Domain'e referans verir.
- Infrastructure, Application ve Domain'e referans verir.
- API, Application ve Infrastructure'ı composition root olarak bir araya getirir.
- Domain testleri yalnızca Domain'i hedefler.
- Integration testleri uygulamayı dış sınırından çalıştırır.

## Oluşturma komutları

Ledgerly kök dizininde:

```powershell
dotnet new sln --name Ledgerly

dotnet new webapi --name Ledgerly.Api --output src/Ledgerly.Api --use-controllers
dotnet new classlib --name Ledgerly.Domain --output src/Ledgerly.Domain
dotnet new classlib --name Ledgerly.Application --output src/Ledgerly.Application
dotnet new classlib --name Ledgerly.Infrastructure --output src/Ledgerly.Infrastructure

dotnet new xunit --name Ledgerly.Domain.Tests --output tests/Ledgerly.Domain.Tests
dotnet new xunit --name Ledgerly.IntegrationTests --output tests/Ledgerly.IntegrationTests
```

Projeleri solution'a ekleme:

```powershell
dotnet sln Ledgerly.slnx add src/Ledgerly.Api/Ledgerly.Api.csproj
dotnet sln Ledgerly.slnx add src/Ledgerly.Domain/Ledgerly.Domain.csproj
dotnet sln Ledgerly.slnx add src/Ledgerly.Application/Ledgerly.Application.csproj
dotnet sln Ledgerly.slnx add src/Ledgerly.Infrastructure/Ledgerly.Infrastructure.csproj
dotnet sln Ledgerly.slnx add tests/Ledgerly.Domain.Tests/Ledgerly.Domain.Tests.csproj
dotnet sln Ledgerly.slnx add tests/Ledgerly.IntegrationTests/Ledgerly.IntegrationTests.csproj
```

Solution dosyası komutlarda açıkça belirtilir. Böylece CLI'ın bulunduğu dizindeki solution'ı otomatik seçtiği varsayılmaz.

Proje referansları:

```powershell
dotnet add src/Ledgerly.Application/Ledgerly.Application.csproj reference src/Ledgerly.Domain/Ledgerly.Domain.csproj

dotnet add src/Ledgerly.Infrastructure/Ledgerly.Infrastructure.csproj reference src/Ledgerly.Domain/Ledgerly.Domain.csproj
dotnet add src/Ledgerly.Infrastructure/Ledgerly.Infrastructure.csproj reference src/Ledgerly.Application/Ledgerly.Application.csproj

dotnet add src/Ledgerly.Api/Ledgerly.Api.csproj reference src/Ledgerly.Application/Ledgerly.Application.csproj
dotnet add src/Ledgerly.Api/Ledgerly.Api.csproj reference src/Ledgerly.Infrastructure/Ledgerly.Infrastructure.csproj

dotnet add tests/Ledgerly.Domain.Tests/Ledgerly.Domain.Tests.csproj reference src/Ledgerly.Domain/Ledgerly.Domain.csproj
dotnet add tests/Ledgerly.IntegrationTests/Ledgerly.IntegrationTests.csproj reference src/Ledgerly.Api/Ledgerly.Api.csproj
```

İlk doğrulama:

```powershell
dotnet sln Ledgerly.slnx list
dotnet build Ledgerly.slnx
dotnet test Ledgerly.slnx
```

`dotnet sln Ledgerly.slnx list` çıktısında altı proje görülmeden build sonucu yeterli kanıt sayılmaz. Boş bir solution hiçbir proje derlemeden `Build succeeded` döndürebilir.

## Bu aşamada özellikle eklenmeyenler

- EF Core ve Npgsql paketleri
- PostgreSQL container tanımı
- MediatR veya başka bir CQRS kütüphanesi
- Kafka/Redpanda, MongoDB ve Redis
- Authentication
- Ayrı mikroservis projeleri

Bu bileşenler ilgili probleme ulaşıldığında ayrı bir karar ve doğrulama adımıyla eklenecektir.

## Git repository ve baseline commit

Solution doğrulandıktan sonra değişiklik geçmişini başlatmak için Ledgerly kök dizininde aşağıdaki komutlar çalıştırılır.

Önce .NET projelerine uygun ignore kuralları oluşturulur:

```powershell
dotnet new gitignore
```

Repository oluşturulur ve varsayılan branch adı `main` olarak belirlenir:

```powershell
git init
git branch -M main
```

Dosyalar stage edilmeden önce ignore kuralları kontrol edilir:

```powershell
git status --short
```

Çıktıda `bin/`, `obj/`, `.vs/`, kullanıcıya özel IDE dosyaları veya secret içeren dosyalar bulunmamalıdır. Ardından başlangıç dosyaları stage edilir:

```powershell
git add .
git status --short
git diff --cached --stat
```

Liste beklendiği gibiyse baseline commit oluşturulur ve doğrulanır:

```powershell
git commit -m "chore: bootstrap Ledgerly solution"
git log --oneline -1
git status --short
```

Son `git status --short` çıktısının boş olması working tree'nin temiz olduğunu gösterir. Bu commit, ilerideki problem senaryolarında önce/sonra karşılaştırması için geri dönülebilir başlangıç noktasıdır.

> Connection string parolası, API key veya başka bir secret hiçbir aşamada Git'e eklenmez. Lokal secret'lar ileride .NET user-secrets veya environment variable ile yönetilecektir.

## Tamamlanma kanıtı

- [ ] `global.json` seçilen .NET 10 SDK sürümünü sabitliyor.
- [ ] `dotnet sln Ledgerly.slnx list` çıktısında altı proje görünüyor.
- [ ] Proje referansları belirlenen bağımlılık yönüyle uyumlu.
- [ ] `dotnet build Ledgerly.slnx` hatasız tamamlanıyor.
- [ ] `dotnet test Ledgerly.slnx` iki test projesini çalıştırıyor.
- [ ] `.gitignore` build ve IDE çıktılarını dışarıda bırakıyor.
- [ ] İlk commit yalnızca bootstrap değişikliklerini içeriyor.
- [ ] Baseline commit sonrasında working tree temiz.

## Sonraki adım

Bootstrap doğrulandıktan sonra ilk vertical slice olan **Create Wallet** için ubiquitous language, invariant, API contract ve domain modeli tasarlanacaktır.
