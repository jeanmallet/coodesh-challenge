namespace OrderAccumulator.Domain;

/// <summary>
/// Motivo de uma rejeição: texto livre (tag 58) + código roteável (tag 103, OrdRejReason).
/// </summary>
public sealed record RuleViolation(int OrdRejReason, string Text);

/// <summary>
/// Regras de validação de campo, autoritativas no OrderAccumulator (ver ADR-0002).
/// Pura e sem dependência de QuickFIX — testável isoladamente. Os códigos de
/// OrdRejReason abaixo são os valores de fio da tag 103 (FIX44.xml), não uma referência
/// a QuickFix.Fields.OrdRejReason — mesmo espírito de SideBuy/SideSell logo abaixo.
/// </summary>
public static class OrderRules
{
    public static readonly IReadOnlySet<string> Symbols =
        new HashSet<string> { "PETR4", "VALE3", "VIIA4" };

    public const int MaxQuantityExclusive = 100_000;   // qtd < 100.000
    public const decimal MaxPriceExclusive = 1_000m;   // preço < 1.000

    public const char SideBuy = '1';
    public const char SideSell = '2';

    public const int OrdRejReasonUnknownSymbol = 1;
    public const int OrdRejReasonExceedsLimit = 3;
    public const int OrdRejReasonIncorrectQuantity = 13;
    public const int OrdRejReasonOther = 99;

    /// <summary>Retorna a violação (texto + código), ou null se a ordem é válida.</summary>
    public static RuleViolation? Validate(OrderFields order)
    {
        if (!Symbols.Contains(order.Symbol))
            return new RuleViolation(OrdRejReasonUnknownSymbol,
                $"Simbolo invalido '{order.Symbol}'. Permitidos: {string.Join(", ", Symbols)}");

        if (order.Side != SideBuy && order.Side != SideSell)
            return new RuleViolation(OrdRejReasonOther,
                $"Lado invalido '{order.Side}'. Permitidos: 1 (Compra) ou 2 (Venda)");

        if (order.Quantity != decimal.Truncate(order.Quantity))
            return new RuleViolation(OrdRejReasonIncorrectQuantity,
                $"Quantidade {order.Quantity} deve ser inteira");
        if (order.Quantity <= 0 || order.Quantity >= MaxQuantityExclusive)
            return new RuleViolation(OrdRejReasonIncorrectQuantity,
                $"Quantidade {order.Quantity} deve ser inteiro positivo menor que {MaxQuantityExclusive}");

        if (order.Price <= 0 || order.Price >= MaxPriceExclusive)
            return new RuleViolation(OrdRejReasonOther,
                $"Preco {order.Price} deve ser positivo e menor que {MaxPriceExclusive}");
        if (decimal.Round(order.Price, 2) != order.Price)
            return new RuleViolation(OrdRejReasonOther,
                $"Preco {order.Price} deve ser multiplo de 0.01 (no maximo 2 casas decimais)");

        return null;
    }
}
