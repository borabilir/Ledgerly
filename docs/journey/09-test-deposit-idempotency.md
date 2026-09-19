# 09 — Test yatırmasında idempotency

## Başlangıç

Test yatırması aynı tutarla iki kez çağrıldığında iki journal ve iki bakiye artışı oluşuyordu. Yanıt commit sonrası kaybolursa istemci tekrar deneyebileceği için bu gerçek bir doğruluk problemidir. [LAB-004](../labs/004-test-deposit-idempotency/README.md) önce eski davranışı reproduce etti, sonra çözümü doğruladı.

## Bu aşamada yapılanlar

- `Idempotency-Key` başlığı zorunlu oldu; yeni işlem için yeni key, retry için aynı key kullanılır.
- `(wallet_id, key)` kalıcı primary key ve ilk makbuz eklendi. Migration: `20260919120000_AddTestDepositIdempotency`.
- Key, wallet bakiyesi, journal ve posting'ler tek veritabanı save/transaction sınırında tutuldu.
- Aynı key + aynı amount ilk makbuzu döner; farklı amount `409`, eksik/geçersiz key `400` verir.
- Paralel aynı-key istekleri gerçek PostgreSQL testinde tek journal bırakır. Deadlock seçilen kaybeden transaction kontrollü write conflict olarak işlenir.

[Seçenekler ve trade-off](../adr/0009-test-deposit-idempotency.md). API yalnızca geliştirme ve integration test ortamlarında açık; gerçek banka/idempotency ve retention politikası henüz kapsam dışı.

## Doğrulama

`dotnet test Ledgerly.slnx` ile Domain, Application ve PostgreSQL integration testleri çalıştırıldı. Sıralı retry, farklı amount, eksik key, paralel aynı/farklı amount ve rollback durumu test edildi.
