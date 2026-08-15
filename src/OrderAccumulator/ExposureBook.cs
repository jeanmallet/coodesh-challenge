using System.Collections.Concurrent;

namespace OrderAccumulator;

public sealed record EvaluationResult(bool Accepted, decimal ResultingExposure, string? RejectReason)
{
    public static EvaluationResult Accept(decimal exposure) => new(true, exposure, null);
    public static EvaluationResult Reject(string reason, decimal unchanged) => new(false, unchanged, reason);
}

/// <summary>
/// Exposição financeira acumulada por símbolo, em memória (reinício limpo para o exercício do desafio).
/// Compras somam, vendas subtraem. A verificação do limite e a atualização
/// acontecem atômicas dentro de um lock por símbolo (ver decisão de concorrência).
/// </summary>
public sealed class ExposureBook
{
    public const decimal LimitPerSymbol = 100_000_000m; // R$ 100 milhões, valor absoluto

    private readonly ConcurrentDictionary<string, decimal> _exposure = new();
    private readonly ConcurrentDictionary<string, object> _locks = new();

    public decimal Current(string symbol) => _exposure.GetValueOrDefault(symbol);

    /// <summary>
    /// Decide aceitar/rejeitar pela exposição resultante e, se aceita, aplica.
    /// Rejeitada não altera a exposição.
    /// </summary>
    public EvaluationResult Evaluate(string symbol, OrderSide side, decimal quantity, decimal price)
    {
        var gate = _locks.GetOrAdd(symbol, _ => new object());
        lock (gate)
        {
            var current = _exposure.GetValueOrDefault(symbol);
            var signed = price * quantity * (side == OrderSide.Buy ? 1 : -1);
            var resulting = current + signed;

            // "ultrapassar" = estritamente maior; exatamente no limite é aceito.
            if (Math.Abs(resulting) > LimitPerSymbol)
                return EvaluationResult.Reject(
                    $"Exposicao resultante {resulting:N2} ultrapassa o limite de {LimitPerSymbol:N2} para {symbol}",
                    current);

            _exposure[symbol] = resulting;
            return EvaluationResult.Accept(resulting);
        }
    }
}
