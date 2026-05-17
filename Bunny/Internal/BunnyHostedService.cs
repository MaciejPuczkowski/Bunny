using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Bunny.Internal;

internal sealed class BunnyHostedService(
    IOptions<BunnyOptions> options,
    BunnyRegistry registry,
    BunnyConnectionState state,
    IBunnyPublisher publisher,
    IBunnySerializer serializer,
    IServiceScopeFactory scopeFactory,
    ILoggerFactory loggerFactory,
    ILogger<BunnyHostedService> logger
) : IHostedService, IAsyncDisposable
{
    private readonly List<IChannel> _channels = [];

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await OpenConnectionAsync(cancellationToken);
        await DeclareExchangesAsync(cancellationToken);
        await StartConsumersAsync(cancellationToken);
        logger.LogInformation("Bunny started: {Bindings} binding(s) on {Host}:{Port}", registry.Bindings.Count, options.Value.HostName, options.Value.Port);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await CloseChannelsAsync();
        await CloseConnectionAsync();
        state.Reset();
    }

    public async ValueTask DisposeAsync() => await StopAsync(CancellationToken.None);

    private async Task OpenConnectionAsync(CancellationToken ct)
    {
        var factory = BuildFactory();
        var connection = await factory.CreateConnectionAsync(ct);
        var publishChannel = await connection.CreateChannelAsync(cancellationToken: ct);
        state.SetConnection(connection);
        state.SetPublishChannel(publishChannel);
    }

    private ConnectionFactory BuildFactory()
    {
        var o = options.Value;
        return new ConnectionFactory
        {
            HostName = o.HostName,
            Port = o.Port,
            UserName = o.UserName,
            Password = o.Password,
            VirtualHost = o.VirtualHost,
            ClientProvidedName = o.ClientProvidedName
        };
    }

    private async Task DeclareExchangesAsync(CancellationToken ct)
    {
        foreach (var exchange in registry.Exchanges) await DeclareExchangeAsync(exchange, ct);
    }

    private Task DeclareExchangeAsync(ExchangeAttribute exchange, CancellationToken ct)
        => state.PublishChannel.ExchangeDeclareAsync(exchange.Name, exchange.Type, exchange.Durable, exchange.AutoDelete, cancellationToken: ct);

    private async Task StartConsumersAsync(CancellationToken ct)
    {
        foreach (var binding in registry.Bindings) await StartConsumerAsync(binding, ct);
    }

    private async Task StartConsumerAsync(HandlerBinding binding, CancellationToken ct)
    {
        var channel = await state.Connection.CreateChannelAsync(cancellationToken: ct);
        _channels.Add(channel);
        await ConfigureChannelAsync(channel, binding, ct);
        var pattern = new RoutePattern(binding.Topic.Pattern);
        var queue = await DeclareAndBindQueueAsync(channel, binding, pattern, ct);
        AttachConsumer(channel, binding, pattern, queue, ct);
        logger.LogInformation("Bound {Type}.{Method} -> {Exchange} / {Pattern} (queue: {Queue})", binding.HandlerType.Name, binding.Method.Name, binding.Exchange.Name, binding.Topic.Pattern, queue);
    }

    private Task ConfigureChannelAsync(IChannel channel, HandlerBinding binding, CancellationToken ct)
        => channel.BasicQosAsync(prefetchSize: 0, prefetchCount: ResolvePrefetch(binding), global: false, ct);

    private ushort ResolvePrefetch(HandlerBinding binding)
        => binding.Topic.Prefetch > 0 ? binding.Topic.Prefetch : options.Value.DefaultPrefetch;

    private static async Task<string> DeclareAndBindQueueAsync(IChannel channel, HandlerBinding binding, RoutePattern pattern, CancellationToken ct)
    {
        var queueName = await DeclareQueueAsync(channel, binding, ct);
        await channel.QueueBindAsync(queueName, binding.Exchange.Name, pattern.BindingKey, cancellationToken: ct);
        return queueName;
    }

    private static async Task<string> DeclareQueueAsync(IChannel channel, HandlerBinding binding, CancellationToken ct)
    {
        var configured = binding.Topic.Queue;
        var exclusive = string.IsNullOrEmpty(configured);
        var result = await channel.QueueDeclareAsync(configured, binding.Topic.Durable && !exclusive, exclusive, binding.Topic.AutoDelete, cancellationToken: ct);
        return result.QueueName;
    }

    private void AttachConsumer(IChannel channel, HandlerBinding binding, RoutePattern pattern, string queue, CancellationToken hostToken)
    {
        var dispatcher = new HandlerDispatcher(binding, pattern, scopeFactory, publisher, serializer, loggerFactory);
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, ea) => dispatcher.DispatchAsync(channel, ea, hostToken);
        _ = channel.BasicConsumeAsync(queue, autoAck: false, consumer, hostToken);
    }

    private async Task CloseChannelsAsync()
    {
        foreach (var channel in _channels) await TryCloseAsync(channel);
        _channels.Clear();
    }

    private async Task TryCloseAsync(IChannel channel)
    {
        try { await channel.CloseAsync(); }
        catch (Exception ex) { logger.LogWarning(ex, "Error while closing channel"); }
    }

    private async Task CloseConnectionAsync()
    {
        if (!state.IsReady) return;
        await TryCloseConnectionAsync();
    }

    private async Task TryCloseConnectionAsync()
    {
        try { await state.Connection.CloseAsync(); }
        catch (Exception ex) { logger.LogWarning(ex, "Error while closing connection"); }
    }
}
