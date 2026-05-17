using System.Reflection;
using System.Text;
using Bunny;
using Bunny.Internal;
using Bunny.Tests.Fixtures;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Xunit;

namespace Bunny.Tests;

public class HandlerDispatcherTests
{
    private readonly HandlerInvocations _recorder = new();
    private readonly IBunnyPublisher _publisher = Substitute.For<IBunnyPublisher>();
    private readonly IBunnySerializer _serializer = new JsonBunnySerializer();
    private readonly IServiceProvider _serviceProvider;

    public HandlerDispatcherTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_recorder);
        services.AddScoped<OrderHandler>();
        services.AddScoped<AuditHandler>();
        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public async Task Task_returning_method_acks_on_success()
    {
        var channel = Substitute.For<IChannel>();
        var dispatcher = BuildDispatcher(nameof(OrderHandler.OnCreated));
        var id = Guid.NewGuid();
        var ea = MakeEvent($"order.{id}.created", _serializer.Serialize(new OrderDto("x", 1m)));
        using var cts = new CancellationTokenSource();

        await dispatcher.DispatchAsync(channel, ea, cts.Token);

        await channel.Received(1).BasicAckAsync(ea.DeliveryTag, multiple: false, Arg.Any<CancellationToken>());
        _recorder.Calls.Should().ContainSingle(c => c.Method == nameof(OrderHandler.OnCreated));
        _recorder.Calls[0].RouteParameters["id"].Should().Be(id);
        _recorder.Calls[0].Body.Should().Be(new OrderDto("x", 1m));
        _recorder.Calls[0].HasCancellationToken.Should().BeTrue();
    }

    [Fact]
    public async Task AckResult_returning_method_uses_returned_value()
    {
        var channel = Substitute.For<IChannel>();
        var dispatcher = BuildDispatcher(nameof(OrderHandler.OnCancelled));
        var ea = MakeEvent($"order.{Guid.NewGuid()}.cancelled");

        await dispatcher.DispatchAsync(channel, ea, CancellationToken.None);

        await channel.Received(1).BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true, Arg.Any<CancellationToken>());
        await channel.DidNotReceive().BasicAckAsync(Arg.Any<ulong>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Exception_causes_nack_with_RequeueOnError()
    {
        var channel = Substitute.For<IChannel>();
        var dispatcher = BuildDispatcher(nameof(OrderHandler.OnFail));
        var ea = MakeEvent("order.fail");

        await dispatcher.DispatchAsync(channel, ea, CancellationToken.None);

        await channel.Received(1).BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AckNowAsync_skips_post_handler_ack()
    {
        var channel = Substitute.For<IChannel>();
        var dispatcher = BuildDispatcher(nameof(OrderHandler.OnFast));
        var ea = MakeEvent($"order.{Guid.NewGuid()}.fast");

        await dispatcher.DispatchAsync(channel, ea, CancellationToken.None);

        await channel.Received(1).BasicAckAsync(ea.DeliveryTag, multiple: false, Arg.Any<CancellationToken>());
        _recorder.Calls.Should().ContainSingle(c => c.Method == nameof(OrderHandler.OnFast));
    }

    [Fact]
    public async Task RejectNowAsync_sends_reject_and_skips_post_handler_ack()
    {
        var channel = Substitute.For<IChannel>();
        var dispatcher = BuildDispatcher(nameof(OrderHandler.OnRejectEarly));
        var ea = MakeEvent($"order.{Guid.NewGuid()}.reject");

        await dispatcher.DispatchAsync(channel, ea, CancellationToken.None);

        await channel.Received(1).BasicRejectAsync(ea.DeliveryTag, requeue: false, Arg.Any<CancellationToken>());
        await channel.DidNotReceive().BasicAckAsync(Arg.Any<ulong>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Routing_key_mismatch_nacks_without_requeue()
    {
        var channel = Substitute.For<IChannel>();
        var dispatcher = BuildDispatcher(nameof(OrderHandler.OnCreated));
        var ea = MakeEvent("order.not-a-guid.created");

        await dispatcher.DispatchAsync(channel, ea, CancellationToken.None);

        await channel.Received(1).BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, Arg.Any<CancellationToken>());
        _recorder.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task String_route_parameter_binds_to_method_argument()
    {
        var channel = Substitute.For<IChannel>();
        var dispatcher = BuildDispatcher<AuditHandler>(nameof(AuditHandler.OnEvent));
        var ea = MakeEvent("audit.web.event", Encoding.UTF8.GetBytes("payload"));

        await dispatcher.DispatchAsync(channel, ea, CancellationToken.None);

        _recorder.Calls.Should().ContainSingle();
        _recorder.Calls[0].RouteParameters["source"].Should().Be("web");
        _recorder.Calls[0].Body.Should().Be("payload");
    }

    [Fact]
    public async Task FromBody_parameter_receives_deserialized_body()
    {
        var channel = Substitute.For<IChannel>();
        var dispatcher = BuildDispatcher(nameof(OrderHandler.OnFromBody));
        var id = Guid.NewGuid();
        var ea = MakeEvent($"order.{id}.frombody", _serializer.Serialize(new OrderDto("widget", 42m)));

        await dispatcher.DispatchAsync(channel, ea, CancellationToken.None);

        await channel.Received(1).BasicAckAsync(ea.DeliveryTag, multiple: false, Arg.Any<CancellationToken>());
        _recorder.Calls.Should().ContainSingle();
        _recorder.Calls[0].Method.Should().Be(nameof(OrderHandler.OnFromBody));
        _recorder.Calls[0].RouteParameters["id"].Should().Be(id);
        _recorder.Calls[0].Body.Should().Be(new OrderDto("widget", 42m));
    }

    [Fact]
    public async Task FromBody_with_malformed_body_nacks_via_exception_path()
    {
        var channel = Substitute.For<IChannel>();
        var dispatcher = BuildDispatcher(nameof(OrderHandler.OnFromBody));
        var ea = MakeEvent($"order.{Guid.NewGuid()}.frombody", Encoding.UTF8.GetBytes("not-json"));

        await dispatcher.DispatchAsync(channel, ea, CancellationToken.None);

        await channel.Received(1).BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, Arg.Any<CancellationToken>());
        _recorder.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Each_dispatch_resolves_handler_in_its_own_scope()
    {
        var channel = Substitute.For<IChannel>();
        var dispatcher = BuildDispatcher(nameof(OrderHandler.OnCreated));
        var ea1 = MakeEvent($"order.{Guid.NewGuid()}.created", _serializer.Serialize(new OrderDto("a", 1m)), deliveryTag: 1);
        var ea2 = MakeEvent($"order.{Guid.NewGuid()}.created", _serializer.Serialize(new OrderDto("b", 2m)), deliveryTag: 2);

        await dispatcher.DispatchAsync(channel, ea1, CancellationToken.None);
        await dispatcher.DispatchAsync(channel, ea2, CancellationToken.None);

        _recorder.Calls.Should().HaveCount(2);
        _recorder.Calls.Select(c => c.Body).Should().BeEquivalentTo([new OrderDto("a", 1m), new OrderDto("b", 2m)]);
    }

    private HandlerDispatcher BuildDispatcher(string methodName)
        => BuildDispatcher<OrderHandler>(methodName);

    private HandlerDispatcher BuildDispatcher<THandler>(string methodName) where THandler : EventHandler
    {
        var binding = BindingFor<THandler>(methodName);
        var pattern = new RoutePattern(binding.Topic.Pattern);
        var scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();
        return new HandlerDispatcher(binding, pattern, scopeFactory, _publisher, _serializer, NullLoggerFactory.Instance);
    }

    private static HandlerBinding BindingFor<THandler>(string methodName)
    {
        var type = typeof(THandler);
        var method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance)!;
        var exchange = type.GetCustomAttribute<ExchangeAttribute>()!;
        var topic = method.GetCustomAttribute<TopicAttribute>()!;
        return new HandlerBinding(type, exchange, topic, method);
    }

    private static BasicDeliverEventArgs MakeEvent(string routingKey, byte[]? body = null, ulong deliveryTag = 1)
        => new(
            consumerTag: "test-consumer",
            deliveryTag: deliveryTag,
            redelivered: false,
            exchange: "test-orders",
            routingKey: routingKey,
            properties: Substitute.For<IReadOnlyBasicProperties>(),
            body: body ?? []);
}
