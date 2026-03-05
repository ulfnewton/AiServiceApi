namespace GeminiDemo.Models;

// ── Inkommande requests till vårt API ────────────────────────

public record ChatRequest(string Message, string? SystemPrompt = null);

public record DescribeRequest(string ProductName, decimal Price, string Category);

// ── Gemini API – request-format ───────────────────────────────
// Dokumentation: https://ai.google.dev/api/generate-content

public record GeminiRequest(List<GeminiContent> Contents, GeminiGenerationConfig? GenerationConfig = null);

public record GeminiContent(string Role, List<GeminiPart> Parts);

public record GeminiPart(string Text);

public record GeminiGenerationConfig(
    int MaxOutputTokens = 1024,
    double Temperature = 0.7,
    double TopP = 0.9);

// ── Gemini API – response-format ─────────────────────────────

public record GeminiResponse(
    List<GeminiCandidate>? Candidates,
    GeminiUsageMetadata? UsageMetadata,
    string? Error);

public record GeminiCandidate(GeminiContent Content, string FinishReason);

public record GeminiUsageMetadata(int PromptTokenCount, int CandidatesTokenCount, int TotalTokenCount);

// ── Svar till klienten ────────────────────────────────────────

public record ChatResponse(string Reply, int TokensUsed, double ElapsedMs);

public record DescribeResponse(string ProductName, string Description, int TokensUsed);
