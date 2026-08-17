using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OrderAccumulator;

// Config referencia FIX44.xml por caminho relativo; ancora o cwd no dir do binário.
Directory.SetCurrentDirectory(AppContext.BaseDirectory);

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders().AddSimpleConsole(o => o.SingleLine = true);

builder.Services.AddSingleton<AccumulatorApp>();
builder.Services.AddHostedService<AccumulatorHostedService>();

var host = builder.Build();
await host.RunAsync();
