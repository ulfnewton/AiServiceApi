# GroqDemo

ASP.NET Core 8 Minimal API som kör Meta Llama 3.3 (70B) via Groqs inference-API.
Byggd som pedagogisk demo för YH-kursen – molntjänster och HttpClient.

## Vad är Groq?

Groq är ett företag som bygger specialiserad hårdvara (LPU – Language Processing Unit)
optimerad för att köra stora språkmodeller. De erbjuder ett gratis API mot open source-modeller
som Meta Llama, Googles Gemma och Mistral.

**Skillnad mot Gemini:**
- Gemini: Googles egna proprietära modell, stängd källkod
- Llama 3.3: Metas öppna modell, källkoden är publik
- Groq: Infrastrukturen som kör Llama – inte modellen i sig

**API-formatet:**
Groq använder exakt samma JSON-format som OpenAI. Det innebär att kod skriven
för OpenAI fungerar mot Groq med bara en URL-ändring – och vice versa.

## Kom igång

### 1. Skaffa en gratis API-nyckel

1. Gå till **https://console.groq.com/keys**
2. Logga in (Google-konto fungerar)
3. Klicka **Create API Key**
4. Kopiera nyckeln – den börjar med `gsk_`

> Inget kreditkort krävs. Inga EU-restriktioner.

### 2. Verifiera nyckeln med curl

```bash
curl "https://api.groq.com/openai/v1/chat/completions" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer gsk_...din-nyckel..." \
  -X POST \
  -d '{
    "model": "llama-3.3-70b-versatile",
    "messages": [{"role": "user", "content": "Säg hej på svenska"}]
  }'
```

Du ska se ett svar med `"choices"` och en text.

### 3. Lägg till UserSecretsId och lagra nyckeln

```bash
dotnet user-secrets init --project src/GroqDemo/
dotnet user-secrets set "Groq:ApiKey" "gsk_...din-nyckel..." --project src/GroqDemo/
```

Verifiera:
```bash
dotnet user-secrets list --project src/GroqDemo/
# Groq:ApiKey = gsk_...
```

### 4. Starta projektet

```bash
dotnet run --project src/GroqDemo/
```

Öppna Swagger: **http://localhost:5001/swagger**

> Port 5001 – kör GeminiDemo (5000) och GroqDemo (5001) parallellt!

---

## Endpoints

| Metod | URL | Beskrivning |
|-------|-----|-------------|
| GET  | `/` | Hälsocheck |
| POST | `/api/chat` | Konversation med Llama 3.3 |
| POST | `/api/translate` | Översätt till valfritt språk |
| POST | `/api/summarize` | Sammanfatta en text |

---

## Exempel – Chat

```json
POST /api/chat
{
  "message": "Förklara skillnaden mellan öppen och proprietär AI-modell",
  "systemPrompt": "Du är en lärare som förklarar för YH-studenter på svenska"
}
```

Svar:
```json
{
  "reply": "En öppen modell...",
  "tokensUsed": 287,
  "elapsedMs": 412,
  "model": "llama-3.3-70b-versatile"
}
```

---

## Exempel – Översättning

```json
POST /api/translate
{
  "text": "Det regnar ute och katten sitter i fönstret.",
  "targetLanguage": "japanska",
  "temperature": 0.1
}
```

---

## Produktion – miljövariabel

```
GROQ__APIKEY=gsk_...
```

---

## Felsökning

### 401 – Unauthorized
Nyckeln är ogiltig. Kontrollera att den börjar med `gsk_` och är korrekt kopierad.

### 429 – Too Many Requests
Gratis-tieren har per-minut-gränser. Vänta några sekunder.

### Appen startar men visar "Groq:ApiKey saknas"
Kör `dotnet user-secrets list --project src/GroqDemo/` och kontrollera att nyckeln finns.
