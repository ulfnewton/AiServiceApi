using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GroqDemo.Models;

namespace GroqDemo.Services;

public interface IGroqService
{
    Task<ChatResponse> ChatAsync(string message, string? systemPrompt = null);
    Task<TranslateResponse> TranslateAsync(string text, string targetLanguage, double temperature = 0.1);
    Task<SummarizeResponse> SummarizeAsync(string text, int maxSentences = 3);
}

public class GroqService(
    HttpClient httpClient,
    IConfiguration config,
    ILogger<GroqService> logger) : IGroqService
{
    // ── Modell ────────────────────────────────────────────────
    // Groq kör open source-modeller med extremt låg latens (< 300ms).
    // llama-3.3-70b-versatile: Metas Llama 3.3, 70 miljarder parametrar
    //   – bra balans mellan kvalitet och hastighet
    // Andra tillgängliga modeller på console.groq.com/docs/models:
    //   llama-3.1-8b-instant   – snabbast, enklare uppgifter
    //   mixtral-8x7b-32768     – bra för långa texter
    //   gemma2-9b-it           – Googles Gemma 2, öppen modell
    private const string Model = "llama-3.3-70b-versatile";

    // Groq använder OpenAI-kompatibelt API-format.
    // Samma JSON-struktur som OpenAI, bara URL och API-nyckel skiljer.
    private const string BaseUrl = "https://api.groq.com/openai/v1/chat/completions";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    // ── Chat ──────────────────────────────────────────────────

    public async Task<ChatResponse> ChatAsync(string message, string? systemPrompt = null)
    {
        logger.LogInformation("Groq chat: {Length} tecken", message.Length);
        var sw = Stopwatch.StartNew();

        var messages = new List<GroqMessage>();

        // Groq stöder "system"-rollen direkt – renare än Geminis workaround
        if (!string.IsNullOrWhiteSpace(systemPrompt))
            messages.Add(new GroqMessage("system", systemPrompt));

        messages.Add(new GroqMessage("user", message));

        var request = new GroqRequest(Model, messages, MaxTokens: 2048, Temperature: 0.7);
        var response = await CallGroqAsync(request);
        sw.Stop();

        var reply  = ExtractText(response);
        var tokens = response.Usage?.TotalTokens ?? 0;

        logger.LogInformation(
            "Groq svarade på {Elapsed:0}ms, {Tokens} tokens, modell: {Model}",
            sw.ElapsedMilliseconds, tokens, response.Model ?? Model);

        return new ChatResponse(reply, tokens, sw.ElapsedMilliseconds, response.Model ?? Model);
    }

    // ── Översättning ──────────────────────────────────────────

    public async Task<TranslateResponse> TranslateAsync(
        string text, string targetLanguage, double temperature = 0.1)
    {
        logger.LogInformation(
            "Översätter {Length} tecken till {Language}", text.Length, targetLanguage);

        var messages = new List<GroqMessage>
        {
            new("system",
                $"Du är en professionell översättare. " +
                $"Översätt text till {targetLanguage}. " +
                $"Svara BARA med den översatta texten – ingen förklaring, ingen rubrik."),
            new("user", text)
        };

        var request  = new GroqRequest(Model, messages, MaxTokens: 1024, Temperature: temperature);
        var response = await CallGroqAsync(request);
        var tokens   = response.Usage?.TotalTokens ?? 0;

        logger.LogInformation("Översättning klar: {Tokens} tokens", tokens);

        return new TranslateResponse(text, ExtractText(response), targetLanguage, tokens);
    }

    // ── Sammanfattning ────────────────────────────────────────

    public async Task<SummarizeResponse> SummarizeAsync(string text, int maxSentences = 3)
    {
        logger.LogInformation(
            "Sammanfattar {Length} tecken i max {Sentences} meningar",
            text.Length, maxSentences);

        var messages = new List<GroqMessage>
        {
            new("system",
                $"Du är en expert på att sammanfatta text. " +
                $"Skriv en sammanfattning på svenska i max {maxSentences} meningar. " +
                $"Svara BARA med sammanfattningen – ingen rubrik eller förklaring."),
            new("user", text)
        };

        var request  = new GroqRequest(Model, messages, MaxTokens: 512, Temperature: 0.3);
        var response = await CallGroqAsync(request);
        var tokens   = response.Usage?.TotalTokens ?? 0;

        logger.LogInformation("Sammanfattning klar: {Tokens} tokens", tokens);

        return new SummarizeResponse(ExtractText(response), text.Length, tokens);
    }

    // ── HTTP-anrop mot Groq API ───────────────────────────────

    private async Task<GroqResponse> CallGroqAsync(GroqRequest request)
    {
        // API-nyckel från IConfiguration
        // Lokalt:     dotnet user-secrets set "Groq:ApiKey" "gsk_..."
        // Produktion: miljövariabel GROQ__APIKEY
        var apiKey = config["Groq:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException(
                "Groq:ApiKey saknas. Kör:\n" +
                "  dotnet user-secrets set \"Groq:ApiKey\" \"gsk_...\" --project src/GroqDemo/\n" +
                "Skaffa nyckel på: https://console.groq.com/keys");

        // Groq använder Bearer-autentisering i Authorization-headern
        // – inte API-nyckel i URL som Gemini
        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", apiKey);

        var json = JsonSerializer.Serialize(request, JsonOptions);
        var body = new StringContent(json, Encoding.UTF8, "application/json");

        logger.LogDebug("POST {Url} ({Bytes} bytes)", BaseUrl, json.Length);

        HttpResponseMessage httpResponse;
        try
        {
            httpResponse = await httpClient.PostAsync(BaseUrl, body);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError("Nätverksfel mot Groq API: {Error}", ex.Message);
            throw new InvalidOperationException("Kunde inte nå Groq API. Kontrollera nätverket.", ex);
        }

        var responseJson = await httpResponse.Content.ReadAsStringAsync();

        if (!httpResponse.IsSuccessStatusCode)
        {
            logger.LogError(
                "Groq API svarade {StatusCode}: {Body}",
                (int)httpResponse.StatusCode, responseJson);

            var hint = (int)httpResponse.StatusCode switch
            {
                401 => " – API-nyckeln är ogiltig. Kontrollera att den börjar med 'gsk_'.",
                429 => " – För många anrop. Vänta några sekunder.",
                503 => " – Groq är tillfälligt otillgänglig. Försök igen.",
                _   => string.Empty
            };

            throw new InvalidOperationException(
                $"Groq API-fel {(int)httpResponse.StatusCode}{hint} Se loggar för detaljer.");
        }

        return JsonSerializer.Deserialize<GroqResponse>(responseJson, JsonOptions)
            ?? throw new InvalidOperationException("Kunde inte deserialisera Groq-svar.");
    }

    // ── Plocka ut texten ──────────────────────────────────────

    private static string ExtractText(GroqResponse response)
    {
        var text = response.Choices
            ?.FirstOrDefault()
            ?.Message
            ?.Content;

        return text ?? "(inget svar från modellen)";
    }
}
