namespace OrderAccumulator;

/// <summary>
/// Lado da ordem, para efeito de cálculo de exposição financeira.
/// </summary>
public enum OrderSide { Buy, Sell };

/// <summary>
/// Campos de uma NewOrderSingle extraídos da mensagem FIX, antes da validação.
/// Side permanece como o char de wire (1/2) — pode ainda ser inválido neste ponto.
/// </summary>
public sealed record OrderFields(string Symbol, char Side, decimal Quantity, decimal Price);
