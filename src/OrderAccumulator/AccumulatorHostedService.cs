using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QuickFix;
using QuickFix.Logger;
using QuickFix.Store;

namespace OrderAccumulator;

/// <summary>
/// Hospeda o ciclo de vida do acceptor FIX 4.4 no Generic Host: inicia em StartAsync
/// e faz logout limpo em StopAsync, cobrindo SIGTERM (docker stop) e não só Ctrl+C.
/// </summary>
public sealed class AccumulatorHostedService(
    AccumulatorApp app,
    ILogger<AccumulatorHostedService> logger) : IHostedService, IDisposable
{
    private readonly ThreadedSocketAcceptor _acceptor = CreateAcceptor(app);

    // ScreenLogFactory cobre no console o que o ILoggerFactory dava (logon/logout/heartbeat);
    // FileLogFactory grava a trilha bruta de mensagens FIX em FileLogPath (accumulator.cfg).
    // Os logger.LogInformation da aplicação (AccumulatorApp) continuam via ILogger, inalterados.
    private static ThreadedSocketAcceptor CreateAcceptor(AccumulatorApp app)
    {
        var settings = new SessionSettings(Path.Combine(AppContext.BaseDirectory, "accumulator.cfg"));
        var logFactory = new CompositeLogFactory([new ScreenLogFactory(settings), new FileLogFactory(settings)]);
        return new ThreadedSocketAcceptor(app, new MemoryStoreFactory(), settings, logFactory, new DefaultMessageFactory());
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _acceptor.Start();
        logger.LogInformation("OrderAccumulator (acceptor FIX 4.4) escutando em 127.0.0.1:5001.");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _acceptor.Stop();
        return Task.CompletedTask;
    }

    public void Dispose() => _acceptor.Dispose();
}
