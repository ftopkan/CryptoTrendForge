# CryptoTrendForge

Bybit USDT perpetual futures market scanner that detects LONG setups, sends Telegram alerts, and exposes signal history through a Blazor dashboard.

## Repository

https://github.com/ftopkan/CryptoTrendForge

## Connect To Render (recommended runtime)

Render runs both services from `render.yaml`:

- `cryptotrendforge-worker` (background worker)
- `cryptotrendforge-dashboard` (web service)

### One-click connect

[![Deploy to Render](https://render.com/images/deploy-to-render-button.svg)](https://dashboard.render.com/blueprint/new?repo=https://github.com/ftopkan/CryptoTrendForge)

### Steps

1. Open the button/link above.
2. Sign in with GitHub and authorize Render if asked.
3. Confirm the repo `ftopkan/CryptoTrendForge`.
4. Fill required env vars:
   - `ConnectionStrings__DefaultConnection`
   - `Telegram__BotToken`
   - `Telegram__ChatId`
   - `DashboardAuth__Username`
   - `DashboardAuth__Password`
5. Deploy Blueprint.

## Connect To Vercel (optional)

Vercel can import this repo, but the current architecture is optimized for Render because:

- Worker needs a long-running background process
- Dashboard is Blazor Server (stateful SignalR)

Use Vercel mainly if you want repo linkage, preview workflow, or future frontend split.

### Import link

https://vercel.com/new/import?s=https://github.com/ftopkan/CryptoTrendForge

### Suggested Vercel import settings

- Project Name: `cryptotrendforge-dashboard`
- Root Directory: repository root
- Framework Preset: `Other`
- Build Command:
  `dotnet publish src/CryptoTrendForge.Dashboard/CryptoTrendForge.Dashboard.csproj -c Release -o .vercel/output`
- Output Directory: `.vercel/output`

If deploy fails on Vercel, keep production on Render and use Vercel only after a frontend split.

## Local Run

```bash
dotnet build
cd src/CryptoTrendForge.Worker
dotnet run
```

Dashboard:

```bash
cd src/CryptoTrendForge.Dashboard
dotnet run
```

## Docs

- `docs/implementation-phases.md`
- `docs/deployment-render-vercel.md`
