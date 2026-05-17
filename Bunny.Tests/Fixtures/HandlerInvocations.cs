namespace Bunny.Tests.Fixtures;

public sealed class HandlerInvocations
{
    public List<InvocationRecord> Calls { get; } = [];
}

public sealed record InvocationRecord(
    string Method,
    string RoutingKey,
    IReadOnlyDictionary<string, object?> RouteParameters,
    object? Body,
    bool HasCancellationToken
);
