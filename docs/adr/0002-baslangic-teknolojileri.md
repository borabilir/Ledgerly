# ADR-0002 — Başlangıç Teknoloji Seçimleri

**Durum:** Accepted  
**Tarih:** 2026-09-03

## Karar özeti

| Alan | Seçim | Gerekçe |
|---|---|---|
| Runtime | .NET 10 LTS | Yeni proje için güncel, uzun süre desteklenen platform |
| Dil | C# 14 | .NET 10 ile gelen kararlı dil sürümü; preview özellikler kapalı |
| HTTP | ASP.NET Core Web API + Controllers | Açık endpoint sınırları, filter/middleware ve API contract pratiği |
| Write database | PostgreSQL | ACID transaction, constraint, isolation ve locking deneyleri |
| Veri erişimi | EF Core | Unit of Work, transaction ve optimistic concurrency desteği; gerektiğinde SQL'e inme imkânı |
| Test | xUnit | Domain ve integration testleri için sade, yaygın ekosistem |
| Lokal altyapı | Docker Compose | İlk aşamada yalnızca PostgreSQL'i tekrar üretilebilir biçimde çalıştırmak |
| NoSQL | MongoDB — daha sonra | CQRS transaction-history read model ve eventual consistency deneyleri |

## Neden .NET 10 LTS?

Ledgerly sıfırdan başlayan uzun soluklu bir öğrenme projesidir. Güncel LTS sürümü, daha uzun destek süresi ve güncel ASP.NET Core/EF Core yetenekleri sağlar. Preview runtime veya dil özellikleri kullanılmayacaktır; amaç platform önizlemesi değil, sistem tasarımı problemleridir.

SDK sürümü `global.json` ile sabitlenecektir. Tam sürüm, yerel kurulum doğrulandıktan sonra belgeye yazılacaktır.

## Neden PostgreSQL?

İlk write modelde transferin tüm finansal etkileri atomik olmalıdır. PostgreSQL; ACID transaction, constraint, isolation level, row-level locking, optimistic concurrency, atomik koşullu update ve execution plan çalışmalarına uygun bir referans noktası sağlar.

PostgreSQL tek geçerli seçim değildir. SQL Server da aynı sınıftaki ihtiyaçları karşılayabilir. PostgreSQL; ücretsiz, container ile kolay kurulabilir ve mevcut SQL deneyiminin dışına kontrollü bir adım attırdığı için seçilmiştir.

## MongoDB neden ilk source of truth değil?

MongoDB transaction desteklese de ilk günden hem finansal domaini hem NoSQL veri modelini öğrenmek root cause analizini zorlaştırır. İlk aşamada finansal source of truth PostgreSQL olacaktır. MongoDB daha sonra transaction history read model olarak eklenerek eventual consistency, projection lag, stale read, duplicate event, idempotent projection, rebuild ve doküman index'leri çalışılacaktır.

## Araç ekleme ilkesi

Kafka/Redpanda, MongoDB, Redis ve resilience kütüphaneleri başlangıç template'ine eklenmez. Her araç önce çözdüğü problem reproduce edildikten ve alternatifleri değerlendirildikten sonra sisteme girer.
