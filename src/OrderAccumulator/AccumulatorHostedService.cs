using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QuickFix;
using QuickFix.Store;

namespace OrderAccumulator;

/// <summary>
/// Hospeda o ciclo de vida do acceptor FIX 4.4 no Generic Host: inicia em StartAsync
/// e faz logout limpo em StopAsync, cobrindo SIGTERM (docker stop) e não só Ctrl+C.
/// </summary>
public sealed class AccumulatorHostedService(
    AccumulatorApp app,
    ILoggerFactory loggerFactory,
    ILogger<AccumulatorHostedService> logger) : IHostedService, IDisposable
{
    private readonly ThreadedSocketAcceptor _acceptor = new(
        app,
        new MemoryStoreFactory(),
        new SessionSettings(Path.Combine(AppContext.BaseDirectory, "accumulator.cfg")),
        loggerFactory,
        new DefaultMessageFactory());

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
