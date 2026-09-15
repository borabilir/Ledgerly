# ADR-0004 — Dengeli Journal Entry ve Posting Modeli

**Durum:** Accepted

**Tarih:** 2026-09-15

**İlgili çalışma:** [Ledger domain milestone](../journey/04-ledger-domain.md)

## Bağlam

Wallet oluşturma ve okuma tamamlandı. Para hareketi eklemeden önce hareketin hesaplar arasındaki karşılığını temsil etmemiz gerekiyor. Balance alanı tek başına hareket geçmişini veya dengeyi ifade etmiyor. Mevcut sistemde çalışan bir transfer arızası iddia etmiyoruz; henüz olmayan finansal davranışın modelini kuruyoruz.

## Alternatifler ve trade-off

| Seçenek | Doğruluk ve ifade gücü | Maliyet |
|---|---|---|
| Yalnızca Balance güncellemek | Son durum var; hareketin karşılığı ve denge kuralı modelde yok | En az kod, sonradan iz sürmek zor |
| From/To/Amount içeren transfer satırı | Basit iki hesaplı transferi ifade eder | Ücret veya birden çok karşı hesap için genişletme gerekir |
| Journal altında iki veya daha fazla posting | Tüm debit/credit satırları birlikte doğrulanır | Daha çok domain kavramı; ileride satırlar atomik kaydedilmeli |

İlk seçeneğin düşük kayıt sayısı veya üçüncünün daha çok satır gerektirmesi bir performans ölçümü değildir. Bu adımda latency/throughput benchmark'ı yapılmadı. Yeni servis, broker veya operasyon bileşeni eklenmedi.

## Karar

`JournalEntry` aggregate root, `Posting` immutable value object ve `PostingDirection` enum kullanıyoruz. Pozitif tutar ve açık yön, negatif tutarla yönü ikinci kez ifade etme belirsizliğini önlüyor. En az iki farklı hesap ve tam debit/credit eşitliği factory'de zorunlu. Domain EF veya bir database provider'ına referans vermiyor.

Tutar kapasitesi mevcut `numeric(19,4)` tercihiyle uyumlu tutuluyor. Daha hassas tutarı sessizce yuvarlamak yerine reddediyoruz. Bu başlangıç politikasıdır; çoklu currency/kur ve hesap precision gereksinimlerinde yeniden değerlendirilecek.

Journal tek currency taşıyor; mevcut Currency value object'i tekrar kullanılıyor. Sırf klasör simetrisi için Currency taşınmadı veya generic Money/Entity altyapısı eklenmedi. Hesap türü, hesap varlığı ve currency uyumu bu milestone'un garantisi değil.

## Sonuçlar

Dengesiz bir journal normal oluşturma yolundan üretilemiyor. Koleksiyonun defensive copy'si ve immutable posting'ler dengeyi sonradan bozmaya izin vermiyor. Bir debit birden fazla credit ile dengelenebiliyor.

Bunun bedeli yeni kavramlar ve gelecekte journal/satırları birlikte kaydetme zorunluluğu. Database bütünlüğü, bakiye kontrolü, tekrar işleme ve kalıcı silinmezlik henüz çözülmedi. Denge, tek başına yanlış hesaba ödeme yapılmasını engellemez. Event sourcing veya dağıtık ledger kararı alınmadı.

## Yeniden değerlendirme koşulları

Gerçek deposit/transfer akışı hesap tiplerini ve persistence sınırını gerektirdiğinde modeli genişleteceğiz. Çoklu currency veya daha farklı hassasiyet gerektiğinde tutar politikasını yeniden ele alacağız. Satır sayısı veya yük ölçümleri sorun gösterirse veri erişimini optimize edeceğiz.
