using Microsoft.Extensions.Diagnostics.HealthChecks;
using OrderGenerator.Orders;

namespace OrderGenerator;

/// <summary>Saudável apenas enquanto a sessão FIX com o OrderAccumulator está logada.</summary>
public sealed class FixSessionHealthCheck(OrderGeneratorApp app) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(app.IsSessionActive
            ? HealthCheckResult.Healthy("Sessao FIX ativa")
            : HealthCheckResult.Unhealthy("Sessao FIX indisponivel"));
}
