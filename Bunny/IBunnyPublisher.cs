using RabbitMQ.Client;

namespace Bunny;

/// <summary>
/// Singleton publisher injectable anywhere via DI. Inside an <see cref="EventHandler"/>, prefer
/// <c>this.PublishAsync(...)</c> which delegates to the same instance.
/// </summary>
/// <example>
/// <code><![CDATA[
/// public class CheckoutService(IBunnyPublisher bus)
/// {
///     public Task EmitOrderCreated(Order order, CancellationToken ct)
///         => bus.PublishAsync("orders", $"order.{order.Id}.created", order, ct);
/// }
/// ]]></code>
/// </example>
public interface IBunnyPublisher
{
    /// <summary>Publishes a message serialized with the configured <see cref="IBunnySerializer"/>.</summary>
    Task PublishAsync<T>(string exchange, string routingKey, T message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes with a callback that lets you set additional AMQP properties (correlation id,
    /// expiration, headers, ...). Defaults applied before the callback: <c>ContentType</c>,
    /// <c>DeliveryMode = Persistent</c>.
    /// </summary>
    Task PublishAsync<T>(string exchange, string routingKey, T message, Action<BasicProperties> configure, CancellationToken cancellationToken = default);
}
