FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY CryptoTrendForge.sln ./
COPY src/CryptoTrendForge.Core/CryptoTrendForge.Core.csproj src/CryptoTrendForge.Core/
COPY src/CryptoTrendForge.Dashboard/CryptoTrendForge.Dashboard.csproj src/CryptoTrendForge.Dashboard/
RUN dotnet restore src/CryptoTrendForge.Dashboard/CryptoTrendForge.Dashboard.csproj

COPY . .
RUN dotnet publish src/CryptoTrendForge.Dashboard/CryptoTrendForge.Dashboard.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
COPY src/CryptoTrendForge.Dashboard/start-dashboard.sh ./start-dashboard.sh
RUN chmod +x ./start-dashboard.sh

ENV ASPNETCORE_ENVIRONMENT=Production
ENTRYPOINT ["./start-dashboard.sh"]
