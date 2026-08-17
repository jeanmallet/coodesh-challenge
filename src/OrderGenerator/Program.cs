using QuickFix;
using QuickFix.Store;
using OrderGenerator;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<GeneratorApp>();
builder.Services.AddHealthChecks().AddCheck<FixSessionHealthCheck>("fix-session");
var app = builder.Build();

// Initiator FIX 4.4 embutido no processo web.
// Config referencia FIX44.xml por caminho relativo; ancora o cwd no dir do binário.
Directory.SetCurrentDirectory(AppContext.BaseDirectory);
var settings = new SessionSettings(Path.Combine(AppContext.BaseDirectory, "generator.cfg"));

// Sobrescreve o alvo FIX quando configurado (ex.: OrderGenerator__Fix__SocketConnectHost
// em container, onde 127.0.0.1 do generator.cfg não alcança o serviço do Accumulator).
var fixHost = app.Configuration["OrderGenerator:Fix:SocketConnectHost"];
var fixPort = app.Configuration["OrderGenerator:Fix:SocketConnectPort"];
if (fixHost is not null || fixPort is not null)
{
    var defaults = settings.Get();
    if (fixHost is not null) defaults.SetString("SocketConnectHost", fixHost);
    if (fixPort is not null) defaults.SetString("SocketConnectPort", fixPort);
    settings.Set(defaults);
}

var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
var fixApp = app.Services.GetRequiredService<GeneratorApp>();
var storeFactory = new MemoryStoreFactory();
var initiator = new QuickFix.Transport.SocketInitiator(fixApp, storeFactory, settings, loggerFactory, new DefaultMessageFactory());
initiator.Start();
app.Lifetime.ApplicationStopping.Register(() => initiator.Stop());

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapHealthChecks("/health");

var orderTimeout = TimeSpan.FromSeconds(app.Configuration.GetValue("OrderGenerator:OrderTimeoutSeconds", 5));

app.MapPost("/api/orders", async (NewOrderRequest req) =>
{
    // Validação autoritativa de formato no gerador: entrada inválida nunca vira FIX.
    var error = OrderValidation.Validate(req);
    if (error is not null)
        return Results.BadRequest(new { error });

    try
    {
        var result = await fixApp.SendOrderAsync(req, orderTimeout);
        return Results.Ok(result);
    }
    catch (InvalidOperationException ex)   // sessão FIX indisponível
    {
        return Results.Json(new { error = ex.Message }, statusCode: 503);
    }
    catch (TimeoutException ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: 504);
    }
});

app.Run();
