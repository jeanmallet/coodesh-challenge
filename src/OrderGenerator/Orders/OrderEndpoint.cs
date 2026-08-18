namespace OrderGenerator.Orders;

/// <summary>Endpoint HTTP do slice de pedidos: POST /api/orders.</summary>
public static class OrderEndpoints
{
    public static void MapOrdersEndpoint(this WebApplication app, OrderGeneratorApp fixApp)
    {
        var orderTimeout = TimeSpan.FromSeconds(app.Configuration.GetValue("OrderGenerator:OrderTimeoutSeconds", 5));

        app.MapPost("/api/orders", async (NewOrderRequest req) =>
        {
            // Validação autoritativa de formato no gerador: entrada inválida nunca vira FIX.
            var error = OrderValidation.Validate(req);
            if (error is not null)
                return Results.Problem(detail: error, statusCode: StatusCodes.Status400BadRequest, title: "Ordem invalida");

            try
            {
                var result = await fixApp.SendOrderAsync(req, orderTimeout);
                return Results.Ok(result);
            }
            catch (InvalidOperationException ex)   // sessão FIX indisponível
            {
                return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable, title: "Sessao FIX indisponivel");
            }
            catch (TimeoutException ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status504GatewayTimeout, title: "Tempo limite excedido");
            }
        }).RequireRateLimiting("orders");
    }
}
