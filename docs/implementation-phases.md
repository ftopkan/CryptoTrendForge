# CryptoTrendForge - Fazlandirilmis Uygulama Yol Haritasi

Bu belge, uzun planin gelistirme sirasina donusturulmus halidir. Her faz bir onceki fazin ciktisina dayanir; bu nedenle siralama kritik kabul edilir.

## Faz 1 - Temel Altyapi ve Dayaniklilik

### Hedef
Teknik analiz ve sinyal motoruna gecmeden once, projeyi calisabilir, test edilebilir ve stabil bir temel uzerine oturtmak.

### Kapsam
- Solution iskeleti: `Core`, `Worker`, `Dashboard`, `Tests`
- Domain modelleri, enumlar, `AppDbContext`, ilk migration
- `BotOptions` ve `BybitOptions` ayar nesneleri
- `BybitHttpClient` (retry, 429 handling, circuit breaker)
- `TechnicalAnalysisService` ilk surum (RSI, EMA)
- Bu servisler icin unit test temeli

### Cikis Kriterleri
- Uygulama acilisinda kritik config hatalari fail-fast yakalanmali
- DB migration acilista otomatik uygulanmali
- RSI/EMA hesaplari unit testlerden gecmeli
- Faz 2 icin veri toplama katmani hazir olmali

### Durum
- [x] Proje iskeleti, domain, migration
- [x] Baslangic teknik analiz servisleri
- [x] Bybit istemcisi temel dayaniklilik
- [x] Options validator + startup migration + test genisletmesi

## Faz 2 - Veri Katmani

### Hedef
Bybit REST verisini ve cache katmanini tek merkezden yonetmek.

### Kapsam
- `BybitService`: kline, open interest, funding, ticker endpointleri
- `MarketDataService`: TTL kurallariyla cache orkestrasyonu
- `MarketSnapshot` ve `BtcSnapshot` modelleri
- Coin listesini her scan turunda DB'den taze cekme

## Faz 3 - Skorlama ve Risk Filtreleri

### Hedef
Long setup degerlendirmesini deterministic ve izlenebilir hale getirmek.

### Kapsam
- `TechnicalAnalysisService` genisletmesi (swing low, volume sentiment, pattern)
- `SignalEngine`: base score + pattern bonus + breakdown/reasons/risks
- `RiskFilterService`: hard filterlar
- `BtcRegimeService`: `RISK_ON/NEUTRAL/RISK_OFF`

## Faz 4 - Signal Lifecycle

### Hedef
Sinyal kaydi, cooldown, duplicate ve outcome snapshot akislarini tamamlamak.

### Kapsam
- `SignalRepository`
- `SignalScanWorker`
- `SignalOutcomeWorker`
- state gecisleri: `PENDING -> ACTIVE -> EXPIRED/INVALIDATED/SUPERSEDED`

## Faz 5 - Telegram Uyarilari

### Hedef
Islenebilir ve acik mesaj formatiyla sinyali operasyona tasimak.

### Kapsam
- `TelegramService`
- Base score ve pattern bonusu ayri gosteren mesaj sablonu
- reasons/risks dinamik bloklari
- hata loglama ve retry/fallback stratejisi

## Faz 6 - Dashboard (Read-Only)

### Hedef
Sinyal gecmisi ve performans analizini operasyonel gorunurluge cevirmek.

### Kapsam
- Blazor sayfalari: `Signals`, `SignalDetail`, `Performance`
- `DashboardDataService` (yalnizca read-only sorgular)
- Pattern bonusu olan/olmayan sinyal basari karsilastirmasi
- regime ve skor araligi bazli performans kirilimlari
