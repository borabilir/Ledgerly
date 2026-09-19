# ADR-0010 — Wallet transferinde optimistic concurrency ile double-spending koruması

**Durum:** Kabul edildi — 2026-09-19

## Bağlam

İki transfer aynı kaynak wallet'ın aynı bakiyesini eşzamanlı okuyabilir. İkisi de ayrı hedefe para yazarken kaynak bakiyeyi aynı son değere getirirse toplam para artar. [LAB-005](../labs/005-wallet-transfer-double-spending/README.md) guard olmadan 100 TRY'nin iki ayrı 80 TRY kararı için kullanılabildiğini gösterdi.

## Karar

Transfer iki tracked wallet'ı değiştirir; kaynak `Debit`, hedef `Credit` olur. Wallet `Balance` alanındaki mevcut optimistic concurrency token korunur. İki bakiye değişikliği, eksik ledger hesapları ve dengeli journal tek `SaveChanges` transaction'ında yazılır. Eski kaynak bakiyeye dayalı update başarısızsa ikinci transferin bütün yazıları rollback olur ve API `409` döner. Otomatik retry yapılmaz; aynı business kararını güncel bakiyeyle körlemesine tekrar etmek yetersiz bakiye sonucunu değiştirebilir.

Kaynak liability hesabına debit, hedef liability hesabına credit yazılır. Farklı wallet ve aynı currency kuralları Application sınırında; pozitif tutar, hassasiyet ve yeterli bakiye Wallet domain modelinde korunur.

## Alternatifler

Row lock ikinci işlemi bekletebilirdi; bu aşamada açık transaction ve kilit sırası karmaşıklığını istemiyoruz. Serializable isolation genel koruma sağlar ama retry/abort maliyeti getirir. Atomik conditional SQL yüksek contention için iyi adaydır; iki wallet ve journal'ın receipt akışı için özel persistence yolu gerektirir. Mevcut EF concurrency modeli küçük baseline için yeterlidir.

## Sonuçlar ve sınırlar

Çakışan işlemlerden biri kullanıcıya `409` dönebilir; doğruluk erişilebilirlikten önce gelir. Balance token pozitif/negatif değişimlerin eski değere geri döndüğü ABA senaryosunda genel aggregate version değildir; transfer modeli genişlediğinde `version` veya PostgreSQL `xmin` yeniden değerlendirilecek.

Transfer retry idempotency'si, transfer durum kaydı, farklı currency, fee, reversal ve auth bu kararın dışında kalır.
