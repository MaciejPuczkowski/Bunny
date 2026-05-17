namespace Bunny;

/// <summary>
/// Binds a method to a routing pattern on the enclosing exchange. The pattern may contain typed
/// route parameters in the form <c>{name:type}</c> (supported types: int, long, guid, string,
/// bool, double, float) - same shape as ASP.NET HTTP route templates. The broker binding key
/// replaces each parameter with <c>*</c>; the dispatcher extracts values from the routing key
/// and binds them to method parameters by name.
/// </summary>
/// <example>
/// <code><![CDATA[
/// [Topic("order.{id:guid}.created", Queue = "orders.created", Prefetch = 20)]
/// public async Task<AckResult> OnCreated(Guid id, [FromBody] OrderCreatedDto dto, CancellationToken ct)
///     => await service.HandleAsync(id, dto, ct) ? Ack() : Nack(requeue: true);
/// ]]></code>
/// </example>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class TopicAttribute(string pattern) : Attribute
{
    /// <summary>Routing pattern. Typed parameters use <c>{name:type}</c> (e.g. <c>{id:guid}</c>).</summary>
    public string Pattern { get; } = pattern;

    /// <summary>Queue name. Empty (default) declares an exclusive auto-named queue per process.</summary>
    public string Queue { get; init; } = "";

    /// <summary>Survive broker restarts. Ignored when <see cref="Queue"/> is empty (exclusive). Default true.</summary>
    public bool Durable { get; init; } = true;

    /// <summary>Delete queue once last consumer disconnects. Default false.</summary>
    public bool AutoDelete { get; init; } = false;

    /// <summary>
    /// When the handler throws an unhandled exception, requeue the message instead of nack-no-requeue
    /// (i.e., instead of routing it to DLX / discarding). Use only for transient failures — a
    /// deterministic crash will loop forever.
    /// </summary>
    public bool RequeueOnError { get; init; } = false;

    /// <summary>Per-consumer prefetch limit. Default 10.</summary>
    public ushort Prefetch { get; init; } = 10;
}
