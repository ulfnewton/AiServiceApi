using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GeminiDemo.Models;

namespace GeminiDemo.Services;

public interface IGeminiService
{
    Task<ChatResponse> ChatAsync(string message, string? systemPrompt = null);
    Task<DescribeResponse> DescribeProductAsync(string name, decimal price, string category);
}

public class GeminiService(
    HttpClient httpClient,
    IConfiguration config,
    ILogger<GeminiService> logger) : IGeminiService
{
    // ── Modell ────────────────────────────────────────────────
    // gemini-2.5-flash-lite: snabbast och billigast på gratistieren
    // Byt till "gemini-2.5-flash" om du vill ha längre/bättre svar
    // Alla modeller: https://ai.google.dev/gemini-api/docs/models
    private const string Model = "gemini-2.5-flash-lite";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    // ── Enkel chat-metod ──────────────────────────────────────

    public async Task<ChatResponse> ChatAsync(string message, string? systemPrompt = null)
    {
        logger.LogInformation("Gemini chat: {Length} tecken", message.Length);
        var sw = Stopwatch.StartNew();

        var contents = new List<GeminiContent>();

        if (!string.IsNullOrWhiteSpace(systemPrompt))
            contents.Add(new GeminiContent("user", [new GeminiPart($"[System]: {systemPrompt}")]));

        contents.Add(new GeminiContent("user", [new GeminiPart(message)]));

        var geminiRequest = new GeminiRequest(contents,
            new GeminiGenerationConfig(MaxOutputTokens: 2048, Temperature: 0.7));

        var response = await CallGeminiAsync(geminiRequest);
        sw.Stop();

        var reply  = ExtractText(response);
        var tokens = response.UsageMetadata?.TotalTokenCount ?? 0;

        logger.LogInformation(
            "Gemini svarade på {Elapsed:0}ms, {Tokens} tokens",
            sw.ElapsedMilliseconds, tokens);

        return new ChatResponse(reply, tokens, sw.ElapsedMilliseconds);
    }

    // ── Produktbeskrivning ────────────────────────────────────

    public async Task<DescribeResponse> DescribeProductAsync(
        string name, decimal price, string category)
    {
        logger.LogInformation(
            "Genererar beskrivning för {Product} ({Category}, {Price} kr)",
            name, category, price);

        var prompt = $"""
            Du är en erfaren copywriter för en svensk e-handelssajt.
            Skriv en säljande produktbeskrivning på svenska, max 3 meningar.

            Produkt:  {name}
            Kategori: {category}
            Pris:     {price:N0} kr

            Var entusiastisk men trovärdig. Nämn gärna vad produkten lämpar sig för.
            Svara BARA med beskrivningstexten, ingen rubrik eller förklaring.
            """;

        var contents = new List<GeminiContent>
        {
            new("user", [new GeminiPart(prompt)])
        };

        var geminiRequest = new GeminiRequest(contents,
            new GeminiGenerationConfig(MaxOutputTokens: 256, Temperature: 0.8));

        var response = await CallGeminiAsync(geminiRequest);
        var text     = ExtractText(response);
        var tokens   = response.UsageMetadata?.TotalTokenCount ?? 0;

        logger.LogInformation("Beskrivning klar: {Tokens} tokens", tokens);

        return new DescribeResponse(name, text, tokens);
    }

    // ── HTTP-anrop mot Gemini API ─────────────────────────────

    private async Task<GeminiResponse> CallGeminiAsync(GeminiRequest request)
    {
        var apiKey = config["Gemini:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException(
                "Gemini:ApiKey saknas. Kör:\n" +
                "  dotnet user-secrets set \"Gemini:ApiKey\" \"AIza...\" --project src/GeminiDemo/\n" +
                "Skaffa nyckel på: https://aistudio.google.com/apikey");

        var url  = $"https://generativelanguage.googleapis.com/v1beta/models/{Model}:generateContent?key={apiKey}";
        var json = JsonSerializer.Serialize(request, JsonOptions);
        var body = new StringContent(json, Encoding.UTF8, "application/json");

        // Logga URL utan nyckeln – nyckeln ska aldrig synas i loggar
        logger.LogDebug("POST {Url} ({Bytes} bytes)", url.Split('?')[0], json.Length);

        HttpResponseMessage httpResponse;
        try
        {
            httpResponse = await httpClient.PostAsync(url, body);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError("Nätverksfel mot Gemini API: {Error}", ex.Message);
            throw new InvalidOperationException("Kunde inte nå Gemini API. Kontrollera nätverket.", ex);
        }

        var responseJson = await httpResponse.Content.ReadAsStringAsync();

        if (!httpResponse.IsSuccessStatusCode)
        {
            logger.LogError(
                "Gemini API svarade {StatusCode}: {Body}",
                (int)httpResponse.StatusCode, responseJson);

            var hint = (int)httpResponse.StatusCode switch
            {
                403 => " – Nyckeln är ogiltig eller saknar behörighet.",
                429 => " – Kvoten slut eller för många anrop. Se 'limit: 0' i loggen? Skapa ny nyckel på aistudio.google.com/apikey.",
                503 => " – Gemini är tillfälligt otillgänglig. Vänta och försök igen.",
                _   => string.Empty
            };

            throw new InvalidOperationException(
                $"Gemini API-fel {(int)httpResponse.StatusCode}{hint} Se loggar för detaljer.");
        }

        return JsonSerializer.Deserialize<GeminiResponse>(responseJson, JsonOptions)
            ?? throw new InvalidOperationException("Kunde inte deserialisera Gemini-svar.");
    }

    // ── Plocka ut texten ur response-strukturen ───────────────

    private static string ExtractText(GeminiResponse response)
    {
        var text = response.Candidates
            ?.FirstOrDefault()
            ?.Content
            ?.Parts
            ?.FirstOrDefault()
            ?.Text;

        return text ?? "(inget svar från modellen)";
    }
}
