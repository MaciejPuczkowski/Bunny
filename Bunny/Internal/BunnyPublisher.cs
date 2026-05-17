using RabbitMQ.Client;

namespace Bunny.Internal;

internal sealed class BunnyPublisher(BunnyConnectionState state, IBunnySerializer serializer) : IBunnyPublisher
{
    public Task PublishAsync<T>(string exchange, string routingKey, T message, CancellationToken cancellationToken = default)
        => PublishAsync(exchange, routingKey, message, _ => { }, cancellationToken);

    public async Task PublishAsync<T>(string exchange, string routingKey, T message, Action<BasicProperties> configure, CancellationToken cancellationToken = default)
    {
        await state.PublishLock.WaitAsync(cancellationToken);
        try { await DoPublish(exchange, routingKey, message, configure, cancellationToken); }
        finally { state.PublishLock.Release(); }
    }

    private async Task DoPublish<T>(string exchange, string routingKey, T message, Action<BasicProperties> configure, CancellationToken ct)
    {
        var body = serializer.Serialize(message);
        var props = BuildProperties(configure);
        await state.PublishChannel.BasicPublishAsync(exchange, routingKey, mandatory: true, basicProperties: props, body: body, cancellationToken: ct);
    }

    private BasicProperties BuildProperties(Action<BasicProperties> configure)
    {
        var props = new BasicProperties
        {
            ContentType = serializer.ContentType,
            DeliveryMode = DeliveryModes.Persistent
        };
        configure(props);
        return props;
    }
}
