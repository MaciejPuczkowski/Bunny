namespace Bunny;

/// <summary>
/// Outcome of a topic method. Returned from a handler method (or constructed via the
/// <see cref="EventHandler.Ack"/> / <see cref="EventHandler.Nack(bool)"/> /
/// <see cref="EventHandler.Reject(bool)"/> shortcuts in the base class) to tell the broker
/// what to do with the message. Methods that return <c>void</c> / <c>Task</c> / <c>ValueTask</c>
/// implicitly produce <see cref="Ack"/>.
/// </summary>
/// <example>
/// <code><![CDATA[
/// [Topic("order.{id:guid}.created")]
/// public async Task<AckResult> OnCreated(Guid id, CancellationToken ct)
/// {
///     if (!TryBodyAs<OrderDto>(out var dto)) return Reject(requeue: false);  // malformed
///     return await service.TryHandle(dto, ct) ? Ack() : Nack(requeue: true); // transient
/// }
/// ]]></code>
/// </example>
public readonly struct AckResult
{
    internal AckOp Op { get; }
    internal bool Requeue { get; }

    private AckResult(AckOp op, bool requeue)
    {
        Op = op;
        Requeue = requeue;
    }

    /// <summary>Successfully processed — broker removes the message permanently.</summary>
    public static AckResult Ack() => new(AckOp.Ack, false);

    /// <summary>
    /// Negative acknowledge. <paramref name="requeue"/> = true puts the message back on the queue
    /// (potential poison-message loop); false discards or routes to DLX if configured on the queue.
    /// </summary>
    public static AckResult Nack(bool requeue = false) => new(AckOp.Nack, requeue);

    /// <summary>
    /// Reject this single message. Semantically equivalent to <see cref="Nack(bool)"/> with
    /// <c>multiple: false</c>; conventionally used when the message itself is invalid (malformed,
    /// business reject) rather than the consumer being unable to process.
    /// </summary>
    public static AckResult Reject(bool requeue = false) => new(AckOp.Reject, requeue);
}

internal enum AckOp : byte
{
    Ack,
    Nack,
    Reject
}
