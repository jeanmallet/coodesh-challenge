using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using QuickFix;
using QuickFix.Store;
using OrderGenerator;
using OrderGenerator.Orders;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<OrderGeneratorApp>();
builder.Services.AddHealthChecks().AddCheck<FixSessionHealthCheck>("fix-session");
builder.Services.AddProblemDetails();

// Fixed window por IP: protege POST /api/orders sem afetar /health.
var rateLimitPermitLimit = builder.Configuration.GetValue("OrderGenerator:RateLimit:PermitLimit", 10);
var rateLimitWindowSeconds = builder.Configuration.GetValue("OrderGenerator:RateLimit:WindowSeconds", 10);
builder.Services.AddRateLimiter(options =>
{
    options.OnRejected = (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        return new ValueTask(context.HttpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Title = "Muitas requisicoes",
            Status = StatusCodes.Status429TooManyRequests,
            Detail = "Limite de requisicoes excedido. Tente novamente em instantes.",
        }, cancellationToken));
    };
    options.AddPolicy("orders", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = rateLimitPermitLimit,
            Window = TimeSpan.FromSeconds(rateLimitWindowSeconds),
            QueueLimit = 0,
        }));
});

var app = builder.Build();
app.UseExceptionHandler();

app.UseRateLimiter();

// Initiator FIX 4.4 embutido no processo web.
// Config referencia FIX44.xml por caminho relativo; ancora o cwd no dir do binário.
Directory.SetCurrentDirectory(AppContext.BaseDirectory);
var settings = new SessionSettings(Path.Combine(AppContext.BaseDirectory, "generator.cfg"));

// Sobrescreve o alvo FIX quando configurado (ex.: OrderGenerator__Fix__SocketConnectHost
// em container, onde 127.0.0.1 do generator.cfg não alcança o serviço do Accumulator).
// Precisa mutar o dicionário por sessão, não o [DEFAULT]: SessionSettings já mescla os
// dois na leitura do .cfg, então uma sessão já existente não herda mudança no default.
// Get(sessionId) devolve o dicionário vivo (SettingsDictionary é referência) — mutar
// basta; Set(sessionId, ...) rejeitaria com "Duplicate Session" para uma já existente.
var fixHost = app.Configuration["OrderGenerator:Fix:SocketConnectHost"];
var fixPort = app.Configuration["OrderGenerator:Fix:SocketConnectPort"];
if (fixHost is not null || fixPort is not null)
{
    foreach (var sessionId in settings.GetSessions())
    {
        var sessionSettings = settings.Get(sessionId);
        if (fixHost is not null) sessionSettings.SetString("SocketConnectHost", fixHost);
        if (fixPort is not null) sessionSettings.SetString("SocketConnectPort", fixPort);
    }
}

var fixApp = app.Services.GetRequiredService<OrderGeneratorApp>();
var storeFactory = new MemoryStoreFactory();
// ScreenLogFactory cobre no console o que o ILoggerFactory dava (logon/logout/heartbeat);
// FileLogFactory grava a trilha bruta de mensagens FIX em FileLogPath (generator.cfg).
// Os logger.LogInformation da aplicação (OrderGeneratorApp) continuam via ILogger, inalterados.
QuickFix.Logger.ILogFactory logFactory = new QuickFix.Logger.CompositeLogFactory(
    [new QuickFix.Logger.ScreenLogFactory(settings), new QuickFix.Logger.FileLogFactory(settings)]);
    
var initiator = new QuickFix.Transport.SocketInitiator(fixApp, storeFactory, settings, logFactory, new DefaultMessageFactory());
initiator.Start();
app.Lifetime.ApplicationStopping.Register(() => initiator.Stop());

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapHealthChecks("/health");
app.MapOrdersEndpoint(fixApp);

app.Run();
