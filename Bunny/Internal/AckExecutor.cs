using RabbitMQ.Client;

namespace Bunny.Internal;

internal static class AckExecutor
{
    public static async Task ApplyAsync(IChannel channel, ulong deliveryTag, AckResult ack, CancellationToken ct)
    {
        switch (ack.Op)
        {
            case AckOp.Ack: await channel.BasicAckAsync(deliveryTag, multiple: false, ct); break;
            case AckOp.Nack: await channel.BasicNackAsync(deliveryTag, multiple: false, ack.Requeue, ct); break;
            case AckOp.Reject: await channel.BasicRejectAsync(deliveryTag, ack.Requeue, ct); break;
        }
    }
}
