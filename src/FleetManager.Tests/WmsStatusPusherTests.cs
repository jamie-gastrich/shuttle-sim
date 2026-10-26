using System.Net;
using System.Text.Json;
using FleetCore;

namespace FleetManager.Tests;

internal sealed class ThrowsOnceHandler : HttpMessageHandler
{
    private bool _hasThrown;

    public string? ThrownOrderId { get; private set; }
    public List<string> Sent { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = await request.Content!.ReadAsStringAsync(cancellationToken);
        if (!_hasThrown)
        {
            _hasThrown = true;
            using var document = JsonDocument.Parse(body);
            ThrownOrderId = document.RootElement.GetProperty("orderId").GetString();
            throw new InvalidOperationException("the WMS dropped this one");
        }

        Sent.Add(body);
        return new HttpResponseMessage(HttpStatusCode.OK);
    }
}

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
        var clock = Start;
        var pusher = new WmsStatusPusher(
            board,
            outbox,
            client,
            TimeSpan.FromSeconds(1),
            new WmsRetryPolicy(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(8)),
            new WmsDeliveryFailures(),
            () => clock);

        await pusher.SendPendingAsync(CancellationToken.None);
        Assert.Equal(1, outbox.PendingCount);

        clock = clock.AddSeconds(1);
        await pusher.SendPendingAsync(CancellationToken.None);
        Assert.Equal(0, outbox.PendingCount);
        Assert.Equal(2, stub.Paths.Count);
    }

    [Fact]
    public async Task A_500_reply_is_retried_after_the_first_wait()
    {
        var (board, outbox) = CreatePendingOrder();
        var stub = new StubHandler(HttpStatusCode.InternalServerError, HttpStatusCode.OK);
        using var client = new HttpClient(stub) { BaseAddress = new Uri("http://wms.test") };
        var clock = Start;
        var failures = new WmsDeliveryFailures();
        var pusher = new WmsStatusPusher(
            board, outbox, client, TimeSpan.FromSeconds(1),
            new WmsRetryPolicy(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(8)),
            failures, () => clock);

        await pusher.SendPendingAsync(CancellationToken.None);
        await pusher.SendPendingAsync(CancellationToken.None);

        Assert.Equal(1, outbox.PendingCount);
        Assert.Single(stub.Paths);

        clock = clock.AddSeconds(1);
        await pusher.SendPendingAsync(CancellationToken.None);

        Assert.Equal(0, outbox.PendingCount);
        Assert.Equal(2, stub.Paths.Count);
        Assert.Empty(failures.Snapshot());
    }

    [Fact]
    public async Task A_permanent_4xx_is_recorded_and_not_retried()
    {
        var (board, outbox) = CreatePendingOrder();
        var stub = new StubHandler(HttpStatusCode.NotFound);
        using var client = new HttpClient(stub) { BaseAddress = new Uri("http://wms.test") };
        var failures = new WmsDeliveryFailures();
        var pusher = new WmsStatusPusher(
            board, outbox, client, TimeSpan.FromSeconds(1),
            new WmsRetryPolicy(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(8)),
            failures, () => Start);

        await pusher.SendPendingAsync(CancellationToken.None);
        await pusher.SendPendingAsync(CancellationToken.None);

        Assert.Equal(0, outbox.PendingCount);
        Assert.Single(stub.Paths);
        var failure = Assert.Single(failures.Snapshot());
        Assert.Equal("ORD-1", failure.OrderId);
        Assert.Equal("HTTP 404", failure.Reason);
    }

    [Fact]
    public async Task A_successful_retry_clears_the_backoff_and_the_failure()
    {
        var (board, outbox) = CreatePendingOrder();
        var stub = new StubHandler(
            HttpStatusCode.NotFound,
            HttpStatusCode.InternalServerError,
            HttpStatusCode.OK,
            HttpStatusCode.InternalServerError,
            HttpStatusCode.OK);
        using var client = new HttpClient(stub) { BaseAddress = new Uri("http://wms.test") };
        var clock = Start;
        var failures = new WmsDeliveryFailures();
        var pusher = new WmsStatusPusher(
            board, outbox, client, TimeSpan.FromSeconds(1),
            new WmsRetryPolicy(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(8)),
            failures, () => clock);

        await pusher.SendPendingAsync(CancellationToken.None);
        Assert.Single(failures.Snapshot());

        outbox.MarkPending("ORD-1");
        await pusher.SendPendingAsync(CancellationToken.None);
        Assert.Equal(1, outbox.PendingCount);

        clock = clock.AddSeconds(1);
        await pusher.SendPendingAsync(CancellationToken.None);
        Assert.Empty(failures.Snapshot());
        Assert.Equal(0, outbox.PendingCount);

        outbox.MarkPending("ORD-1");
        await pusher.SendPendingAsync(CancellationToken.None);
        Assert.Equal(1, outbox.PendingCount);

        clock = clock.AddSeconds(1);
        await pusher.SendPendingAsync(CancellationToken.None);

        Assert.Equal(0, outbox.PendingCount);
        Assert.Equal(5, stub.Paths.Count);
        Assert.Empty(failures.Snapshot());
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
    

    [Fact]
    public async Task A_push_pass_that_throws_keeps_the_word_and_the_next_order()
    {
        // Arrange: two orders, both owed a word
        var tasks = new TaskBoard();
        var board = new WmsOrderBoard(tasks);
        board.Submit(new SubmitOrderRequest("ORD-1", WmsOrderKinds.Store, "PLT-001", "A01-L01"), Start);
        board.Submit(new SubmitOrderRequest("ORD-2", WmsOrderKinds.Store, "PLT-002", "A01-L02"), Start);
        var outbox = new WmsStatusOutbox();
        outbox.MarkPending("ORD-1");
        outbox.MarkPending("ORD-2");

        // Arrange: the first send throws, the second answers 200
        var handler = new ThrowsOnceHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://wms.test") };
        var pusher = new WmsStatusPusher(board, outbox, client, TimeSpan.FromSeconds(1));

        // Act
        await pusher.SendPendingAsync(CancellationToken.None);

        // Assert: the broken order is still owed, and the other one went out
        Assert.Equal(1, outbox.PendingCount);
        Assert.Equal(handler.ThrownOrderId, Assert.Single(outbox.Claim()));
        Assert.Single(handler.Sent);
        using var document = JsonDocument.Parse(handler.Sent[0]);
        Assert.NotEqual(handler.ThrownOrderId, document.RootElement.GetProperty("orderId").GetString());
    }

    private static (WmsOrderBoard Board, WmsStatusOutbox Outbox) CreatePendingOrder()
    {
        var taskBoard = new TaskBoard();
        var board = new WmsOrderBoard(taskBoard);
        board.Submit(
            new SubmitOrderRequest("ORD-1", WmsOrderKinds.Store, "PLT-001", "A01-L01"),
            Start);
        var task = taskBoard.Get("ORD-1");
        task.Assign("SHUTTLE-1", Start);
        task.Start(Start);
        Assert.Equal(OrderSyncOutcome.Advanced, board.SyncFromTask("ORD-1"));

        var outbox = new WmsStatusOutbox();
        outbox.MarkPending("ORD-1");
        return (board, outbox);
    }
}