using System.Net;
using System.Net.Http.Json;
using FleetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;

namespace FleetManager;

public class WmsEndpointTests
{
    [Fact]
    public void The_wms_url_must_be_an_http_or_https_url()
    {
        Assert.IsType<Uri>(WmsEndpoint.Parse("http://127.0.0.1:5090"));
        Assert.IsType<Uri>(WmsEndpoint.Parse("https://wms.example/api"));
        Assert.Throws<InvalidOperationException>(() => WmsEndpoint.Parse("ftp://wms.example/api"));
        Assert.Throws<InvalidOperationException>(() => WmsEndpoint.Parse("not-a-url"));
        Assert.Throws<InvalidOperationException>(() => WmsEndpoint.Parse(""));
    }

    [Fact]
    public async Task Cancel_endpoint_returns_200_and_updates_state()
    {
        var tasks = new TaskBoard();
        var board = new WmsOrderBoard(tasks);
        board.Submit(new SubmitOrderRequest("ORD-1", WmsOrderKinds.Store, "PLT-001", "A01-L01"), DateTime.UtcNow);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        var app = builder.Build();
        app.MapPost(WmsRoutes.OrderCancel("{orderId}"), (string orderId) =>
            WmsOrderCancelEndpoint.Handle(board, tasks, orderId, DateTime.UtcNow));
        app.MapGet(WmsRoutes.Order("{orderId}"), (string orderId) =>
            board.TryGet(orderId, out var order)
                ? Results.Ok(order)
                : Results.Problem(statusCode: 404, title: "Unknown order"));

        await app.StartAsync();
        try
        {
            using var client = app.GetTestClient();
            var cancel = await client.PostAsync(WmsRoutes.OrderCancel("ORD-1"), null);
            Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);

            var get = await client.GetAsync(WmsRoutes.Order("ORD-1"));
            Assert.Equal(HttpStatusCode.OK, get.StatusCode);
            var payload = await get.Content.ReadFromJsonAsync<WmsOrder>();
            Assert.NotNull(payload);
            Assert.Equal(WmsOrderStates.Cancelled, payload!.State);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Cancel_endpoint_is_idempotent_for_repeated_requests()
    {
        var tasks = new TaskBoard();
        var board = new WmsOrderBoard(tasks);
        board.Submit(new SubmitOrderRequest("ORD-1", WmsOrderKinds.Store, "PLT-001", "A01-L01"), DateTime.UtcNow);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        var app = builder.Build();
        app.MapPost(WmsRoutes.OrderCancel("{orderId}"), (string orderId) =>
            WmsOrderCancelEndpoint.Handle(board, tasks, orderId, DateTime.UtcNow));
        app.MapGet(WmsRoutes.Order("{orderId}"), (string orderId) =>
            board.TryGet(orderId, out var order)
                ? Results.Ok(order)
                : Results.Problem(statusCode: 404, title: "Unknown order"));

        await app.StartAsync();
        try
        {
            using var client = app.GetTestClient();

            var first = await client.PostAsync(WmsRoutes.OrderCancel("ORD-1"), null);
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);

            var second = await client.PostAsync(WmsRoutes.OrderCancel("ORD-1"), null);
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);

            var get = await client.GetAsync(WmsRoutes.Order("ORD-1"));
            Assert.Equal(HttpStatusCode.OK, get.StatusCode);
            var payload = await get.Content.ReadFromJsonAsync<WmsOrder>();
            Assert.NotNull(payload);
            Assert.Equal(WmsOrderStates.Cancelled, payload!.State);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }
}