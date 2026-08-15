namespace OrderGenerator;

/// <summary>Ordem vinda do formulário web (Lado em português, quantidade inteira).</summary>
public record NewOrderRequest(string Symbol, string Side, int Quantity, decimal Price);

/// <summary>
/// Validação de formato do OrderGenerator (fail-fast de UX). Cópia própria e
/// independente — o OrderAccumulator revalida por conta própria (ver ADR-0002).
/// </summary>
public static class OrderValidation
{
    public static readonly IReadOnlySet<string> Symbols =
        new HashSet<string> { "PETR4", "VALE3", "VIIA4" };

    public const int MaxQuantityExclusive = 100_000;
    public const decimal MaxPriceExclusive = 1_000m;
    public const string SideBuy = "Compra";
    public const string SideSell = "Venda";

    /// <summary>Retorna o motivo da rejeição, ou null se válida.</summary>
    public static string? Validate(NewOrderRequest r)
    {
        if (!Symbols.Contains(r.Symbol))
            return $"Simbolo invalido '{r.Symbol}'. Permitidos: {string.Join(", ", Symbols)}";
        if (r.Side != SideBuy && r.Side != SideSell)
            return $"Lado invalido '{r.Side}'. Permitidos: {SideBuy} ou {SideSell}";
        if (r.Quantity <= 0 || r.Quantity >= MaxQuantityExclusive)
            return $"Quantidade {r.Quantity} deve ser inteiro positivo menor que {MaxQuantityExclusive}";
        if (r.Price <= 0 || r.Price >= MaxPriceExclusive)
            return $"Preco {r.Price} deve ser positivo e menor que {MaxPriceExclusive}";
        if (decimal.Round(r.Price, 2) != r.Price)
            return $"Preco {r.Price} deve ser multiplo de 0.01 (no maximo 2 casas decimais)";
        return null;
    }

    public static char SideToFix(string side) => side == SideBuy ? '1' : '2';
}
