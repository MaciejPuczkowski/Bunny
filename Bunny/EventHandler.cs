using Bunny.Internal;
using RabbitMQ.Client;

namespace Bunny;

/// <summary>
/// Base class for routing-style RabbitMQ handlers. One instance is constructed per message inside
/// a dedicated DI scope (handlers are registered as scoped), so injecting <c>DbContext</c> or any
/// other scoped service is safe.
/// </summary>
/// <remarks>
/// The name clashes with <see cref="System.EventHandler"/> (a delegate). When both namespaces are
/// imported the compiler reports an ambiguity — either qualify as <c>Bunny.EventHandler</c> or add
/// <c>using EventHandler = Bunny.EventHandler;</c> to the file.
/// </remarks>
/// <example>
/// <code><![CDATA[
/// [Exchange("orders")]
/// public class OrderHandler(IOrderService orders, ILogger<OrderHandler> logger) : Bunny.EventHandler
/// {
///     [Topic("order.{id:guid}.created")]
///     public async Task OnCreated(Guid id, [FromBody] OrderCreatedDto dto, CancellationToken ct)
///     {
///         logger.LogInformation("Order {Id} from {RoutingKey}", id, RoutingKey);
///         await orders.HandleAsync(dto, ct);
///     }
/// }
/// ]]></code>
/// </example>
public abstract class EventHandler
{
    private BunnyContext? _context;
    private IBunnyPublisher? _publisher;
    private IChannel? _channel;
    private bool _replied;

    /// <summary>Per-message context: routing key, body, properties, route parameters.</summary>
    protected BunnyContext Context => Require(_context);

    /// <summary>Routing key the message was delivered with.</summary>
    protected string RoutingKey => Context.RoutingKey;

    /// <summary>Exchange the message was published to.</summary>
    protected string Exchange => Context.Exchange;

    /// <summary>AMQP basic properties of the delivered message.</summary>
    protected IReadOnlyBasicProperties Properties => Context.Properties;

    /// <summary>Host-level cancellation token — cancelled on graceful shutdown.</summary>
    protected CancellationToken CancellationToken => Context.CancellationToken;

    /// <summary>Raw message body.</summary>
    protected ReadOnlyMemory<byte> Body => Context.Body;

    /// <summary>Body decoded as UTF-8 string.</summary>
    protected string BodyAsString() => Context.BodyAsString();

    /// <summary>Body deserialized via the configured <see cref="IBunnySerializer"/>.</summary>
    protected T? BodyAs<T>() => Context.BodyAs<T>();

    /// <summary>Tries to deserialize the body; returns false on malformed payload instead of throwing.</summary>
    protected bool TryBodyAs<T>(out T? value) => Context.TryBodyAs(out value);

    /// <summary>Reads a typed route parameter extracted from the routing key.</summary>
    protected T? Route<T>(string name) => Context.Route<T>(name);

    /// <summary>Publishes a message via the singleton <see cref="IBunnyPublisher"/>.</summary>
    protected Task PublishAsync<T>(string exchange, string routingKey, T message, CancellationToken cancellationToken = default)
        => Require(_publisher).PublishAsync(exchange, routingKey, message, cancellationToken);

    /// <summary>Publishes with custom AMQP properties (correlation id, headers, expiration, ...).</summary>
    protected Task PublishAsync<T>(string exchange, string routingKey, T message, Action<BasicProperties> configure, CancellationToken cancellationToken = default)
        => Require(_publisher).PublishAsync(exchange, routingKey, message, configure, cancellationToken);

    /// <summary>Shortcut for <see cref="AckResult.Ack"/> — usable in <c>return Ack();</c>.</summary>
    protected static AckResult Ack() => AckResult.Ack();

    /// <summary>Shortcut for <see cref="AckResult.Nack(bool)"/> — usable in <c>return Nack(requeue: true);</c>.</summary>
    protected static AckResult Nack(bool requeue = false) => AckResult.Nack(requeue);

    /// <summary>Shortcut for <see cref="AckResult.Reject(bool)"/>.</summary>
    protected static AckResult Reject(bool requeue = false) => AckResult.Reject(requeue);

    /// <summary>
    /// Acks the message immediately (before the handler finishes), freeing a prefetch slot.
    /// Trades at-least-once for at-most-once: a crash after this call loses the message.
    /// Use for metrics / audit / fire-and-forget; the handler's return value is then ignored.
    /// </summary>
    protected Task AckNowAsync() => ReplyNowAsync(AckResult.Ack());

    /// <summary>Nack the message immediately. Return value is ignored after this call.</summary>
    protected Task NackNowAsync(bool requeue = false) => ReplyNowAsync(AckResult.Nack(requeue));

    /// <summary>Reject the message immediately. Return value is ignored after this call.</summary>
    protected Task RejectNowAsync(bool requeue = false) => ReplyNowAsync(AckResult.Reject(requeue));

    internal bool Replied => _replied;

    internal void Initialize(BunnyContext context, IBunnyPublisher publisher, IChannel channel)
    {
        _context = context;
        _publisher = publisher;
        _channel = channel;
        _replied = false;
    }

    private async Task ReplyNowAsync(AckResult result)
    {
        if (_replied) return;
        await AckExecutor.ApplyAsync(Require(_channel), Context.DeliveryTag, result, CancellationToken);
        _replied = true;
    }

    private static T Require<T>(T? value) where T : class
        => value ?? throw new InvalidOperationException("Handler is not initialized - access only inside a topic method.");
}
