# 08 — LedgerAccount Purpose

**Durum:** Tamamlandı — 2026-09-17

## Problemden karara

Test fon hesabı yalnızca Asset + boş WalletId özelliklerinden anlaşılıyordu. “Bu hesap ne için kullanılıyor?” bilgisini `LedgerAccountPurpose` olarak açıkça modelledik. [Basit açıklama](../domain/05-ledger-account-purpose.md), [alternatifler ve karar](../adr/0008-explicit-ledger-account-purpose.md).

## Değişiklik

Purpose: Wallet = 1, TestFunding = 2. Factory → Record → Snapshot boyunca taşınır. GetTestFundingAsync amaç ve currency üzerinden sorgular. Database amaç/tür/wallet birleşimini doğrular; test fonu unique filtresi purpose = 2 olur. Mevcut index adı, conflict davranışı ve yatırmanın transaction sınırı korunur.

## Veri taşıma ve test

`20260917155805_AddLedgerAccountPurpose` mevcut customer ve funding satırlarını doğru amaçla doldurur. Hesap kimliklerini değiştirmez. Varsayılan purpose yoktur.

Migration testi ayrı PostgreSQL schema'sında önceki sürümü kurdu, wallet + iki hesap + dengeli journal/posting ekledi; yeni migration sonrasında kimliklerin, tutarların ve amaçların korunduğunu doğruladı. Down/up dönüşü de aynı verilerle geçti. Yeni database kurulumu mevcut fixture üzerinden doğrulanır.

9 yeni integration case:

- 1: Yalnızca wallet hesabı varken fon sorgusu null; fon eklendiğinde açık TestFunding amacıyla okunur.
- 6: Tanımsız amaçlar ve yanlış Type/Purpose/WalletId birleşimleri 23514 ile reddedilir.
- 1: Amaç yazılmayan INSERT, 23502 ile reddedilir.
- 1: Eski verilerin migration upgrade/down/upgrade sırasında korunması.

Tüm testler: **124 başarılı** — 53 Domain, 9 Application, 62 Integration. Mevcut factory/persistence testlerine Purpose doğrulamaları eklendi; bunlar yeni case sayısını artırmaz.

## Sonraki adım

Test yatırmasındaki aynı başarılı isteğin tekrar işlenmesi hâlâ ayrı bir problem. Idempotency lab'ı sırada; Purpose bu davranışı değiştirmez.
