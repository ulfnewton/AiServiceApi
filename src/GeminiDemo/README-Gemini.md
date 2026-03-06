# GeminiDemo

ASP.NET Core 8 Minimal API som kommunicerar med Google Gemini.
Byggd som pedagogisk demo för YH-kursen – molntjänster och HttpClient.

## Kom igång

### 1. Skaffa en gratis API-nyckel

1. Gå till **https://aistudio.google.com/apikey**
2. Klicka **Create API key** → välj **Create API key in new project**
3. Kopiera nyckeln (börjar med `AIza`)

> ⚠️ Välj alltid "in new project" – en nyckel i ett befintligt projekt
> som aldrig använt Gemini API ger `limit: 0` och 429-fel.

### 2. Verifiera nyckeln med curl innan du fortsätter

**Windows (Git Bash / WSL):**
```bash
curl "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash-lite:generateContent" \
  -H "Content-Type: application/json" \
  -H "X-goog-api-key: AIza...din-nyckel..." \
  -X POST \
  -d '{"contents":[{"parts":[{"text":"Säg hej på svenska"}]}]}'
```

Du ska se ett JSON-svar med `"candidates"` och en text. Om du ser ett fel – lös det innan du går vidare.

### 3. Lägg till UserSecretsId i projektet

```bash
dotnet user-secrets init --project src/GeminiDemo/
```

### 4. Lagra nyckeln med User Secrets

```bash
dotnet user-secrets set "Gemini:ApiKey" "AIza...din-nyckel..." --project src/GeminiDemo/
```

Verifiera:
```bash
dotnet user-secrets list --project src/GeminiDemo/
# Gemini:ApiKey = AIza...
```

> ✅ Nyckeln lagras i `%APPDATA%\Microsoft\UserSecrets\` på Windows –
> aldrig i projektkatalogen, aldrig i git.

### 5. Starta projektet

```bash
dotnet run --project src/GeminiDemo/
```

Om nyckeln är korrekt konfigurerad ser du:
```
[INF] Gemini:ApiKey konfigurerad (39 tecken)
[INF] === GeminiDemo redo – Swagger: http://localhost:5000/swagger ===
```

Om nyckeln saknas ser du ett tydligt felmeddelande och appen startar inte.

Öppna **http://localhost:5000/swagger** i webbläsaren.

---

## Endpoints

| Metod | URL | Beskrivning |
|-------|-----|-------------|
| GET  | `/` | Hälsocheck |
| POST | `/api/chat` | Skicka ett meddelande till Gemini |
| POST | `/api/products/describe` | Generera produktbeskrivning |

---

## Exempel – Chat

```json
POST /api/chat
{
  "message": "Förklara vad en REST API är på ett enkelt sätt",
  "systemPrompt": "Du är en lärare som förklarar för gymnasieelever"
}
```

Svar:
```json
{
  "reply": "En REST API är som en servitör på en restaurang...",
  "tokensUsed": 312,
  "elapsedMs": 1843
}
```

---

## Exempel – Produktbeskrivning

```json
POST /api/products/describe
{
  "productName": "Laptop",
  "price": 12999,
  "category": "Elektronik"
}
```

---

## Produktion – miljövariabel

I produktion (t.ex. Azure App Service) sätts nyckeln som miljövariabel.
Dubbelt understreck (`__`) betyder hierarki i ASP.NET Core:

```
GEMINI__APIKEY=AIza...
```

---

## Felsökning

### Appen startar men visar "Gemini:ApiKey saknas"
User Secrets är inte satt. Kör steg 3–4 ovan igen och kontrollera
att `--project`-sökvägen pekar på rätt `.csproj`.

### 429 med `limit: 0` i fellogg
Nyckeln är kopplad till ett projekt utan aktiverat Gemini API,
eller dagsgränsen är förbrukad. Skapa en ny nyckel i ett nytt projekt
på https://aistudio.google.com/apikey och uppdatera User Secret.

### 403 – `PERMISSION_DENIED`
Nyckeln är ogiltig. Skapa en ny.

### 503 – `UNAVAILABLE`
Gemini är tillfälligt överbelastad. Vänta och försök igen.

### curl fungerar men inte Swagger
Kontrollera att curl och appen använder **samma nyckel**.
Kör `dotnet user-secrets list --project src/GeminiDemo/` och
jämför med nyckeln du testade i curl.

### `UserSecretsId` dök upp i git diff på .csproj
Det är ofarligt – det är bara ett GUID som identifierar projektet,
inte nyckeln. Nyckeln lagras aldrig i projektkatalogen.
