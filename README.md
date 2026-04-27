# ForexEnv 📈🤖

![.NET](https://img.shields.io/badge/.NET-9.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)
![PostgreSQL](https://img.shields.io/badge/PostgreSQL-316192?style=for-the-badge&logo=postgresql&logoColor=white)
![Telegram](https://img.shields.io/badge/Telegram-2CA5E0?style=for-the-badge&logo=telegram&logoColor=white)
![Railway](https://img.shields.io/badge/Railway-131415?style=for-the-badge&logo=railway&logoColor=white)
![Entity Framework Core](https://img.shields.io/badge/EF_Core-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)

**ForexEnv** to zaawansowany symulator rynku Forex działający jako Bot na komunikatorze Telegram. Umożliwia użytkownikom obrót wirtualnymi środkami, dając im 10 000 PLN na start do handlu światowymi walutami na podstawie rzeczywistych, bieżących kursów pobieranych z Narodowego Banku Polskiego (NBP).

Celem projektu jest demonstracja umiejętności w zakresie budowy systemów rozproszonych, asynchronicznej komunikacji z zewnętrznym API, użycia relacyjnych baz danych (PostgreSQL) poprzez ORM (Entity Framework Core) oraz automatyzacji wdrożeń CI/CD (Docker, Railway).

## ✨ Główne Funkcjonalności

- **Wirtualne Portfele:** Każdy użytkownik może posiadać wiele portfeli, z przypisanym balansem początkowym (10 000 PLN).
- **Rzeczywiste Kursy Walut:** Integracja z otwartym API NBP. Obsługa walut takich jak: USD, EUR, GBP, CHF, JPY, CAD, AUD, SEK.
- **Transakcje:** Możliwość kupna i sprzedaży walut po kursach na żywo z uwzględnieniem walidacji biznesowej (brak ujemnego salda).
- **Historia Transakcji:** Pełen zapis logów operacyjnych z możliwością przeglądu do tyłu.
- **Analityka i Wykresy:** Obliczanie PnL (Zysków/Strat) dla poszczególnych portfeli oraz generowanie wykresów struktury portfela (QuickChart API).

## 🏛️ Architektura i Technologie

Projekt wykorzystuje nowoczesny stos technologiczny oparty o ekosystem .NET:

- **Język:** C# 13, .NET 9.0
- **Baza Danych:** PostgreSQL (na produkcji), Entity Framework Core (Code-First)
- **Klient API:** HTTPClient, Newtonsoft.Json (dla API NBP)
- **Komunikacja:** Telegram.Bot API (działający w trybie Long Polling)
- **Wdrożenie:** Aplikacja jest w pełni skonteneryzowana przy użyciu **Docker** i wdrożona na platformie chmurowej **Railway**.
- **Wzorce Projektowe:** Asynchroniczność (TPL), Inversion of Control (częściowo) oraz podejście Domain-Driven do projektowania modeli (User -> Wallet -> Transaction).

## 🚀 Uruchomienie Projektu Lokalnie

Aby uruchomić bota na swoim komputerze, będziesz potrzebował zainstalowanego .NET 9 SDK oraz bazy PostgreSQL (lokalnie lub w chmurze).

### 1. Klonowanie repozytorium
```bash
git clone https://github.com/TwojaNazwa/ForexEnv.git
cd ForexEnv
```

### 2. Zmienne środowiskowe
Zanim uruchomisz aplikację, upewnij się, że posiadasz token swojego Bota (od @BotFather) i wpisz go do systemu jako zmienną środowiskową:
*W systemie Linux/macOS:*
```bash
export TELEGRAM_BOT_TOKEN="twój_token"
export DATABASE_URL="postgresql://postgres:haslo@localhost:5432/forexenv"
```
*W systemie Windows (PowerShell):*
```powershell
$env:TELEGRAM_BOT_TOKEN="twój_token"
$env:DATABASE_URL="postgresql://postgres:haslo@localhost:5432/forexenv"
```

### 3. Migracja bazy danych
Aplikacja jest skonfigurowana tak, aby zaktualizować bazę danych przy uruchomieniu (`db.Database.MigrateAsync()`).

### 4. Uruchomienie aplikacji
```bash
dotnet run
```
Bot jest teraz gotowy i oczekuje na komendę `/start` na Telegramie.

## 📦 Deployment (Docker / Railway)

Projekt jest przystosowany do ciągłego wdrażania (Continuous Deployment). 
1. Wypchnięcie zmian do głównej gałęzi (main/master) automatycznie aktywuje build na Railway.
2. Railway wykorzystuje zamieszczony plik `Dockerfile`, aby zbudować obraz oparty o `mcr.microsoft.com/dotnet/sdk:9.0` i uruchomić w lekkim środowisku uruchomieniowym `runtime:9.0`.
3. Zmienne konfiguracyjne zarządzane są poprzez mechanizm Railway Environment Variables, co gwarantuje bezpieczeństwo tajnych kluczy.

## 📝 Licencja
Projekt udostępniony na licencji MIT. 
