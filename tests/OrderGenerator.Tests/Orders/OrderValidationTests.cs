using OrderGenerator.Orders;
using Xunit;

namespace OrderGenerator.Tests;

public class OrderValidationTests
{
    private const string Buy = OrderValidation.SideBuy;

    private static string? Validate(string symbol, string side, int quantity, decimal price)
        => OrderValidation.Validate(new NewOrderRequest(symbol, side, quantity, price));

    [Fact]
    public void Valid_order_passes()
        => Assert.Null(Validate("PETR4", Buy, 100, 25.50m));

    [Theory]
    [InlineData("PETR4")]
    [InlineData("VALE3")]
    [InlineData("VIIA4")]
    public void All_allowed_symbols_pass(string symbol)
        => Assert.Null(Validate(symbol, Buy, 10, 1m));

    [Fact]
    public void Unknown_symbol_is_rejected()
        => Assert.NotNull(Validate("ITUB4", Buy, 100, 25m));

    [Fact]
    public void Invalid_side_is_rejected()
        => Assert.NotNull(Validate("PETR4", "Comprar", 100, 25m));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(100_000)]   // limite exclusivo
    [InlineData(150_000)]
    public void Quantity_out_of_range_is_rejected(int qty)
        => Assert.NotNull(Validate("PETR4", Buy, qty, 25m));

    [Fact]
    public void Max_valid_quantity_passes()
        => Assert.Null(Validate("PETR4", Buy, 99_999, 25m));

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(1000)]      // limite exclusivo
    [InlineData(1500)]
    public void Price_out_of_range_is_rejected(double price)
        => Assert.NotNull(Validate("PETR4", Buy, 100, (decimal)price));

    [Fact]
    public void Price_with_more_than_two_decimals_is_rejected()
        => Assert.NotNull(Validate("PETR4", Buy, 100, 25.501m));

    [Fact]
    public void Max_valid_price_passes()
        => Assert.Null(Validate("PETR4", Buy, 100, 999.99m));

    [Fact]
    public void Sell_side_passes()
        => Assert.Null(Validate("PETR4", OrderValidation.SideSell, 100, 25m));

    [Theory]
    [InlineData(OrderValidation.SideBuy, '1')]
    [InlineData(OrderValidation.SideSell, '2')]
    public void SideToFix_maps_to_wire_value(string side, char expected)
        => Assert.Equal(expected, OrderValidation.SideToFix(side));
}
