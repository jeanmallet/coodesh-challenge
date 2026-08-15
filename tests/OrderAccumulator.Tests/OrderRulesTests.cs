using OrderAccumulator;
using Xunit;

namespace OrderAccumulator.Tests;

public class OrderRulesTests
{
    private const char Buy = OrderRules.SideBuy;

    private static string? Validate(string symbol, char side, decimal quantity, decimal price)
        => OrderRules.Validate(new OrderFields(symbol, side, quantity, price));

    [Fact]
    public void Valid_order_passes()
        => Assert.Null(Validate("PETR4", Buy, 100m, 25.50m));

    [Theory]
    [InlineData("PETR4")]
    [InlineData("VALE3")]
    [InlineData("VIIA4")]
    public void All_allowed_symbols_pass(string symbol)
        => Assert.Null(Validate(symbol, Buy, 10m, 1m));

    [Fact]
    public void Unknown_symbol_is_rejected()
        => Assert.NotNull(Validate("ITUB4", Buy, 100m, 25m));

    [Fact]
    public void Invalid_side_is_rejected()
        => Assert.NotNull(Validate("PETR4", 'X', 100m, 25m));

    [Fact]
    public void Non_integer_quantity_is_rejected()
        => Assert.NotNull(Validate("PETR4", Buy, 100.5m, 25m));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(100_000)]   // limite exclusivo
    [InlineData(150_000)]
    public void Quantity_out_of_range_is_rejected(int qty)
        => Assert.NotNull(Validate("PETR4", Buy, qty, 25m));

    [Fact]
    public void Max_valid_quantity_passes()
        => Assert.Null(Validate("PETR4", Buy, 99_999m, 25m));

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(1000)]      // limite exclusivo
    [InlineData(1500)]
    public void Price_out_of_range_is_rejected(double price)
        => Assert.NotNull(Validate("PETR4", Buy, 100m, (decimal)price));

    [Fact]
    public void Price_with_more_than_two_decimals_is_rejected()
        => Assert.NotNull(Validate("PETR4", Buy, 100m, 25.501m));

    [Fact]
    public void Max_valid_price_passes()
        => Assert.Null(Validate("PETR4", Buy, 100m, 999.99m));
}
