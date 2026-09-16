# ADR-0006 — Ledger Satır Modelleri ve Tek Save Sınırı

**Durum:** Accepted

**Tarih:** 2026-09-16

**İlgili lab:** [LAB-002 — Journal Atomicity](../labs/002-journal-atomicity/README.md)

## Bağlam

Hesap ve dengeli journal domain modelleri bellekte var; kalıcı saklama yoktu. Immutable posting koleksiyonunu ve value semantics'i korurken ilişkisel satırlara ihtiyaç doğdu. Başlık ve posting'leri ayrı commit etmek, son satır hata aldığında yarım muhasebe kaydı bırakabilir.

## Alternatifler

| Karar | Seçenek | Bedel / kazanç |
|---|---|---|
| EF mapping | Domain'i doğrudan map etmek | Daha az dönüşüm; EF'nin dolduracağı koleksiyon/constructor/backing field düzeni ve aynı Posting instance'ının tekrarı ele alınmalı |
| EF mapping | Ayrı Infrastructure satır modelleri | Dönüşüm kodu ve ek tipler; domain immutable API'si değişmez, satır kimliği domain'e taşınmaz |
| Kayıt | Her satırı ayrı SaveChanges ile yazmak | Basit görünür ama önceki commit'ler sonraki hatada geri alınmaz |
| Kayıt | Bütün graph'ı tek SaveChanges ile yazmak | Mevcut Unit of Work ve provider transaction'ı yeterli; bellekte graph hazırlanır |
| Kayıt | Birden çok save'i explicit transaction'a almak | Geçerli alternatif; bu akışta gereksiz lifecycle yönetimi |
| Currency uyumu | Sadece application kontrolü | Daha az key/index; SQL veya farklı yazma yolunda ihlal edilebilir |
| Currency uyumu | Composite foreign key | Ek currency sütunu ve alternate key index'leri; referans ve currency birlikte korunur |

## Karar

Ayrı internal EF satır modelleri, domain'e özel iki repository portu, read snapshot'ları ve mevcut IUnitOfWork kullanılacak. Journal Add bütün satırları hazırlar; repository içinde save yok. Tek SaveChanges akışında başarısızlık transaction tarafından geri alınır. Yeni transaction abstraction eklenmez.

Account/wallet ve posting/account/journal ilişkileri currency ile birlikte kurulur. Hesap türü/WalletId eşleşmesi check, hesap tekilliği filtreli unique index ile korunur. Posting kimliği journal ID + sıra numarasıdır. Cascade delete yerine Restrict kullanılır.

## Kanıt ve sonuçlar

Bozuk ayrı-save deneyinde başlık ve ilk posting commit olur, ikinci posting 23503 alır; yeni bağlantı 1 journal / 1 posting görür. Tek Unit of Work deneyinde başlık ve ilk posting INSERT komutlarının başarıyla çalıştığı gözlenir, aynı ikinci-satır hatası sonrası yeni bağlantı 0 / 0 görür. Seed wallet/account kayıtları korunur.

Ek tablo modelleri ve composite key index maliyeti kabul edildi; latency/throughput benchmark'ı yapılmadı. Kalıcı denge trigger'ı, append-only SQL yetkileri, bakiye kontrolü, idempotency veya otomatik hesap provisioning'i bu kararın tamamlanmış sonucu değildir.

## Yeniden değerlendirme

Bir use-case birden fazla save/SQL komutu gerektirirse explicit transaction sınırı; SQL üzerinden ek yazıcılar varsa DB seviyesinde denge/immutability; yeni hesap rolleri varsa filtreli unique index'ler yeniden değerlendirilir. Transfer/deposit Application use-case'i bu altyapının üzerine ayrı milestone olarak eklenecek.
