# CryptoTrendForge Deployment (Render + Vercel)

## TL;DR

- `CryptoTrendForge.Worker` arka plan servisi oldugu icin **Render Worker** olarak deploy edilir.
- `CryptoTrendForge.Dashboard` Blazor Server oldugu icin stateful SignalR baglantisi ister; bu nedenle **Render Web Service** olarak deploy edilir.
- Vercel bu repo icin dogrudan uygun runtime degildir (Blazor Server + long-running worker). Vercel'i en fazla domain/yonlendirme veya ayrik static frontend icin kullanin.

## Render Dosyalari

Repo icine su dosyalar eklendi:

- `render.yaml`
- `src/CryptoTrendForge.Worker/Dockerfile`
- `src/CryptoTrendForge.Dashboard/Dockerfile`
- `src/CryptoTrendForge.Dashboard/start-dashboard.sh`
- `.dockerignore`

## Render Uzerinde Kurulum

1. Render'da **Blueprint** ile bu repoyu bagla (render.yaml otomatik okunur).
2. Su environment variable'lari doldur:

### Ortak

- `ConnectionStrings__DefaultConnection` -> PostgreSQL baglanti metni

### Worker

- `Telegram__BotToken`
- `Telegram__ChatId`
- `Telegram__AdminChatId` (opsiyonel)
- `Telegram__AdminAlertsEnabled` (`true`/`false`)

### Dashboard

- `DashboardAuth__Username`
- `DashboardAuth__Password`

3. Deploy sonrasi:
   - Dashboard URL aciliyor olmali
   - Worker loglarinda scan dongusu gorunmeli

## Vercel Notu (Onemli)

Bu repo'nun mevcut mimarisi (Blazor Server + BackgroundService worker) Vercel'in serverless modeline dogrudan uygun degildir.

- Worker Vercel'de 7/24 calisamaz
- Blazor Server icin gereken stateful SignalR baglantisi Vercel'de stabil hedef degildir

Bu nedenle production runtime olarak Render kullanilmali.

## Saglik Kontrolu

Deploy sonrasi asagidakileri kontrol et:

1. `coins` tablosunda aktif coin var mi?
2. Worker logunda scan ve score satirlari geliyor mu?
3. Telegram sinyali aliniyor mu?
4. Dashboard `Signals` ve `Performance` sayfalari veri cekiyor mu?
