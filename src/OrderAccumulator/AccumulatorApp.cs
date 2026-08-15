using QuickFix;
using QuickFix.Fields;

namespace OrderAccumulator;

/// <summary>
/// Acceptor FIX 4.4. Recebe NewOrderSingle, revalida todas as regras (autoritativo),
/// aplica a regra de exposição e responde com ExecutionReport (New ou Rejected).
/// </summary>
public class AccumulatorApp : MessageCracker, IApplication
{
    private readonly ExposureBook _book = new();

    public void OnCreate(SessionID sessionID) { }
    public void OnLogon(SessionID sessionID) => Console.WriteLine($"[Accumulator] Logon: {sessionID}");
    public void OnLogout(SessionID sessionID) => Console.WriteLine($"[Accumulator] Logout: {sessionID}");
    public void ToAdmin(Message message, SessionID sessionID) { }
    public void FromAdmin(Message message, SessionID sessionID) { }
    public void ToApp(Message message, SessionID sessionID) { }
    public void FromApp(Message message, SessionID sessionID) => Crack(message, sessionID);

    public void OnMessage(QuickFix.FIX44.NewOrderSingle order, SessionID sessionID)
    {
        var clOrdId = order.ClOrdID.Value;
        var symbol = order.IsSetSymbol() ? order.Symbol.Value : "";
        var side = order.IsSetSide() ? order.Side.Value : '\0';
        var quantity = order.IsSetOrderQty() ? order.OrderQty.Value : 0m;
        var price = order.IsSetPrice() ? order.Price.Value : 0m;

        Console.WriteLine($"[Accumulator] NewOrderSingle {clOrdId} {symbol} side={side} qty={quantity} px={price}");

        var error = OrderRules.Validate(symbol, side, quantity, price);
        if (error is not null)
        {
            Send(sessionID, clOrdId, symbol, side, quantity, price,
                accepted: false, text: error);
            return;
        }

        var result = _book.Evaluate(
            symbol, side == OrderRules.SideBuy ? OrderSide.Buy : OrderSide.Sell, quantity, price);

        var text = result.Accepted
            ? $"Aceita. Exposicao resultante {result.ResultingExposure:N2} para {symbol}"
            : result.RejectReason!;

        Send(sessionID, clOrdId, symbol, side, quantity, price,
            accepted: result.Accepted, text: text);
    }

    private static void Send(SessionID sessionID, string clOrdId, string symbol, char side,
        decimal quantity, decimal price, bool accepted, string text)
    {
        // Campos exigidos pelo dicionário FIX44 precisam de valores válidos mesmo na
        // rejeição por formato: usa fallbacks (símbolo/lado) só para o report ser bem-formado.
        var reportSymbol = string.IsNullOrEmpty(symbol) ? "UNKNOWN" : symbol;
        var reportSide = side is OrderRules.SideBuy or OrderRules.SideSell ? side : OrderRules.SideBuy;

        var report = new QuickFix.FIX44.ExecutionReport
        {
            OrderID = new OrderID(Guid.NewGuid().ToString("N")),
            ExecID = new ExecID(Guid.NewGuid().ToString("N")),
            ExecType = new ExecType(accepted ? ExecType.NEW : ExecType.REJECTED),
            OrdStatus = new OrdStatus(accepted ? OrdStatus.NEW : OrdStatus.REJECTED),
            Symbol = new Symbol(reportSymbol),
            Side = new Side(reportSide),
            LeavesQty = new LeavesQty(0m),
            CumQty = new CumQty(accepted ? quantity : 0m),
            AvgPx = new AvgPx(accepted ? price : 0m),
        };
        report.Set(new ClOrdID(clOrdId));
        report.Set(new OrderQty(quantity));
        if (accepted)
        {
            report.Set(new LastQty(quantity));
            report.Set(new LastPx(price));
            report.Set(new Price(price));
        }
        report.Set(new Text(text));

        Session.SendToTarget(report, sessionID);
        Console.WriteLine($"[Accumulator] ExecutionReport {clOrdId} -> {(accepted ? "New" : "Rejected")}: {text}");
    }
}
