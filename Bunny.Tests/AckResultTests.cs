using Bunny;
using FluentAssertions;
using Xunit;

namespace Bunny.Tests;

public class AckResultTests
{
    [Fact]
    public void Ack_factory_produces_ack_op_without_requeue()
    {
        var result = AckResult.Ack();
        result.Op.Should().Be(AckOp.Ack);
        result.Requeue.Should().BeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Nack_factory_carries_requeue_flag(bool requeue)
    {
        var result = AckResult.Nack(requeue);
        result.Op.Should().Be(AckOp.Nack);
        result.Requeue.Should().Be(requeue);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Reject_factory_carries_requeue_flag(bool requeue)
    {
        var result = AckResult.Reject(requeue);
        result.Op.Should().Be(AckOp.Reject);
        result.Requeue.Should().Be(requeue);
    }

    [Fact]
    public void Nack_defaults_to_no_requeue()
        => AckResult.Nack().Requeue.Should().BeFalse();

    [Fact]
    public void Reject_defaults_to_no_requeue()
        => AckResult.Reject().Requeue.Should().BeFalse();
}
