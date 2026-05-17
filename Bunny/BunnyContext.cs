using System.Text;
using RabbitMQ.Client;

namespace Bunny;

public sealed class BunnyContext(
    IBunnySerializer serializer,
    string consumerTag,
    ulong deliveryTag,
    bool redelivered,
    string exchange,
    string routingKey,
    IReadOnlyBasicProperties properties,
    ReadOnlyMemory<byte> body,
    IReadOnlyDictionary<string, object?> routeParameters,
    CancellationToken cancellationToken)
{
    public string ConsumerTag { get; } = consumerTag;
    public ulong DeliveryTag { get; } = deliveryTag;
    public bool Redelivered { get; } = redelivered;
    public string Exchange { get; } = exchange;
    public string RoutingKey { get; } = routingKey;
    public IReadOnlyBasicProperties Properties { get; } = properties;
    public CancellationToken CancellationToken { get; } = cancellationToken;
    public ReadOnlyMemory<byte> Body => body;
    public IReadOnlyDictionary<string, object?> RouteParameters => routeParameters;

    public string BodyAsString() => Encoding.UTF8.GetString(body.Span);

    public T? BodyAs<T>() => serializer.Deserialize<T>(body.Span);

    public bool TryBodyAs<T>(out T? value) => TryDeserialize(out value);

    public T? Route<T>(string name)
        => routeParameters.TryGetValue(name, out var raw) && raw is not null
            ? CoerceRoute<T>(raw)
            : default;

    private bool TryDeserialize<T>(out T? value)
    {
        try { value = BodyAs<T>(); return value is not null; }
        catch { value = default; return false; }
    }

    private static T CoerceRoute<T>(object raw)
        => raw is T cast ? cast : (T)Convert.ChangeType(raw, typeof(T))!;
}
