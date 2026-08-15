using QuickFix;
using QuickFix.Store;
using OrderGenerator;

var builder = WebApplication.CreateBuilder(args);
// Porta web fixa e distinta da porta FIX (5001), para não confundir a UI com o socket FIX.
builder.WebHost.UseUrls("http://localhost:5080");
var app = builder.Build();

// Initiator FIX 4.4 embutido no processo web.
// Config referencia FIX44.xml por caminho relativo; ancora o cwd no dir do binário.
Directory.SetCurrentDirectory(AppContext.BaseDirectory);
var settings = new SessionSettings(Path.Combine(AppContext.BaseDirectory, "generator.cfg"));
var fixApp = new GeneratorApp();
var storeFactory = new MemoryStoreFactory();
var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
var initiator = new QuickFix.Transport.SocketInitiator(fixApp, storeFactory, settings, loggerFactory, new DefaultMessageFactory());
initiator.Start();
app.Lifetime.ApplicationStopping.Register(() => initiator.Stop());

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapPost("/api/orders", async (NewOrderRequest req) =>
{
    // Validação autoritativa de formato no gerador: entrada inválida nunca vira FIX.
    var error = OrderValidation.Validate(req);
    if (error is not null)
        return Results.BadRequest(new { error });

    try
    {
        var result = await fixApp.SendOrderAsync(req, TimeSpan.FromSeconds(5));
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
