namespace OrderAccumulator;

/// <summary>
/// Regras de validação de campo, autoritativas no OrderAccumulator (ver ADR-0002).
/// Pura e sem dependência de QuickFIX — testável isoladamente.
/// </summary>
public static class OrderRules
{
    public static readonly IReadOnlySet<string> Symbols =
        new HashSet<string> { "PETR4", "VALE3", "VIIA4" };

    public const int MaxQuantityExclusive = 100_000;   // qtd < 100.000
    public const decimal MaxPriceExclusive = 1_000m;   // preço < 1.000

    public const char SideBuy = '1';
    public const char SideSell = '2';

    /// <summary>Retorna o motivo da rejeição, ou null se a ordem é válida.</summary>
    public static string? Validate(OrderFields order)
    {
        if (!Symbols.Contains(order.Symbol))
            return $"Simbolo invalido '{order.Symbol}'. Permitidos: {string.Join(", ", Symbols)}";

        if (order.Side != SideBuy && order.Side != SideSell)
            return $"Lado invalido '{order.Side}'. Permitidos: 1 (Compra) ou 2 (Venda)";

        if (order.Quantity != decimal.Truncate(order.Quantity))
            return $"Quantidade {order.Quantity} deve ser inteira";
        if (order.Quantity <= 0 || order.Quantity >= MaxQuantityExclusive)
            return $"Quantidade {order.Quantity} deve ser inteiro positivo menor que {MaxQuantityExclusive}";

        if (order.Price <= 0 || order.Price >= MaxPriceExclusive)
            return $"Preco {order.Price} deve ser positivo e menor que {MaxPriceExclusive}";
        if (decimal.Round(order.Price, 2) != order.Price)
            return $"Preco {order.Price} deve ser multiplo de 0.01 (no maximo 2 casas decimais)";

        return null;
    }
}
