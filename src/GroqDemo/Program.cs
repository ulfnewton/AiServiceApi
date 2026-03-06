using Serilog;
using Serilog.Events;
using GroqDemo.Models;
using GroqDemo.Services;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Warning()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // ── Konfigurationskällor (läses i prioritetsordning) ──────
    //  1. appsettings.json            (lägst)
    //  2. appsettings.{Environment}.json
    //  3. User Secrets                (Development)
    //  4. Miljövariabler  GROQ__APIKEY
    //  5. Kommandoradsargument        (högst)

    builder.Host.UseSerilog((context, _, config) => config
        .MinimumLevel.Debug()
        .MinimumLevel.Override("Microsoft",                  LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
        .MinimumLevel.Override("Microsoft.AspNetCore",       LogEventLevel.Warning)
        .Enrich.FromLogContext()
        .Enrich.WithMachineName()
        .Enrich.WithEnvironmentName()
        .ReadFrom.Configuration(context.Configuration)
        .WriteTo.Console(
            outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
        .WriteTo.File("logs/groq-.log",
            rollingInterval: RollingInterval.Day,
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}"));

    builder.Services.AddHttpClient<IGroqService, GroqService>(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(30);
    });

    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
        c.SwaggerDoc("v1", new() { Title = "GroqDemo API – Llama 3.3", Version = "v1" }));

    var app = builder.Build();

    // ── Kontrollera API-nyckel vid uppstart ───────────────────
    var apiKey = app.Configuration["Groq:ApiKey"];
    if (string.IsNullOrWhiteSpace(apiKey))
    {
        Log.Fatal(
            "Groq:ApiKey saknas!\n" +
            "  dotnet user-secrets set \"Groq:ApiKey\" \"gsk_...\" --project src/GroqDemo/\n" +
            "Skaffa nyckel (gratis): https://console.groq.com/keys");
        return;
    }
    Log.Information("Groq:ApiKey konfigurerad ({Length} tecken)", apiKey.Length);

    app.UseSerilogRequestLogging(opts =>
    {
        opts.MessageTemplate = "HTTP {RequestMethod} {RequestPath} → {StatusCode} ({Elapsed:0.0}ms)";
        opts.GetLevel = (ctx, _, ex) =>
        {
            if (ex is not null) return LogEventLevel.Error;
            if (ctx.Response.StatusCode >= 400) return LogEventLevel.Warning;
            if (ctx.Request.Path.StartsWithSegments("/swagger")) return LogEventLevel.Verbose;
            return LogEventLevel.Information;
        };
    });

    app.UseSwagger();
    app.UseSwaggerUI();

    // ════════════════════════════════════════════════════════
    // GET /  – hälsocheck
    // ════════════════════════════════════════════════════════
    app.MapGet("/", () => new
    {
        Status  = "ok",
        Service = "GroqDemo",
        Model   = "llama-3.3-70b-versatile",
        Docs    = "/swagger"
    });

    // ════════════════════════════════════════════════════════
    // POST /api/chat  – enkel konversation med Llama 3.3
    // ════════════════════════════════════════════════════════
    app.MapPost("/api/chat", async (
        ChatRequest request,
        IGroqService groq,
        ILogger<Program> logger) =>
    {
        if (string.IsNullOrWhiteSpace(request.Message))
            return Results.BadRequest(new { error = "Message får inte vara tom." });

        logger.LogInformation("Chat-request: {Length} tecken", request.Message.Length);

        try
        {
            var response = await groq.ChatAsync(request.Message, request.SystemPrompt);
            return Results.Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError("Chat misslyckades: {Error}", ex.Message);
            return Results.Problem(ex.Message, statusCode: 502);
        }
    })
    .WithName("Chat")
    .WithSummary("Skicka ett meddelande till Llama 3.3 via Groq")
    .WithDescription("""
        Anropar Meta Llama 3.3 (70B) via Groqs inference-API.
        Groq kör modellen på specialiserad hårdvara (LPU) – typisk svarstid < 500ms.

        Exempel:
        {
          "message": "Förklara skillnaden mellan öppen och proprietär AI-modell",
          "systemPrompt": "Du är en lärare som förklarar för YH-studenter"
        }
        """);

    // ════════════════════════════════════════════════════════
    // POST /api/translate  – översättning
    // ════════════════════════════════════════════════════════
    app.MapPost("/api/translate", async (
        TranslateRequest request,
        IGroqService groq,
        ILogger<Program> logger) =>
    {
        if (string.IsNullOrWhiteSpace(request.Text))
            return Results.BadRequest(new { error = "Text får inte vara tom." });

        if (string.IsNullOrWhiteSpace(request.TargetLanguage))
            return Results.BadRequest(new { error = "TargetLanguage krävs." });

        if (request.Temperature is < 0 or > 1)
            return Results.BadRequest(new { error = "Temperature måste vara mellan 0.0 och 1.0." });

        try
        {
            var response = await groq.TranslateAsync(
                request.Text, request.TargetLanguage, request.Temperature);
            return Results.Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError("Translate misslyckades: {Error}", ex.Message);
            return Results.Problem(ex.Message, statusCode: 502);
        }
    })
    .WithName("Translate")
    .WithSummary("Översätt text till valfritt språk")
    .WithDescription("""
        Översätter text till angivet målspråk med Llama 3.3.
        Använd låg temperature (0.0–0.2) för konsekvent, exakt översättning.

        Exempel:
        {
          "text": "Det regnar ute och katten sitter i fönstret.",
          "targetLanguage": "engelska",
          "temperature": 0.1
        }
        """);

    // ════════════════════════════════════════════════════════
    // POST /api/summarize  – sammanfattning
    // ════════════════════════════════════════════════════════
    app.MapPost("/api/summarize", async (
        SummarizeRequest request,
        IGroqService groq,
        ILogger<Program> logger) =>
    {
        if (string.IsNullOrWhiteSpace(request.Text))
            return Results.BadRequest(new { error = "Text får inte vara tom." });

        if (request.MaxSentences is < 1 or > 10)
            return Results.BadRequest(new { error = "MaxSentences måste vara mellan 1 och 10." });

        try
        {
            var response = await groq.SummarizeAsync(request.Text, request.MaxSentences);
            return Results.Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError("Summarize misslyckades: {Error}", ex.Message);
            return Results.Problem(ex.Message, statusCode: 502);
        }
    })
    .WithName("Summarize")
    .WithSummary("Sammanfatta en lång text")
    .WithDescription("""
        Sammanfattar en text i ett angivet antal meningar.

        Exempel:
        {
          "text": "Klistra in valfri lång text här...",
          "maxSentences": 3
        }
        """);

    Log.Information("=== GroqDemo redo – Swagger: http://localhost:5001/swagger ===");
    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "GroqDemo kraschade vid uppstart");
}
finally
{
    await Log.CloseAndFlushAsync();
}
