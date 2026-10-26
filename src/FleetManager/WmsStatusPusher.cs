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
    private readonly WmsRetryPolicy _retryPolicy;
    private readonly WmsDeliveryFailures _failures;
    private readonly Func<DateTime> _now;
    private readonly Dictionary<string, RetryState> _retry = new(StringComparer.Ordinal);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public WmsStatusPusher(WmsOrderBoard board, WmsStatusOutbox outbox, HttpClient client, TimeSpan interval)
        : this(
            board,
            outbox,
            client,
            interval,
            new WmsRetryPolicy(interval, interval),
            new WmsDeliveryFailures(),
            () => DateTime.UtcNow)
    {
    }

    public WmsStatusPusher(
        WmsOrderBoard board,
        WmsStatusOutbox outbox,
        HttpClient client,
        TimeSpan interval,
        WmsRetryPolicy retryPolicy,
        WmsDeliveryFailures failures,
        Func<DateTime> now)
    {
        _board = board ?? throw new ArgumentNullException(nameof(board));
        _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _interval = interval > TimeSpan.Zero ? interval : throw new ArgumentOutOfRangeException(nameof(interval));
        _retryPolicy = retryPolicy ?? throw new ArgumentNullException(nameof(retryPolicy));
        _failures = failures ?? throw new ArgumentNullException(nameof(failures));
        _now = now ?? throw new ArgumentNullException(nameof(now));
    }

    public async Task SendPendingAsync(CancellationToken cancellationToken)
    {
        var now = _now();
        var ids = _outbox.Claim();
        foreach (var orderId in ids)
        {
            if (!_board.TryGet(orderId, out var order) || order is null)
            {
                Console.WriteLine($"no order for {orderId}");
                continue;
            }

            if (_retry.TryGetValue(orderId, out var retryState) && now < retryState.NextAttemptAt)
            {
                _outbox.MarkPending(orderId);
                continue;
            }

            try
            {
                var body = JsonSerializer.Serialize(order, Json);
                using var content = new StringContent(body, Encoding.UTF8, "application/json");
                using var response = await _client.PostAsync(WmsRoutes.OrderStatus, content, cancellationToken);
                var statusCode = (int)response.StatusCode;
                var outcome = ApplyOutcome(orderId, statusCode, now);
                if (outcome == WmsDeliveryResult.Delivered)
                {
                    Console.WriteLine($"pushed {orderId} {order.State}");
                }
                else if (outcome == WmsDeliveryResult.Permanent)
                {
                    Console.WriteLine($"rejected permanently {orderId} {order.State}: HTTP {statusCode}");
                }
                else
                {
                    Console.WriteLine($"rejected with {statusCode} {orderId} {order.State}");
                }
            }
            catch (HttpRequestException)
            {
                ApplyOutcome(orderId, null, now);
                Console.WriteLine($"unreachable {orderId} {order.State}");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                ApplyOutcome(orderId, null, now);
                Console.WriteLine($"timeout {orderId} {order.State}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ApplyOutcome(orderId, null, now);
                Console.WriteLine($"status push: failed {orderId} {ex.GetType().Name}, will retry");
            }
        }
    }

    private WmsDeliveryResult ApplyOutcome(string orderId, int? statusCode, DateTime now)
    {
        var outcome = WmsDeliveryOutcome.FromStatus(statusCode);
        if (outcome == WmsDeliveryResult.Delivered)
        {
            _retry.Remove(orderId);
            _failures.Clear(orderId);
        }
        else if (outcome == WmsDeliveryResult.Permanent)
        {
            _retry.Remove(orderId);
            _failures.Record(orderId, $"HTTP {statusCode}");
        }
        else
        {
            ScheduleRetry(orderId, now);
        }

        return outcome;
    }

    private void ScheduleRetry(string orderId, DateTime now)
    {
        var failures = _retry.TryGetValue(orderId, out var state) && state.Failures < int.MaxValue
            ? state.Failures + 1
            : state is null ? 1 : int.MaxValue;
        _retry[orderId] = new RetryState(failures, now + _retryPolicy.DelayFor(failures));
        _outbox.MarkPending(orderId);
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

    private sealed record RetryState(int Failures, DateTime NextAttemptAt);
}
