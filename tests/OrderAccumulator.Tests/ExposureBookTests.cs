using OrderAccumulator;
using Xunit;

namespace OrderAccumulator.Tests;

public class ExposureBookTests
{
    // Limite = R$ 100.000.000. 200 * 500.000 = 100.000.000 exatos.
    private const decimal Px = 200m;
    private const decimal QtyAtLimit = 500_000m;

    [Fact]
    public void Buy_increases_exposure()
    {
        var book = new ExposureBook();
        var r = book.Evaluate("PETR4", OrderSide.Buy, 1000m, 50m);
        Assert.True(r.Accepted);
        Assert.Equal(50_000m, r.ResultingExposure);
        Assert.Equal(50_000m, book.Current("PETR4"));
    }

    [Fact]
    public void Sell_decreases_exposure()
    {
        var book = new ExposureBook();
        book.Evaluate("PETR4", OrderSide.Buy, 1000m, 100m);   // +100.000
        var r = book.Evaluate("PETR4", OrderSide.Sell, 400m, 100m); // -40.000
        Assert.True(r.Accepted);
        Assert.Equal(60_000m, r.ResultingExposure);
    }

    [Fact]
    public void Exactly_at_limit_is_accepted() // "ultrapassar" é estrito (>)
    {
        var book = new ExposureBook();
        var r = book.Evaluate("PETR4", OrderSide.Buy, QtyAtLimit, Px); // 100.000.000
        Assert.True(r.Accepted);
        Assert.Equal(100_000_000m, book.Current("PETR4"));
    }

    [Fact]
    public void Above_limit_is_rejected_and_exposure_unchanged()
    {
        var book = new ExposureBook();
        var r = book.Evaluate("PETR4", OrderSide.Buy, QtyAtLimit + 1m, Px); // 100.000.200
        Assert.False(r.Accepted);
        Assert.NotNull(r.RejectReason);
        Assert.Equal(0m, book.Current("PETR4")); // rejeitada não entra no cálculo
    }

    [Fact]
    public void Net_short_exposure_uses_absolute_value()
    {
        var book = new ExposureBook();
        book.Evaluate("VALE3", OrderSide.Sell, QtyAtLimit, Px);            // -100.000.000 (ok, |.|==limite)
        var r = book.Evaluate("VALE3", OrderSide.Sell, 1m, Px);           // -100.000.200 → |.| > limite
        Assert.False(r.Accepted);
        Assert.Equal(-100_000_000m, book.Current("VALE3"));
    }

    [Fact]
    public void Symbols_are_tracked_independently()
    {
        var book = new ExposureBook();
        book.Evaluate("PETR4", OrderSide.Buy, 1000m, 100m); // 100.000
        book.Evaluate("VALE3", OrderSide.Buy, 2000m, 100m); // 200.000
        Assert.Equal(100_000m, book.Current("PETR4"));
        Assert.Equal(200_000m, book.Current("VALE3"));
    }

    [Fact]
    public void Concurrent_orders_never_breach_limit() // valida o lock por símbolo
    {
        var book = new ExposureBook();
        int accepted = 0;

        // Cada ordem sozinha atinge o limite (100M). Em paralelo, só UMA pode ser aceita.
        Parallel.For(0, 1000, _ =>
        {
            if (book.Evaluate("PETR4", OrderSide.Buy, QtyAtLimit, Px).Accepted)
                Interlocked.Increment(ref accepted);
        });

        Assert.Equal(1, accepted);
        Assert.Equal(100_000_000m, book.Current("PETR4"));
    }
}
