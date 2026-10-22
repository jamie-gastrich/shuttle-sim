using System.Text;
using System.Text.Json;
using FleetCore;

namespace FleetManager;

public sealed class WmsStatusPusher
{
    private readonly WmsOrderBoard _board;
    private readonly WmsStatusOutbox _outbox;
    private readonly HttpClient _client;
    private readonly TimeSpan _interval;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public WmsStatusPusher(WmsOrderBoard board, WmsStatusOutbox outbox, HttpClient client, TimeSpan interval)
    {
        _board = board ?? throw new ArgumentNullException(nameof(board));
        _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _interval = interval > TimeSpan.Zero ? interval : throw new ArgumentOutOfRangeException(nameof(interval));
    }

    public async Task SendPendingAsync(CancellationToken cancellationToken)
    {
        var ids = _outbox.Claim();
        foreach (var orderId in ids)
        {
            if (!_board.TryGet(orderId, out var order) || order is null)
            {
                Console.WriteLine($"no order for {orderId}");
                continue;
            }

            try
            {
                var body = JsonSerializer.Serialize(order, Json);
                using var content = new StringContent(body, Encoding.UTF8, "application/json");
                using var response = await _client.PostAsync(WmsRoutes.OrderStatus, content, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"pushed {orderId} {order.State}");
                }
                else
                {
                    _outbox.MarkPending(order.OrderId);
                    Console.WriteLine($"rejected with {(int)response.StatusCode} {orderId} {order.State}");
                }
            }
            catch (HttpRequestException)
            {
                _outbox.MarkPending(order.OrderId);
                Console.WriteLine($"unreachable {orderId} {order.State}");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _outbox.MarkPending(order.OrderId);
                Console.WriteLine($"timeout {orderId} {order.State}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _outbox.MarkPending(orderId);                                    // the truth is not given up on
                Console.WriteLine($"status push: failed {orderId} {ex.GetType().Name}, will retry");
            }
        }
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_interval);

        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            try
            {
                await SendPendingAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"status push: pass failed {ex.GetType().Name} {ex.Message}");
            }
        }
    }
}
