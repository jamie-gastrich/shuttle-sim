using MockWms;

var builder = WebApplication.CreateBuilder(args);
var slowDelayMs = builder.Configuration.GetValue<int>("MockWms:SlowDelayMs");
if (slowDelayMs < 1)
    throw new InvalidOperationException("MockWms:SlowDelayMs must be positive.");

var ledger = new MockWmsLedger();
var control = new MockWmsControl(slowDelayMs);
var app = builder.Build();

app.MapPost("/api/order-status", async (MockOrder? request, CancellationToken cancellationToken) =>
{
    var mode = control.Snapshot();
    if (!MockOrder.TryCreate(request, out var order, out var errors))
        return Results.ValidationProblem(errors);

    if (mode.Mode == MockWmsControl.Error)
        return Results.StatusCode(StatusCodes.Status500InternalServerError);

    if (mode.Mode == MockWmsControl.Hang)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return Results.StatusCode(StatusCodes.Status500InternalServerError);
    }

    var result = ledger.Apply(order!);
    if (result == MockWmsApplyResult.Conflict)
        return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Order id already used");

    if (mode.Mode == MockWmsControl.Slow)
        await Task.Delay(TimeSpan.FromMilliseconds(mode.SlowDelayMs), cancellationToken);

    return Results.Ok(new { result });
});

app.MapGet("/api/orders", () => Results.Ok(ledger.Snapshot()));
app.MapGet("/api/orders/{orderId}", (string orderId) =>
    ledger.TryGet(orderId, out var order)
        ? Results.Ok(order)
        : Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Unknown order"));

app.MapGet("/mock/state", () =>
{
    var mode = control.Snapshot();
    return Results.Ok(new
    {
        mode.Mode,
        mode.SlowDelayMs,
        Orders = ledger.Snapshot(),
        Counts = ledger.Counts(),
    });
});

app.MapPost("/mock/mode", (MockModeRequest? request) =>
{
    if (!control.TrySetMode(request, out var state, out var errors))
        return Results.ValidationProblem(errors);
    return Results.Ok(state);
});

app.MapPost("/mock/reset", () =>
{
    ledger.Reset();
    return Results.Ok(new { Mode = control.Snapshot().Mode, Orders = ledger.Snapshot(), Counts = ledger.Counts() });
});

app.Run();
