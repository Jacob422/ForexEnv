# Etap 1: Budowanie aplikacji
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Skopiuj csproj i przywróć zależności
COPY ["ForexEnv.csproj", "./"]
RUN dotnet restore "./ForexEnv.csproj"

# Skopiuj resztę plików i zbuduj
COPY . .
RUN dotnet publish "ForexEnv.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Etap 2: Środowisko uruchomieniowe
FROM mcr.microsoft.com/dotnet/runtime:9.0 AS final
WORKDIR /app

# Ustawienie TimeZone (opcjonalnie, domyślnie UTC, dla Forexu może to być przydatne)
# ENV TZ=Europe/Warsaw

COPY --from=build /app/publish .

# Uruchomienie aplikacji
ENTRYPOINT ["dotnet", "ForexEnv.dll"]
