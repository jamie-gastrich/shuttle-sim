using System.Net;
using System.Text.Json;
using FleetCore;

namespace FleetManager.Tests;

internal sealed class StubHandler : HttpMessageHandler
{
    // --- what the stub was asked for ---
    public List<string> Paths { get; } = new();
    public List<string> ContentTypes { get; } = new();
    public List<string> Bodies { get; } = new();

    // --- how the stub answers ---
    private readonly Queue<HttpStatusCode> _replies = new();
    private readonly TimeSpan _delay;

    public StubHandler(params HttpStatusCode[] replies)
    {
        foreach (var reply in replies) _replies.Enqueue(reply);
    }

    public StubHandler(TimeSpan delay, params HttpStatusCode[] replies)
    {
        _delay = delay;
        foreach (var reply in replies) _replies.Enqueue(reply);
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Paths.Add(request.RequestUri!.AbsolutePath);
        ContentTypes.Add(request.Content?.Headers.ContentType?.ToString() ?? "");
        Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));

        if (_delay > TimeSpan.Zero)
            await Task.Delay(_delay, cancellationToken);
            
        return new HttpResponseMessage(
            _replies.Count > 0 ? _replies.Dequeue() : HttpStatusCode.OK);
    }
}

public class WmsStatusPusherTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Push_posts_the_current_word_to_the_status_route()
    {
        // Arrange: an order walked to in_progress, noted
        var tasks = new TaskBoard();
        var board = new WmsOrderBoard(tasks);
        board.Submit(new SubmitOrderRequest("ORD-1", WmsOrderKinds.Store, "PLT-001", "A01-L01"), Start);
        var task = tasks.Get("ORD-1");
        task.Assign("SHUTTLE-1", Start);
        task.Start(Start);
        Assert.Equal(OrderSyncOutcome.Advanced, board.SyncFromTask("ORD-1"));

        var outbox = new WmsStatusOutbox();
        outbox.NoteSync("ORD-1", OrderSyncOutcome.Advanced);

        // Arrange: a stub network
        var stub = new StubHandler();
        using var client = new HttpClient(stub) { BaseAddress = new Uri("http://wms.test") };
        var pusher = new WmsStatusPusher(board, outbox, client, TimeSpan.FromSeconds(1));

        // Act
        await pusher.SendPendingAsync(CancellationToken.None);

        // Assert: what the stub was asked, and the word inside the body
        Assert.Equal("/api/order-status", Assert.Single(stub.Paths));
        Assert.Equal("application/json; charset=utf-8", Assert.Single(stub.ContentTypes));
        using var doc = JsonDocument.Parse(Assert.Single(stub.Bodies));
        Assert.Equal("in_progress", doc.RootElement.GetProperty("state").GetString());
        Assert.Equal("ORD-1", doc.RootElement.GetProperty("orderId").GetString());
        Assert.Equal(0, outbox.PendingCount);
    }

    [Fact]
    public async Task Push_failure_keeps_the_order_pending_for_the_next_pass()
    {
        // Arrange: an order walked to in_progress, noted
        var tasks = new TaskBoard();
        var board = new WmsOrderBoard(tasks);
        board.Submit(new SubmitOrderRequest("ORD-1", WmsOrderKinds.Store, "PLT-001", "A01-L01"), Start);
        var task = tasks.Get("ORD-1");
        task.Assign("SHUTTLE-1", Start);
        task.Start(Start);
        Assert.Equal(OrderSyncOutcome.Advanced, board.SyncFromTask("ORD-1"));

        var outbox = new WmsStatusOutbox();
        outbox.NoteSync("ORD-1", OrderSyncOutcome.Advanced);

        // Arrange: a stub network
        var stub = new StubHandler(HttpStatusCode.InternalServerError, HttpStatusCode.OK);
        using var client = new HttpClient(stub) { BaseAddress = new Uri("http://wms.test") };
        var pusher = new WmsStatusPusher(board, outbox, client, TimeSpan.FromSeconds(1));

        await pusher.SendPendingAsync(CancellationToken.None);
        Assert.Equal(1, outbox.PendingCount);

        await pusher.SendPendingAsync(CancellationToken.None);
        Assert.Equal(0, outbox.PendingCount);
        Assert.Equal(2, stub.Paths.Count);
    }

    [Fact]
    public async Task Push_timeout_keeps_the_order_pending_and_does_not_escape()
    {
        // Arrange: an order walked to in_progress, noted
        var tasks = new TaskBoard();
        var board = new WmsOrderBoard(tasks);
        board.Submit(new SubmitOrderRequest("ORD-1", WmsOrderKinds.Store, "PLT-001", "A01-L01"), Start);
        var task = tasks.Get("ORD-1");
        task.Assign("SHUTTLE-1", Start);
        task.Start(Start);
        Assert.Equal(OrderSyncOutcome.Advanced, board.SyncFromTask("ORD-1"));

        var outbox = new WmsStatusOutbox();
        outbox.NoteSync("ORD-1", OrderSyncOutcome.Advanced);

        // Arrange: a stub network
        var stub = new StubHandler(TimeSpan.FromMilliseconds(2000));
        using var client = new HttpClient(stub) { BaseAddress = new Uri("http://wms.test"), Timeout = TimeSpan.FromMilliseconds(100) };
        var pusher = new WmsStatusPusher(board, outbox, client, TimeSpan.FromSeconds(1));

        await pusher.SendPendingAsync(CancellationToken.None);
        Assert.Equal(1, outbox.PendingCount);
    }
}