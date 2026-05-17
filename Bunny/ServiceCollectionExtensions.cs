using Bunny.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bunny;

/// <summary>
/// Bunny registration on <see cref="IServiceCollection"/>. Adds the hosted service that opens the
/// RabbitMQ connection, scans the configured assemblies for handlers, registers them as scoped
/// and declares all exchanges (handler-bound + explicitly declared) at startup.
/// </summary>
/// <example>
/// <code><![CDATA[
/// // Program.cs (works in WebApplication or generic Host)
/// builder.Services.AddBunny(b => b
///     .ScanAssembly(typeof(Program).Assembly)
///     .DeclareExchange("audit"));        // publish-only, no handler consumes it
///
/// // appsettings.json
/// // "Bunny": { "HostName": "rabbitmq", "Port": 5672, "UserName": "guest", "Password": "guest" }
/// ]]></code>
/// </example>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers Bunny with fluent configuration. Use the <see cref="BunnyBuilder"/> callback to
    /// declare scanned assemblies, extra exchanges and the configuration section name.
    /// </summary>
    public static IServiceCollection AddBunny(this IServiceCollection services, Action<BunnyBuilder> configure)
    {
        var builder = BuildConfig(configure);
        BindOptions(services, builder.ConfigurationSection);
        RegisterCoreServices(services);
        RegisterDiscoveredHandlers(services, builder);
        return services;
    }

    /// <summary>
    /// Registers publisher-only Bunny — opens the RabbitMQ connection and exposes
    /// <see cref="IBunnyPublisher"/> in DI, without scanning for handlers or starting consumers.
    /// Use in services that only emit events (REST APIs, background jobs).
    /// </summary>
    public static IServiceCollection AddBunnyPublisher(this IServiceCollection services, Action<BunnyPublisherBuilder>? configure = null)
    {
        var publisherBuilder = BuildPublisherConfig(configure);
        return AddBunny(services, b => BridgeToBunnyBuilder(b, publisherBuilder));
    }

    private static BunnyBuilder BridgeToBunnyBuilder(BunnyBuilder target, BunnyPublisherBuilder source)
    {
        target.ConfigureFrom(source.ConfigurationSection);
        foreach (var e in source.Exchanges) target.DeclareExchange(e.Name, e.Type, e.Durable, e.AutoDelete);
        return target;
    }

    private static BunnyPublisherBuilder BuildPublisherConfig(Action<BunnyPublisherBuilder>? configure)
    {
        var builder = new BunnyPublisherBuilder();
        configure?.Invoke(builder);
        return builder;
    }

    private static BunnyBuilder BuildConfig(Action<BunnyBuilder> configure)
    {
        var builder = new BunnyBuilder();
        configure(builder);
        return builder;
    }

    private static void BindOptions(IServiceCollection services, string sectionName)
        => services.AddOptions<BunnyOptions>().BindConfiguration(sectionName);

    private static void RegisterCoreServices(IServiceCollection services)
    {
        services.TryAddSingleton<IBunnySerializer>(_ => new JsonBunnySerializer());
        services.TryAddSingleton<BunnyConnectionState>();
        services.TryAddSingleton<IBunnyPublisher, BunnyPublisher>();
        services.TryAddSingleton<BunnyRegistry>();
        services.AddHostedService<BunnyHostedService>();
    }

    private static void RegisterDiscoveredHandlers(IServiceCollection services, BunnyBuilder builder)
    {
        var registry = BuildRegistry(builder);
        services.Replace(ServiceDescriptor.Singleton(registry));
        foreach (var type in registry.HandlerTypes) services.AddScoped(type);
    }

    private static BunnyRegistry BuildRegistry(BunnyBuilder builder)
    {
        var registry = new BunnyRegistry();
        foreach (var assembly in builder.Assemblies) registry.Scan(assembly);
        foreach (var exchange in builder.ExplicitExchanges) registry.DeclareExchange(exchange);
        return registry;
    }
}
