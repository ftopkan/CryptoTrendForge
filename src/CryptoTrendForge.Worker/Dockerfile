FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY CryptoTrendForge.sln ./
COPY src/CryptoTrendForge.Core/CryptoTrendForge.Core.csproj src/CryptoTrendForge.Core/
COPY src/CryptoTrendForge.Worker/CryptoTrendForge.Worker.csproj src/CryptoTrendForge.Worker/
RUN dotnet restore src/CryptoTrendForge.Worker/CryptoTrendForge.Worker.csproj

COPY . .
RUN dotnet publish src/CryptoTrendForge.Worker/CryptoTrendForge.Worker.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENV DOTNET_ENVIRONMENT=Production
ENTRYPOINT ["dotnet", "CryptoTrendForge.Worker.dll"]
