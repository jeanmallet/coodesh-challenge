using Microsoft.Extensions.Logging;
using QuickFix;
using QuickFix.Fields;

namespace OrderAccumulator;

/// <summary>
/// Acceptor FIX 4.4. Recebe NewOrderSingle, revalida todas as regras (autoritativo),
/// aplica a regra de exposição e responde com ExecutionReport (New ou Rejected).
/// </summary>
public class AccumulatorApp(ILogger<AccumulatorApp> logger) : MessageCracker, IApplication
{
    private readonly ExposureBook _book = new();

    public void OnCreate(SessionID sessionID) { }
    public void OnLogon(SessionID sessionID) => logger.LogInformation("Logon: {SessionID}", sessionID);
    public void OnLogout(SessionID sessionID) => logger.LogInformation("Logout: {SessionID}", sessionID);
    public void ToAdmin(Message message, SessionID sessionID) { }
    public void FromAdmin(Message message, SessionID sessionID) { }
    public void ToApp(Message message, SessionID sessionID) { }
    public void FromApp(Message message, SessionID sessionID) => Crack(message, sessionID);

    public void OnMessage(QuickFix.FIX44.NewOrderSingle order, SessionID sessionID)
    {
        var clOrdId = order.ClOrdID.Value;
        var fields = ExtractFields(order);

        logger.LogInformation("NewOrderSingle {ClOrdId} {Symbol} side={Side} qty={Quantity} px={Price}",
            clOrdId, fields.Symbol, fields.Side, fields.Quantity, fields.Price);

        var error = OrderRules.Validate(fields);
        if (error is not null)
        {
            Send(sessionID, clOrdId, fields, accepted: false, text: error);
            return;
        }

        var result = _book.Evaluate(
            symbol: fields.Symbol, 
            side: fields.Side == OrderRules.SideBuy ? OrderSide.Buy : OrderSide.Sell,
            quantity: fields.Quantity, 
            price: fields.Price);

        var text = result.Accepted
            ? $"Aceita. Exposicao resultante {result.ResultingExposure:N2} para {fields.Symbol}"
            : result.RejectReason!;

        Send(sessionID, clOrdId, fields, accepted: result.Accepted, text: text);
    }

    private static OrderFields ExtractFields(QuickFix.FIX44.NewOrderSingle order) => new(
        Symbol: order.IsSetSymbol() ? order.Symbol.Value : "",
        Side: order.IsSetSide() ? order.Side.Value : '\0',
        Quantity: order.IsSetOrderQty() ? order.OrderQty.Value : 0m,
        Price: order.IsSetPrice() ? order.Price.Value : 0m);

    private void Send(SessionID sessionID, string clOrdId, OrderFields fields, bool accepted, string text)
    {
        // Campos exigidos pelo dicionário FIX44 precisam de valores válidos mesmo na
        // rejeição por formato: usa fallbacks (símbolo/lado) só para o report ser bem-formado.
        var reportSymbol = string.IsNullOrEmpty(fields.Symbol) ? "UNKNOWN" : fields.Symbol;
        var reportSide = fields.Side is OrderRules.SideBuy or OrderRules.SideSell ? fields.Side : OrderRules.SideBuy;

        var report = new QuickFix.FIX44.ExecutionReport
        {
            OrderID = new OrderID(Guid.NewGuid().ToString("N")),
            ExecID = new ExecID(Guid.NewGuid().ToString("N")),
            ExecType = new ExecType(accepted ? ExecType.NEW : ExecType.REJECTED),
            OrdStatus = new OrdStatus(accepted ? OrdStatus.NEW : OrdStatus.REJECTED),
            Symbol = new Symbol(reportSymbol),
            Side = new Side(reportSide),
            LeavesQty = new LeavesQty(0m),
            CumQty = new CumQty(accepted ? fields.Quantity : 0m),
            AvgPx = new AvgPx(accepted ? fields.Price : 0m),
        };
        report.Set(new ClOrdID(clOrdId));
        report.Set(new OrderQty(fields.Quantity));
        if (accepted)
        {
            report.Set(new LastQty(fields.Quantity));
            report.Set(new LastPx(fields.Price));
            report.Set(new Price(fields.Price));
        }
        report.Set(new Text(text));

        Session.SendToTarget(report, sessionID);
        logger.LogInformation("ExecutionReport {ClOrdId} -> {ExecType}: {Text}",
            clOrdId, accepted ? "New" : "Rejected", text);
    }
}
