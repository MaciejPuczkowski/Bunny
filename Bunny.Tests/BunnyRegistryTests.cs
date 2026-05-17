using Bunny.Internal;
using Bunny.Tests.Fixtures;
using FluentAssertions;
using Xunit;

namespace Bunny.Tests;

public class BunnyRegistryTests
{
    [Fact]
    public void Scan_discovers_concrete_handlers_with_exchange_attribute()
    {
        var registry = new BunnyRegistry();
        registry.Scan(typeof(OrderHandler).Assembly);
        registry.HandlerTypes.Should().Contain(typeof(OrderHandler)).And.Contain(typeof(AuditHandler));
    }

    [Fact]
    public void Scan_skips_abstract_types()
    {
        var registry = new BunnyRegistry();
        registry.Scan(typeof(OrderHandler).Assembly);
        registry.HandlerTypes.Should().NotContain(typeof(AbstractHandler));
    }

    [Fact]
    public void Scan_skips_types_without_exchange_attribute()
    {
        var registry = new BunnyRegistry();
        registry.Scan(typeof(OrderHandler).Assembly);
        registry.HandlerTypes.Should().NotContain(typeof(InvocationRecord));
    }

    [Fact]
    public void Scan_discovers_all_topic_methods_per_handler()
    {
        var registry = new BunnyRegistry();
        registry.Scan(typeof(OrderHandler).Assembly);
        var orderBindings = registry.Bindings.Where(b => b.HandlerType == typeof(OrderHandler)).ToList();
        orderBindings.Should().HaveCount(5);
    }

    [Fact]
    public void Exchanges_returns_distinct_exchange_names()
    {
        var registry = new BunnyRegistry();
        registry.Scan(typeof(OrderHandler).Assembly);
        registry.Exchanges.Select(e => e.Name).Should().Contain(["test-orders", "test-audit"]).And.OnlyHaveUniqueItems();
    }

    [Fact]
    public void Handler_with_exchange_but_no_topics_contributes_no_bindings()
    {
        var registry = new BunnyRegistry();
        registry.Scan(typeof(OrderHandler).Assembly);
        registry.Bindings.Should().NotContain(b => b.HandlerType == typeof(HandlerWithoutTopics));
    }
}
