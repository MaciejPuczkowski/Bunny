using Bunny;
using Bunny.Tests.Fixtures;
using FluentAssertions;
using Xunit;

namespace Bunny.Tests;

public class JsonBunnySerializerTests
{
    private readonly IBunnySerializer _serializer = new JsonBunnySerializer();

    [Fact]
    public void ContentType_is_application_json()
        => _serializer.ContentType.Should().Be("application/json");

    [Fact]
    public void Roundtrip_preserves_record_value()
    {
        var original = new OrderDto("widget", 9.99m);
        var bytes = _serializer.Serialize(original);
        var decoded = _serializer.Deserialize<OrderDto>(bytes);
        decoded.Should().Be(original);
    }

    [Fact]
    public void Deserialize_is_case_insensitive()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes("{\"NAME\":\"X\",\"AMOUNT\":1}");
        var dto = _serializer.Deserialize<OrderDto>(bytes);
        dto.Should().Be(new OrderDto("X", 1m));
    }
}
