# Ledgerly

Ledgerly, dijital cüzdan ve ödeme problemlerini küçük bir başlangıç sistemi üzerinde senaryo bazlı olarak incelemek için geliştirilen bir öğrenme projesidir.

Proje doğrudan production-grade bir mikroservis platformu olarak başlamaz. Her mimari yetenek, gerçek bir problem yeniden üretildikten ve alternatifleri değerlendirildikten sonra eklenir.

Dokümantasyonun başlangıç noktası: [Project Charter](docs/00-project-charter.md)

## Çalışma döngüsü

```text
Baseline
  -> Problem
  -> Problemi reproduce et
  -> Gözlem ve kanıt
  -> Root cause
  -> Alternatif çözümler
  -> Trade-off
  -> Karar
  -> Implementasyon
  -> Test ve doğrulama
  -> Dokümantasyon
```

Detaylı kayıt düzeni için [dokümantasyon rehberine](docs/README.md) bakın.

## Uygulamayı çalıştırma

```powershell
docker compose up -d
dotnet tool restore
dotnet dev-certs https --trust # yalnızca ilk lokal kurulumda gerekiyorsa
dotnet ef database update `
  --project src/Ledgerly.Infrastructure/Ledgerly.Infrastructure.csproj `
  --startup-project src/Ledgerly.Api/Ledgerly.Api.csproj `
  -- --environment Development
dotnet run --project src/Ledgerly.Api/Ledgerly.Api.csproj --launch-profile https
```

Development ortamında API arayüzü `https://localhost:7092/scalar/v1`, OpenAPI belgesi ise `https://localhost:7092/openapi/v1.json` adresindedir.

İlk endpoint:

```http
POST /api/wallets
Content-Type: application/json

{
  "ownerId": "7d8ea830-5bd0-4f5f-bdc8-9d3c413ea55e",
  "currencyCode": "TRY"
}
```

Tüm testler:

```powershell
dotnet test Ledgerly.slnx
```
