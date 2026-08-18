using Microsoft.Extensions.Logging;
using QuickFix;
using QuickFix.Fields;
using OrderAccumulator.Domain;

namespace OrderAccumulator.Application;

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
        var clOrdId = string.Empty;
        try
        {
            clOrdId = order.ClOrdID.Value;
            var fields = ExtractFields(order);

            logger.LogInformation("NewOrderSingle {ClOrdId} {Symbol} side={Side} qty={Quantity} px={Price}",
                clOrdId, fields.Symbol, fields.Side, fields.Quantity, fields.Price);

            var violation = OrderRules.Validate(fields);
            if (violation is not null)
            {
                Send(sessionID, clOrdId, fields, accepted: false, text: violation.Text, ordRejReason: violation.OrdRejReason);
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
            var ordRejReason = result.Accepted ? (int?)null : OrderRules.OrdRejReasonExceedsLimit;

            Send(sessionID, clOrdId, fields, accepted: result.Accepted, text: text, ordRejReason: ordRejReason);
        }
        catch (Exception ex)
        {
            // Falha inesperada no processamento de uma ordem não pode derrubar a sessão FIX
            logger.LogError(ex, "Erro inesperado processando NewOrderSingle {ClOrdId}", clOrdId);
            Send(sessionID, clOrdId, new OrderFields("", '\0', 0m, 0m), accepted: false,
                text: "Erro interno ao processar a ordem", ordRejReason: OrderRules.OrdRejReasonOther);
        }
    }

    private void Send(SessionID sessionID, string clOrdId, OrderFields fields, bool accepted, string text, int? ordRejReason = null)
    {
        var message = BuildFIXMessage(clOrdId, fields, accepted, text, ordRejReason);

        try
        {
            Session.SendToTarget(message, sessionID);

            logger.LogInformation("ExecutionReport {ClOrdId} -> {ExecType}: {Text}", 
                clOrdId, 
                accepted ? "New" : "Rejected", 
                text);
        }
        catch (SessionNotFound ex)
        {
            logger.LogError(ex, "Session not found for ExecutionReport {ClOrdId}", clOrdId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occurred while sending ExecutionReport {ClOrdId}", clOrdId);
        }
    }

    private static OrderFields ExtractFields(QuickFix.FIX44.NewOrderSingle order) => new(
        Symbol: order.IsSetSymbol() ? order.Symbol.Value : string.Empty,
        Side: order.IsSetSide() ? order.Side.Value : '\0',
        Quantity: order.IsSetOrderQty() ? order.OrderQty.Value : 0m,
        Price: order.IsSetPrice() ? order.Price.Value : 0m);

    private static QuickFix.FIX44.ExecutionReport BuildFIXMessage(string clOrdId, OrderFields fields, bool accepted, string text, int? ordRejReason)
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

        if (!accepted && ordRejReason.HasValue)
            report.Set(new OrdRejReason(ordRejReason.Value));

        report.Set(new Text(text));

        return report;
    }
}
