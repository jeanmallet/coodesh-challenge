namespace OrderAccumulator;

public enum OrderSide { Buy, Sell }

/// <summary>
/// Regras de validação de campo, autoritativas no OrderAccumulator (ver ADR-0002).
/// Recebe os campos já extraídos da mensagem FIX (Side ainda como char 1/2).
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
    public static string? Validate(string symbol, char side, decimal quantity, decimal price)
    {
        if (!Symbols.Contains(symbol))
            return $"Simbolo invalido '{symbol}'. Permitidos: {string.Join(", ", Symbols)}";

        if (side != SideBuy && side != SideSell)
            return $"Lado invalido '{side}'. Permitidos: 1 (Compra) ou 2 (Venda)";

        if (quantity != decimal.Truncate(quantity))
            return $"Quantidade {quantity} deve ser inteira";
        if (quantity <= 0 || quantity >= MaxQuantityExclusive)
            return $"Quantidade {quantity} deve ser inteiro positivo menor que {MaxQuantityExclusive}";

        if (price <= 0 || price >= MaxPriceExclusive)
            return $"Preco {price} deve ser positivo e menor que {MaxPriceExclusive}";
        if (decimal.Round(price, 2) != price)
            return $"Preco {price} deve ser multiplo de 0.01 (no maximo 2 casas decimais)";

        return null;
    }
}
