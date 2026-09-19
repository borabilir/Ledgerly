# 14 — Transfer History Worker ve MongoDB başlangıcı

## Başlangıç

`wallet.transfer-completed.v1` mesajı RabbitMQ'daki durable `ledgerly.transfer-history` kuyruğuna ulaşabiliyordu. Fakat kuyruğu okuyacak consumer ve sorgulamaya uygun işlem geçmişi modeli yoktu.

Bu aşamada consumer davranışını henüz yazmadan önce ikinci deployable'ın ve ona ait veri deposunun tekrarlanabilir başlangıç zemini kuruldu.

## MongoDB neden kullanılıyor?

PostgreSQL hâlâ para hareketlerinin ve muhasebe kayıtlarının source of truth'udur. MongoDB finansal transaction yürütmek için değil, ekranda gösterilecek transfer geçmişinin read model'i için seçildi.

İleride tek bir doküman şu tür gösterime hazır alanları taşıyabilir:

```text
transferId
sourceWalletId
destinationWalletId
amount
currencyCode
occurredAtUtc
direction
counterparty
```

Bu model, write tarafındaki normalize tablolara ve join'lere birebir uymak zorunda değildir. Amaç CQRS'in read tarafını ve document database trade-off'larını gerçek bir ihtiyaç üzerinde öğrenmektir.

## Kurulum adımları

### 1. MongoDB container'ı

`compose.yml` içine resmi `mongo:8.3.11-noble` image'ı eklendi:

```yaml
mongodb:
  image: mongo:8.3.11-noble
  ports:
    - "27017:27017"
  volumes:
    - ledgerly_mongodb_data:/data/db
```

Development kullanıcı/parolası compose environment değerlerinden gelir. Healthcheck, container process'inin yalnızca çalışmasına değil, authenticated `ping` komutuna cevap vermesine bakar.

```powershell
docker compose up -d mongodb
docker compose ps mongodb
```

Beklenen durum:

```text
ledgerly-mongodb-1   running (healthy)   0.0.0.0:27017->27017/tcp
```

### 2. Worker projesi

Oluşturulan proje:

```text
src/Ledgerly.TransferHistory.Worker
```

Karşılığı olan başlangıç komutu şudur:

```powershell
dotnet new worker `
  --name Ledgerly.TransferHistory.Worker `
  --output src/Ledgerly.TransferHistory.Worker `
  --framework net10.0
```

Proje solution'ın `/src/` klasörüne eklendi. Worker, Ledgerly API katmanlarına project reference almıyor. İleride API ile yalnızca RabbitMQ integration event sözleşmesi üzerinden iletişim kuracak.

### 3. MongoDB driver

Resmi .NET driver eklendi:

```powershell
dotnet add src/Ledgerly.TransferHistory.Worker/Ledgerly.TransferHistory.Worker.csproj `
  package MongoDB.Driver `
  --version 3.11.2
```

`MongoClient` singleton kaydedildi. Driver connection pooling'i kendi yönettiği için her işlemde yeni client oluşturulmuyor.

### 4. Configuration

Genel `appsettings.json` içinde connection string boş bırakıldı. Lokal değer yalnızca `appsettings.Development.json` içinde bulunuyor:

```text
mongodb://ledgerly:ledgerly_dev@localhost:27017/?authSource=admin
```

`authSource=admin`, compose'un oluşturduğu root development kullanıcısının `admin` veritabanında doğrulanacağını belirtir. Hedef read-model database'i `ledgerly_transfer_history` olarak ayrıdır.

Options startup sırasında validate edilir. Connection string veya database adı eksikse worker sessizce yanlış ayarla çalışmaya devam etmez.

### 5. Startup bağlantı kontrolü

`MongoDbStartupCheck`, worker başlarken MongoDB'ye şu komutu gönderir:

```javascript
{ ping: 1 }
```

Bağlantı veya authentication başarısızsa host başlangıcı hata verir. Başarılıysa şu log oluşur:

```text
Transfer History Worker connected to MongoDB database ledgerly_transfer_history.
```

Bu kontrol henüz collection oluşturmaz veya veri yazmaz. MongoDB'de database/collection ilk gerçek yazmada fiziksel olarak oluşacaktır.

## Çalıştırma

Terminal:

```powershell
docker compose up -d
dotnet run `
  --project src/Ledgerly.TransferHistory.Worker/Ledgerly.TransferHistory.Worker.csproj `
  --launch-profile Ledgerly.TransferHistory.Worker
```

VS Code'da iki seçenek eklendi:

- `Ledgerly.TransferHistory.Worker`: yalnızca worker'ı debug eder.
- `Ledgerly: API + Transfer History Worker`: API ve worker'ı birlikte başlatır.

## Doğrulama

Bu milestone'da aşağıdakiler doğrulandı:

```text
Compose config geçerli                         ✅
MongoDB container healthcheck                  ✅
Worker Release build                           ✅
Options configuration                          ✅
.NET driver ile authenticated MongoDB ping      ✅
Graceful worker shutdown                        ✅
```

## Şu anda özellikle ne yok?

- Worker RabbitMQ'ya bağlanmıyor.
- Queue'dan mesaj tüketmiyor.
- MongoDB collection veya index oluşturmuyor.
- Transfer history dokümanı yazmıyor.
- Acknowledgement vermiyor.
- Duplicate event koruması yok.

Bu eksikler hata değil; bir sonraki lab'ın kontrollü başlangıç noktasıdır.

## Sonraki adım

`wallet.transfer-completed.v1` eventini tüketen en basit consumer yazılacak. Aynı `EventId` iki kere publish edilerek MongoDB'ye duplicate history kaydı oluşması deterministik biçimde reproduce edilecek. Bundan sonra Inbox/Idempotent Consumer alternatifleri değerlendirilecek.
