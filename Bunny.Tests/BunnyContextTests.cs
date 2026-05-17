using System.Text;
using Bunny;
using Bunny.Tests.Fixtures;
using FluentAssertions;
using NSubstitute;
using RabbitMQ.Client;
using Xunit;

namespace Bunny.Tests;

public class BunnyContextTests
{
    private static readonly IBunnySerializer Serializer = new JsonBunnySerializer();

    [Fact]
    public void BodyAsString_decodes_utf8()
    {
        var ctx = MakeContext(Encoding.UTF8.GetBytes("hello"));
        ctx.BodyAsString().Should().Be("hello");
    }

    [Fact]
    public void BodyAs_deserializes_json_to_record()
    {
        var json = "{\"name\":\"foo\",\"amount\":12.5}";
        var ctx = MakeContext(Encoding.UTF8.GetBytes(json));
        ctx.BodyAs<OrderDto>().Should().Be(new OrderDto("foo", 12.5m));
    }

    [Fact]
    public void TryBodyAs_returns_true_on_valid_payload()
    {
        var ctx = MakeContext(Encoding.UTF8.GetBytes("{\"name\":\"x\",\"amount\":1}"));
        var ok = ctx.TryBodyAs<OrderDto>(out var dto);
        ok.Should().BeTrue();
        dto!.Name.Should().Be("x");
    }

    [Fact]
    public void TryBodyAs_returns_false_on_malformed_payload()
    {
        var ctx = MakeContext(Encoding.UTF8.GetBytes("not-json"));
        var ok = ctx.TryBodyAs<OrderDto>(out var dto);
        ok.Should().BeFalse();
        dto.Should().BeNull();
    }

    [Fact]
    public void Route_returns_typed_value_when_present()
    {
        var id = Guid.NewGuid();
        var ctx = MakeContext([], new Dictionary<string, object?> { ["id"] = id });
        ctx.Route<Guid>("id").Should().Be(id);
    }

    [Fact]
    public void Route_returns_default_when_missing()
    {
        var ctx = MakeContext([], new Dictionary<string, object?>());
        ctx.Route<Guid>("missing").Should().Be(Guid.Empty);
    }

    private static BunnyContext MakeContext(byte[] body, IReadOnlyDictionary<string, object?>? routeParams = null)
        => new(
            Serializer,
            consumerTag: "tag-1",
            deliveryTag: 1UL,
            redelivered: false,
            exchange: "ex",
            routingKey: "rk",
            properties: Substitute.For<IReadOnlyBasicProperties>(),
            body: body,
            routeParameters: routeParams ?? new Dictionary<string, object?>(),
            cancellationToken: CancellationToken.None);
}
