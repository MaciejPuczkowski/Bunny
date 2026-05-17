namespace Bunny.Tests.Fixtures;

public sealed record OrderDto(string Name, decimal Amount);

[Exchange("test-orders")]
public class OrderHandler(HandlerInvocations recorder) : EventHandler
{
    [Topic("order.{id:guid}.created")]
    public Task OnCreated(Guid id, CancellationToken ct)
    {
        Record(nameof(OnCreated), new Dictionary<string, object?> { ["id"] = id }, BodyAs<OrderDto>(), ct);
        return Task.CompletedTask;
    }

    [Topic("order.{id:guid}.cancelled", RequeueOnError = true)]
    public AckResult OnCancelled(Guid id)
    {
        Record(nameof(OnCancelled), new Dictionary<string, object?> { ["id"] = id }, null, false);
        return Nack(requeue: true);
    }

    [Topic("order.fail")]
    public Task OnFail()
        => throw new InvalidOperationException("boom");

    [Topic("order.{id:guid}.fast")]
    public async Task OnFast(Guid id, CancellationToken ct)
    {
        await AckNowAsync();
        Record(nameof(OnFast), new Dictionary<string, object?> { ["id"] = id }, null, ct);
    }

    [Topic("order.{id:guid}.reject")]
    public async Task OnRejectEarly(Guid id)
    {
        await RejectNowAsync(requeue: false);
        Record(nameof(OnRejectEarly), new Dictionary<string, object?> { ["id"] = id }, null, false);
    }

    [Topic("order.{id:guid}.frombody")]
    public Task OnFromBody(Guid id, [FromBody] OrderDto dto, CancellationToken ct)
    {
        Record(nameof(OnFromBody), new Dictionary<string, object?> { ["id"] = id }, dto, ct);
        return Task.CompletedTask;
    }

    private void Record(string method, IReadOnlyDictionary<string, object?> routeParams, object? body, CancellationToken ct)
        => recorder.Calls.Add(new InvocationRecord(method, RoutingKey, routeParams, body, ct.CanBeCanceled));

    private void Record(string method, IReadOnlyDictionary<string, object?> routeParams, object? body, bool hasToken)
        => recorder.Calls.Add(new InvocationRecord(method, RoutingKey, routeParams, body, hasToken));
}

[Exchange("test-audit")]
public class AuditHandler(HandlerInvocations recorder) : EventHandler
{
    [Topic("audit.{source:string}.event")]
    public Task OnEvent(string source)
    {
        recorder.Calls.Add(new InvocationRecord(
            nameof(OnEvent),
            RoutingKey,
            new Dictionary<string, object?> { ["source"] = source },
            BodyAsString(),
            false));
        return Task.CompletedTask;
    }
}

public abstract class AbstractHandler : EventHandler
{
    [Topic("nope")]
    public Task ShouldNotBeDiscovered() => Task.CompletedTask;
}

[Exchange("no-topics")]
public class HandlerWithoutTopics : EventHandler;
