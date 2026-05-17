using Bunny;
using Bunny.Internal;
using Bunny.Tests.Fixtures;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;
using Xunit;

namespace Bunny.Tests;

public class BunnyBuilderTests
{
    [Fact]
    public void ScanAssembly_collects_assembly()
    {
        var builder = new BunnyBuilder();
        builder.ScanAssembly(typeof(OrderHandler).Assembly);
        builder.Assemblies.Should().ContainSingle().Which.Should().BeSameAs(typeof(OrderHandler).Assembly);
    }

    [Fact]
    public void DeclareExchange_collects_explicit_exchange()
    {
        var builder = new BunnyBuilder();
        builder.DeclareExchange("audit", ExchangeType.Direct, durable: false, autoDelete: true);
        var exchange = builder.ExplicitExchanges.Should().ContainSingle().Subject;
        exchange.Name.Should().Be("audit");
        exchange.Type.Should().Be(ExchangeType.Direct);
        exchange.Durable.Should().BeFalse();
        exchange.AutoDelete.Should().BeTrue();
    }

    [Fact]
    public void ConfigureFrom_overrides_default_section()
    {
        var builder = new BunnyBuilder();
        builder.ConfigureFrom("MyApp:Messaging");
        builder.ConfigurationSection.Should().Be("MyApp:Messaging");
    }

    [Fact]
    public void Fluent_methods_return_builder_for_chaining()
    {
        var builder = new BunnyBuilder();
        var chain = builder
            .ScanAssembly(typeof(OrderHandler).Assembly)
            .DeclareExchange("audit")
            .ConfigureFrom("section");
        chain.Should().BeSameAs(builder);
    }
}

public class AddBunnyTests
{
    [Fact]
    public void Discovers_handlers_from_scanned_assembly_and_registers_them_scoped()
    {
        var services = new ServiceCollection();
        services.AddBunny(b => b.ScanAssembly(typeof(OrderHandler).Assembly));

        var descriptor = services.Should().Contain(s => s.ServiceType == typeof(OrderHandler)).Subject;
        descriptor.Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    [Fact]
    public void Registers_hosted_service()
    {
        var services = new ServiceCollection();
        services.AddBunny(b => b.ScanAssembly(typeof(OrderHandler).Assembly));

        services.Should().Contain(s => s.ServiceType == typeof(IHostedService));
    }

    [Fact]
    public void Explicit_exchange_appears_in_registry_in_addition_to_handler_bound()
    {
        var services = new ServiceCollection();
        services.AddBunny(b => b
            .ScanAssembly(typeof(OrderHandler).Assembly)
            .DeclareExchange("audit", ExchangeType.Fanout));

        var registry = services.BuildServiceProvider().GetRequiredService<BunnyRegistry>();
        registry.Exchanges.Select(e => e.Name).Should().Contain(["test-orders", "test-audit", "audit"]);
    }

    [Fact]
    public void Explicit_exchange_with_same_name_as_handler_bound_does_not_duplicate()
    {
        var services = new ServiceCollection();
        services.AddBunny(b => b
            .ScanAssembly(typeof(OrderHandler).Assembly)
            .DeclareExchange("test-orders"));

        var registry = services.BuildServiceProvider().GetRequiredService<BunnyRegistry>();
        registry.Exchanges.Count(e => e.Name == "test-orders").Should().Be(1);
    }

    [Fact]
    public void Without_ScanAssembly_no_handlers_are_registered_but_core_services_are()
    {
        var services = new ServiceCollection();
        services.AddBunny(b => b.DeclareExchange("audit"));

        var sp = services.BuildServiceProvider();
        sp.GetService<IBunnyPublisher>().Should().NotBeNull();
        sp.GetService<IBunnySerializer>().Should().NotBeNull();
        sp.GetRequiredService<BunnyRegistry>().Bindings.Should().BeEmpty();
        sp.GetRequiredService<BunnyRegistry>().Exchanges.Select(e => e.Name).Should().BeEquivalentTo(["audit"]);
    }
}

public class AddBunnyPublisherTests
{
    [Fact]
    public void Registers_publisher_without_scanning()
    {
        var services = new ServiceCollection();
        services.AddBunnyPublisher(b => b.DeclareExchange("orders"));

        var sp = services.BuildServiceProvider();
        sp.GetService<IBunnyPublisher>().Should().NotBeNull();
        sp.GetRequiredService<BunnyRegistry>().Bindings.Should().BeEmpty();
        sp.GetRequiredService<BunnyRegistry>().HandlerTypes.Should().BeEmpty();
    }

    [Fact]
    public void Declares_explicit_exchanges()
    {
        var services = new ServiceCollection();
        services.AddBunnyPublisher(b => b
            .DeclareExchange("orders")
            .DeclareExchange("audit", RabbitMQ.Client.ExchangeType.Fanout, durable: false));

        var registry = services.BuildServiceProvider().GetRequiredService<BunnyRegistry>();
        registry.Exchanges.Should().HaveCount(2);
        registry.Exchanges.Should().Contain(e => e.Name == "orders" && e.Type == RabbitMQ.Client.ExchangeType.Topic && e.Durable);
        registry.Exchanges.Should().Contain(e => e.Name == "audit" && e.Type == RabbitMQ.Client.ExchangeType.Fanout && !e.Durable);
    }

    [Fact]
    public void ConfigureFrom_overrides_default_section_on_builder()
    {
        var builder = new BunnyPublisherBuilder();
        builder.ConfigureFrom("MyApp:Bus");
        builder.ConfigurationSection.Should().Be("MyApp:Bus");
    }

    [Fact]
    public void Fluent_methods_return_builder_for_chaining()
    {
        var builder = new BunnyPublisherBuilder();
        var chain = builder
            .DeclareExchange("orders")
            .ConfigureFrom("section");
        chain.Should().BeSameAs(builder);
    }

    [Fact]
    public void Works_with_no_configuration_at_all()
    {
        var services = new ServiceCollection();
        services.AddBunnyPublisher();
        var sp = services.BuildServiceProvider();
        sp.GetService<IBunnyPublisher>().Should().NotBeNull();
    }
}
