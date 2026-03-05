using Serilog;
using Serilog.Events;
using GeminiDemo.Models;
using GeminiDemo.Services;

// ── Bootstrap-logger – används tills den riktiga loggern är klar ─
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Warning()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // ── Konfigurationskällor (läses i prioritetsordning) ──────────
    //
    //  1. appsettings.json                    (lägst prioritet)
    //  2. appsettings.{Environment}.json
    //  3. User Secrets                        (bara i Development)
    //  4. Miljövariabler  (GEMINI__APIKEY)
    //  5. Kommandoradsargument                (högst prioritet)
    //
    // WebApplication.CreateBuilder() sätter upp alla fem automatiskt.
    // User Secrets kräver att .csproj innehåller <UserSecretsId> –
    // det läggs till av: dotnet user-secrets init --project src/GeminiDemo/

    // ── Serilog – konfigureras efter builder så den når IConfiguration ─
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
        .WriteTo.File("logs/gemini-.log",
            rollingInterval: RollingInterval.Day,
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}"));

    // ── HttpClient – IHttpClientFactory hanterar connection pooling ─
    builder.Services.AddHttpClient<IGeminiService, GeminiService>(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(30);
    });

    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
        c.SwaggerDoc("v1", new() { Title = "GeminiDemo API", Version = "v1" }));

    var app = builder.Build();

    // ── Kontrollera att API-nyckeln finns vid uppstart ────────────
    // Bättre att misslyckas tydligt här än med ett kryptiskt 403/429
    // när man väl gör ett anrop i Swagger.
    var apiKey = app.Configuration["Gemini:ApiKey"];
    if (string.IsNullOrWhiteSpace(apiKey))
    {
        Log.Fatal(
            "Gemini:ApiKey saknas! Kör:\n" +
            "  dotnet user-secrets set \"Gemini:ApiKey\" \"AIza...\" --project src/GeminiDemo/\n" +
            "Skaffa nyckel på: https://aistudio.google.com/apikey");
        return;
    }
    Log.Information("Gemini:ApiKey konfigurerad ({Length} tecken)", apiKey.Length);

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

    // ════════════════════════════════════════════════════════════════
    // ENDPOINT 1: Hälsocheck
    // GET /
    // ════════════════════════════════════════════════════════════════
    app.MapGet("/", () => new
    {
        Status  = "ok",
        Service = "GeminiDemo",
        Docs    = "/swagger"
    });

    // ════════════════════════════════════════════════════════════════
    // ENDPOINT 2: Enkel chat
    // POST /api/chat
    // ════════════════════════════════════════════════════════════════
    app.MapPost("/api/chat", async (
        ChatRequest request,
        IGeminiService gemini,
        ILogger<Program> logger) =>
    {
        if (string.IsNullOrWhiteSpace(request.Message))
            return Results.BadRequest(new { error = "Message får inte vara tom." });

        logger.LogInformation("Chat-request: {Length} tecken", request.Message.Length);

        try
        {
            var response = await gemini.ChatAsync(request.Message, request.SystemPrompt);
            return Results.Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError("Chat misslyckades: {Error}", ex.Message);
            return Results.Problem(ex.Message, statusCode: 502);
        }
    })
    .WithName("Chat")
    .WithSummary("Skicka ett meddelande till Gemini och få svar")
    .WithDescription("""
        Skickar ett meddelande till Google Gemini och returnerar svaret.

        Valfritt: lägg till systemPrompt för att styra modellens beteende.

        Exempel:
        {
          "message": "Förklara vad en REST API är på ett enkelt sätt",
          "systemPrompt": "Du är en lärare som förklarar för gymnasieelever"
        }
        """);

    // ════════════════════════════════════════════════════════════════
    // ENDPOINT 3: Produktbeskrivning
    // POST /api/products/describe
    // ════════════════════════════════════════════════════════════════
    app.MapPost("/api/products/describe", async (
        DescribeRequest request,
        IGeminiService gemini,
        ILogger<Program> logger) =>
    {
        if (string.IsNullOrWhiteSpace(request.ProductName))
            return Results.BadRequest(new { error = "ProductName krävs." });

        if (request.Price <= 0)
            return Results.BadRequest(new { error = "Price måste vara större än 0." });

        try
        {
            var response = await gemini.DescribeProductAsync(
                request.ProductName, request.Price, request.Category);
            return Results.Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError("Describe misslyckades: {Error}", ex.Message);
            return Results.Problem(ex.Message, statusCode: 502);
        }
    })
    .WithName("DescribeProduct")
    .WithSummary("Generera en AI-skriven produktbeskrivning")
    .WithDescription("""
        Skickar produktdata till Gemini och returnerar en säljande beskrivning på svenska.

        Exempel:
        {
          "productName": "Laptop",
          "price": 12999,
          "category": "Elektronik"
        }
        """);

    // ════════════════════════════════════════════════════════════════
    // ENDPOINT 4: Batch (workshop-uppgift – avkommentera för att aktivera)
    // POST /api/products/describe-batch
    // ════════════════════════════════════════════════════════════════
    // app.MapPost("/api/products/describe-batch", async (
    //     List<DescribeRequest> requests,
    //     IGeminiService gemini) =>
    // {
    //     var tasks   = requests.Select(r =>
    //         gemini.DescribeProductAsync(r.ProductName, r.Price, r.Category));
    //     var results = await Task.WhenAll(tasks);
    //     return Results.Ok(results);
    // });

    Log.Information("=== GeminiDemo redo – Swagger: http://localhost:5000/swagger ===");
    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "GeminiDemo kraschade vid uppstart");
}
finally
{
    await Log.CloseAndFlushAsync();
}
