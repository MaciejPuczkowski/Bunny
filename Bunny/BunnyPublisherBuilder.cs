using RabbitMQ.Client;

namespace Bunny;

/// <summary>
/// Fluent configuration for publisher-only Bunny (no consumer scanning). Use in services that only
/// publish to RabbitMQ (REST APIs, background jobs that emit events) — no handler discovery, no
/// queue declarations, no consumer channels.
/// </summary>
/// <example>
/// <code><![CDATA[
/// // In a REST API that only emits domain events
/// builder.Services.AddBunnyPublisher(b => b
///     .DeclareExchange("orders")
///     .DeclareExchange("audit"));
///
/// // Then inject IBunnyPublisher anywhere
/// public class CheckoutController(IBunnyPublisher bus) : ControllerBase { ... }
/// ]]></code>
/// </example>
public sealed class BunnyPublisherBuilder
{
    private readonly List<ExchangeAttribute> _exchanges = [];

    /// <summary>Configuration section name bound to <see cref="BunnyOptions"/>. Default <c>"Bunny"</c>.</summary>
    public string ConfigurationSection { get; private set; } = "Bunny";

    internal IReadOnlyList<ExchangeAttribute> Exchanges => _exchanges;

    /// <summary>
    /// Declares an exchange at startup. Idempotent — safe if it already exists with the same
    /// parameters. Required when you publish to an exchange that no other component creates.
    /// </summary>
    public BunnyPublisherBuilder DeclareExchange(string name, string type = ExchangeType.Topic, bool durable = true, bool autoDelete = false)
    {
        _exchanges.Add(new ExchangeAttribute(name, type, durable, autoDelete));
        return this;
    }

    /// <summary>Overrides the configuration section name (default <c>"Bunny"</c>) bound to <see cref="BunnyOptions"/>.</summary>
    public BunnyPublisherBuilder ConfigureFrom(string sectionName)
    {
        ConfigurationSection = sectionName;
        return this;
    }
}
