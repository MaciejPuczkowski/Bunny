using System.Reflection;
using RabbitMQ.Client;

namespace Bunny;

/// <summary>
/// Fluent configuration passed to <see cref="ServiceCollectionExtensions.AddBunny"/>. Use it to
/// declare which assemblies to scan for handlers and which extra exchanges to declare at startup
/// (e.g., publish-only exchanges that no handler consumes from).
/// </summary>
/// <example>
/// <code><![CDATA[
/// builder.Services.AddBunny(b => b
///     .ScanAssembly(typeof(Program).Assembly)
///     .DeclareExchange("audit")
///     .DeclareExchange("dlx", durable: true)
///     .ConfigureFrom("MyApp:Messaging"));
/// ]]></code>
/// </example>
public sealed class BunnyBuilder
{
    private readonly List<Assembly> _assemblies = [];
    private readonly List<ExchangeAttribute> _explicitExchanges = [];

    /// <summary>Configuration section name bound to <see cref="BunnyOptions"/>. Default <c>"Bunny"</c>.</summary>
    public string ConfigurationSection { get; private set; } = "Bunny";

    internal IReadOnlyList<Assembly> Assemblies => _assemblies;
    internal IReadOnlyList<ExchangeAttribute> ExplicitExchanges => _explicitExchanges;

    /// <summary>Adds an assembly to scan for <see cref="EventHandler"/>-derived types.</summary>
    public BunnyBuilder ScanAssembly(Assembly assembly)
    {
        _assemblies.Add(assembly);
        return this;
    }

    /// <summary>Adds multiple assemblies to scan.</summary>
    public BunnyBuilder ScanAssemblies(params Assembly[] assemblies)
    {
        _assemblies.AddRange(assemblies);
        return this;
    }

    /// <summary>
    /// Declares an exchange at startup independent of any handler binding. Use for publish-only
    /// exchanges (you publish to them but no handler in this service consumes from them).
    /// Idempotent — safe if the exchange already exists with the same parameters.
    /// </summary>
    public BunnyBuilder DeclareExchange(string name, string type = ExchangeType.Topic, bool durable = true, bool autoDelete = false)
    {
        _explicitExchanges.Add(new ExchangeAttribute(name, type, durable, autoDelete));
        return this;
    }

    /// <summary>Overrides the configuration section name (default <c>"Bunny"</c>) bound to <see cref="BunnyOptions"/>.</summary>
    public BunnyBuilder ConfigureFrom(string sectionName)
    {
        ConfigurationSection = sectionName;
        return this;
    }
}
