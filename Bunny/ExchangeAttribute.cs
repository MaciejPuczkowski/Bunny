using RabbitMQ.Client;

namespace Bunny;

/// <summary>
/// Binds the decorated handler class to a RabbitMQ exchange. The exchange is declared at startup
/// (idempotent — safe if it already exists) and every <see cref="TopicAttribute"/> method on the
/// class becomes a queue bound to this exchange.
/// </summary>
/// <example>
/// <code><![CDATA[
/// [Exchange("orders", ExchangeType.Topic, durable: true)]
/// public class OrderHandler : EventHandler
/// {
///     [Topic("order.{id:guid}.created")]
///     public Task OnCreated(Guid id) => ...;
/// }
/// ]]></code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class ExchangeAttribute(
    string name,
    string type = ExchangeType.Topic,
    bool durable = true,
    bool autoDelete = false
) : Attribute
{
    /// <summary>Exchange name as it appears in RabbitMQ.</summary>
    public string Name { get; } = name;

    /// <summary>Exchange type — use constants from <see cref="ExchangeType"/>. Defaults to topic.</summary>
    public string Type { get; } = type;

    /// <summary>If true the exchange survives broker restarts. Default true.</summary>
    public bool Durable { get; } = durable;

    /// <summary>If true the exchange is deleted once all bindings are gone. Default false.</summary>
    public bool AutoDelete { get; } = autoDelete;
}
