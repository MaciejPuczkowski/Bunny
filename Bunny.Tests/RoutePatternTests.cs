using Bunny.Internal;
using FluentAssertions;
using Xunit;

namespace Bunny.Tests;

public class RoutePatternTests
{
    [Fact]
    public void BindingKey_replaces_typed_params_with_star()
    {
        var pattern = new RoutePattern("order.{id:guid}.created");
        pattern.BindingKey.Should().Be("order.*.created");
    }

    [Fact]
    public void BindingKey_passes_through_when_no_params()
    {
        var pattern = new RoutePattern("order.created");
        pattern.BindingKey.Should().Be("order.created");
    }

    [Fact]
    public void TryMatch_extracts_guid()
    {
        var id = Guid.NewGuid();
        var pattern = new RoutePattern("order.{id:guid}.created");
        var matched = pattern.TryMatch($"order.{id}.created", out var values);
        matched.Should().BeTrue();
        values["id"].Should().Be(id);
    }

    [Fact]
    public void TryMatch_extracts_int()
    {
        var pattern = new RoutePattern("item.{qty:int}");
        var matched = pattern.TryMatch("item.42", out var values);
        matched.Should().BeTrue();
        values["qty"].Should().Be(42);
    }

    [Fact]
    public void TryMatch_extracts_multiple_params()
    {
        var pattern = new RoutePattern("metrics.{source:string}.count.{value:long}");
        var matched = pattern.TryMatch("metrics.app1.count.99999", out var values);
        matched.Should().BeTrue();
        values["source"].Should().Be("app1");
        values["value"].Should().Be(99999L);
    }

    [Fact]
    public void TryMatch_returns_false_on_segment_count_mismatch()
    {
        var pattern = new RoutePattern("order.{id:guid}.created");
        var matched = pattern.TryMatch("order.123.created.extra", out _);
        matched.Should().BeFalse();
    }

    [Fact]
    public void TryMatch_returns_false_on_literal_mismatch()
    {
        var pattern = new RoutePattern("order.{id:guid}.created");
        var matched = pattern.TryMatch("order.123.updated", out _);
        matched.Should().BeFalse();
    }

    [Fact]
    public void TryMatch_with_no_params_validates_exact_key()
    {
        var pattern = new RoutePattern("order.created");
        pattern.TryMatch("order.created", out _).Should().BeTrue();
        pattern.TryMatch("order.updated", out _).Should().BeFalse();
    }

    [Fact]
    public void Constructor_throws_on_unsupported_param_type()
    {
        var act = () => new RoutePattern("foo.{x:weird}");
        act.Should().Throw<ArgumentException>().WithMessage("*weird*");
    }
}
