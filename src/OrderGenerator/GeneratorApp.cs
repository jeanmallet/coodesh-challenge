using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using QuickFix;
using QuickFix.Fields;

namespace OrderGenerator;

/// <summary>Resposta do OrderAccumulator, achatada para o formulário web.</summary>
public record OrderResult(
    string ClOrdId, bool Accepted, string ExecType, string OrdStatus,
    string Symbol, string Side, decimal Quantity, decimal Price, string Text);

/// <summary>
/// Initiator FIX 4.4. Envia NewOrderSingle e faz a ponte assíncrona→síncrona:
/// registra um TaskCompletionSource por ClOrdID e o completa quando o
/// ExecutionReport correspondente chega no FromApp (correlação pela tag 11).
/// </summary>
public class GeneratorApp(ILogger<GeneratorApp> logger) : MessageCracker, IApplication
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<OrderResult>> _pending = new();
    private volatile SessionID? _sessionId;

    /// <summary>True enquanto a sessão FIX com o OrderAccumulator está logada. Base do health check.</summary>
    public bool IsSessionActive => _sessionId is not null;

    public void OnCreate(SessionID sessionID) { }
    public void OnLogon(SessionID sessionID) { _sessionId = sessionID; logger.LogInformation("Logon: {SessionID}", sessionID); }
    public void OnLogout(SessionID sessionID) { _sessionId = null; logger.LogInformation("Logout: {SessionID}", sessionID); }
    public void ToAdmin(Message message, SessionID sessionID) { }
    public void FromAdmin(Message message, SessionID sessionID) { }
    public void ToApp(Message message, SessionID sessionID) { }
    public void FromApp(Message message, SessionID sessionID) => Crack(message, sessionID);

    public async Task<OrderResult> SendOrderAsync(NewOrderRequest req, TimeSpan timeout)
    {
        var session = _sessionId
            ?? throw new InvalidOperationException("Sessao FIX indisponivel. O OrderAccumulator esta rodando?");

        var clOrdId = Guid.NewGuid().ToString("N");
        var order = new QuickFix.FIX44.NewOrderSingle();
        order.Set(new ClOrdID(clOrdId));
        order.Set(new Symbol(req.Symbol));
        order.Set(new Side(OrderValidation.SideToFix(req.Side)));
        order.Set(new TransactTime(DateTime.UtcNow));
        order.Set(new OrderQty(req.Quantity));
        order.Set(new OrdType(OrdType.LIMIT));
        order.Set(new Price(req.Price));

        var tcs = new TaskCompletionSource<OrderResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[clOrdId] = tcs;
        try
        {
            Session.SendToTarget(order, session);
            return await tcs.Task.WaitAsync(timeout);
        }
        catch (SessionNotFound ex)
        {
            throw new InvalidOperationException("Sessao FIX indisponivel. O OrderAccumulator esta rodando?", ex);
        }
        catch (TimeoutException)
        {
            throw new TimeoutException("Sem resposta do OrderAccumulator dentro do tempo limite.");
        }
        finally
        {
            _pending.TryRemove(clOrdId, out _);
        }
    }

    public void OnMessage(QuickFix.FIX44.ExecutionReport report, SessionID sessionID)
    {
        var clOrdId = report.IsSetClOrdID() ? report.ClOrdID.Value : "";
        if (!_pending.TryGetValue(clOrdId, out var tcs))
            return; // resposta órfã (ex.: após timeout) — ignora

        var execType = report.ExecType.Value;
        var price = report.IsSetPrice() ? report.Price.Value
                  : report.IsSetLastPx() ? report.LastPx.Value : 0m;

        tcs.TrySetResult(new OrderResult(
            clOrdId,
            Accepted: execType == ExecType.NEW,
            ExecType: ExecTypeName(execType),
            OrdStatus: OrdStatusName(report.OrdStatus.Value),
            Symbol: report.IsSetSymbol() ? report.Symbol.Value : "",
            Side: SideName(report.Side.Value),
            Quantity: report.IsSetOrderQty() ? report.OrderQty.Value : 0m,
            Price: price,
            Text: report.IsSetText() ? report.Text.Value : ""));
    }

    private static string ExecTypeName(char c) => c switch
    {
        ExecType.NEW => "New",
        ExecType.REJECTED => "Rejected",
        _ => c.ToString(),
    };

    private static string OrdStatusName(char c) => c switch
    {
        OrdStatus.NEW => "New",
        OrdStatus.REJECTED => "Rejected",
        _ => c.ToString(),
    };

    private static string SideName(char c) => c switch
    {
        Side.BUY => "Compra",
        Side.SELL => "Venda",
        _ => c.ToString(),
    };
}
