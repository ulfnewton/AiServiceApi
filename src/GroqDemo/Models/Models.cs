namespace GroqDemo.Models;

// ── Inkommande requests till vårt API ────────────────────────

public record ChatRequest(string Message, string? SystemPrompt = null);
public record TranslateRequest(string Text, string TargetLanguage, double Temperature = 0.1);
public record SummarizeRequest(string Text, int MaxSentences = 3);

// ── Groq API – OpenAI-kompatibelt format ─────────────────────
// Groq använder exakt samma format som OpenAI.
// Kod skriven för OpenAI fungerar mot Groq med bara en URL-ändring.

public record GroqRequest(
    string Model,
    List<GroqMessage> Messages,
    int MaxTokens = 1024,
    double Temperature = 0.7);

public record GroqMessage(string Role, string Content);

// ── Groq API – response-format ────────────────────────────────

public record GroqResponse(
    List<GroqChoice>? Choices,
    GroqUsage? Usage,
    string? Model);

public record GroqChoice(GroqMessage Message, string FinishReason);

public record GroqUsage(
    int PromptTokens,
    int CompletionTokens,
    int TotalTokens,
    double TotalTime);

// ── Svar till klienten ────────────────────────────────────────

public record ChatResponse(string Reply, int TokensUsed, double ElapsedMs, string Model);
public record TranslateResponse(string OriginalText, string TranslatedText, string TargetLanguage, int TokensUsed);
public record SummarizeResponse(string Summary, int OriginalLength, int TokensUsed);
