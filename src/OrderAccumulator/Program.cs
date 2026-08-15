using Microsoft.Extensions.Logging;
using QuickFix;
using QuickFix.Store;
using OrderAccumulator;

// Config referencia FIX44.xml por caminho relativo; ancora o cwd no dir do binário.
Directory.SetCurrentDirectory(AppContext.BaseDirectory);
var configPath = Path.Combine(AppContext.BaseDirectory, "accumulator.cfg");
var settings = new SessionSettings(configPath);
using var loggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole(o => o.SingleLine = true));
var app = new AccumulatorApp(loggerFactory.CreateLogger<AccumulatorApp>());
var storeFactory = new MemoryStoreFactory();      // estado em memória, reinício limpo

var acceptor = new ThreadedSocketAcceptor(app, storeFactory, settings, loggerFactory, new DefaultMessageFactory());
acceptor.Start();

var startupLogger = loggerFactory.CreateLogger<Program>();
startupLogger.LogInformation("OrderAccumulator (acceptor FIX 4.4) escutando em 127.0.0.1:5001. Ctrl+C para sair.");
var done = new ManualResetEventSlim(false);
Console.CancelKeyPress += (_, e) => { e.Cancel = true; done.Set(); };
done.Wait();

acceptor.Stop();
