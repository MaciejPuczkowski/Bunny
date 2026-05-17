# Examples & recipes

**public**

Practical patterns. Each example assumes `using EventHandler = Bunny.EventHandler;` at the top of the file (or `: Bunny.EventHandler` instead of `: EventHandler`).

## Multiple topics on one handler

A handler can subscribe to as many topics as you like — each `[Topic]` becomes its own queue + consumer.

```csharp
[Exchange("orders")]
public class OrderHandler(IOrderService orders) : EventHandler
{
    [Topic("order.{id:guid}.created", Queue = "orders.created")]
    public Task OnCreated(Guid id, CancellationToken ct) => orders.HandleCreated(id, BodyAs<OrderDto>()!, ct);

    [Topic("order.{id:guid}.cancelled", Queue = "orders.cancelled")]
    public Task OnCancelled(Guid id, CancellationToken ct) => orders.HandleCancelled(id, ct);

    [Topic("order.{id:guid}.shipped", Queue = "orders.shipped")]
    public Task OnShipped(Guid id, CancellationToken ct) => orders.HandleShipped(id, ct);
}
```

## Route parameters

Supported types: `int`, `long`, `guid`, `string`, `bool`, `double`, `float`. Bind by **parameter name**:

```csharp
[Topic("metrics.{source:string}.count.{value:long}")]
public Task OnMetric(string source, long value)
{
    // source = "web", value = 12345  for routing key "metrics.web.count.12345"
    return Task.CompletedTask;
}
```

A routing key segment that fails to convert to the declared type behaves as a routing mismatch (`Nack(requeue: false)`) — same as if the segment count didn't match.

## Reading the body

Two styles, pick whichever fits the handler:

**1. `[FromBody]` parameter (recommended — matches ASP.NET controllers).** The body is deserialized *before* the handler runs and bound to the parameter by attribute, not by name:

```csharp
[Topic("order.{id:guid}.created")]
public async Task OnCreated(Guid id, [FromBody] OrderDto dto, CancellationToken ct)
{
    // dto is already populated when we get here
    await orders.HandleAsync(id, dto, ct);
}
```

If deserialization fails (malformed payload), the handler is never called — the dispatcher treats it as an exception and applies the topic's `RequeueOnError` policy (default: nack-without-requeue).

**2. Pull from the base class** when you want fine-grained control or graceful malformed handling:

```csharp
[Topic("...")]
public async Task Handle()
{
    ReadOnlyMemory<byte> raw = Body;             // raw bytes
    string text = BodyAsString();                // UTF-8 string
    OrderDto? dto = BodyAs<OrderDto>();          // deserialized (throws on malformed)
    if (TryBodyAs<OrderDto>(out var safe)) ...;  // deserialized, false on malformed - no throw
}
```

The two styles compose freely — one handler can take `[FromBody] OrderDto` while another in the same class uses `TryBodyAs<T>` to handle poison messages explicitly.

## Publishing from inside a handler

Publish to another exchange (e.g., audit, downstream events):

```csharp
[Topic("order.{id:guid}.created")]
public async Task OnCreated(Guid id, [FromBody] OrderCreatedDto dto, CancellationToken ct)
{
    await orders.HandleAsync(dto, ct);
    await PublishAsync("audit", $"order.audit.{id}", new AuditEntry(id, "created"), ct);
}
```

Or with custom AMQP properties:

```csharp
await PublishAsync("audit", "key", message, props =>
{
    props.CorrelationId = Context.Properties.CorrelationId;
    props.Headers = new Dictionary<string, object?> { ["x-source"] = "orders" };
}, ct);
```

## Ack / Nack / Reject — the return-value way

The most common pattern. The handler decides per-message:

```csharp
[Topic("order.{id:guid}.created", RequeueOnError = false)]
public async Task<AckResult> OnCreated(Guid id, [FromBody] OrderDto dto, CancellationToken ct)
{
    if (await orders.IsDuplicate(id, ct))  return Reject(requeue: false);   // business reject
    return await orders.TryHandle(dto, ct) ? Ack() : Nack(requeue: true);   // transient → retry
}
```

(Use `TryBodyAs<T>` from the base class if you want to handle malformed bodies yourself with `Reject(requeue: false)` instead of letting the dispatcher's exception path do it.)

When to use which:

| | Use |
|---|---|
| `Ack()` | Successfully processed. |
| `Nack(requeue: true)` | Transient failure (DB down, network blip) — retry. **Don't use on deterministic failures or you'll loop forever.** |
| `Nack(requeue: false)` | Give up. Goes to DLX if configured; otherwise lost. |
| `Reject(requeue: false)` | Message is invalid / can't be processed by anyone. Same wire effect as `Nack`, but semantically distinct. |

## Ack before processing (at-most-once)

When throughput matters more than guaranteed processing — metrics, audit logs, fire-and-forget events. The ack is sent **while the handler is still running**, freeing the prefetch slot for the next message.

```csharp
[Exchange("metrics")]
public class MetricsHandler(IMetricStore store) : EventHandler
{
    [Topic("metric.{source:string}.event", Prefetch = 100)]
    public async Task OnEvent(string source, CancellationToken ct)
    {
        await AckNowAsync();  // free the slot; broker won't redeliver if we crash now
        await store.IngestAsync(source, BodyAs<MetricEvent>()!, ct);
    }
}
```

Trade-off: at-most-once delivery. If the handler crashes after `AckNowAsync` the message is gone.

## Reject early after fast validation

Validate quickly → reject the bad ones → do heavy work on the good ones at leisure:

```csharp
[Topic("audit.{type:string}")]
public async Task OnAudit(string type, CancellationToken ct)
{
    if (!TryBodyAs<AuditDto>(out var dto))
    {
        await RejectNowAsync(requeue: false);  // malformed — broker frees slot, then we return
        return;
    }
    await AckNowAsync();
    await SlowProcessing(dto!, ct);
}
```

## Per-topic prefetch and the channel/connection model

In AMQP a **connection** is a TCP socket to the broker (expensive — TCP + AMQP handshake + auth) and a **channel** is a lightweight virtual connection multiplexed over it (cheap, sub-millisecond to open). RabbitMQ allows ~2047 channels per connection by default; opening many channels on a single connection is the norm.

Bunny matches that model:

- **One `IConnection`** per process — opened by the hosted service on startup, reused for everything.
- **One `IChannel` dedicated to publishing** — shared by `IBunnyPublisher`, guarded by a semaphore so concurrent publishes are serialized.
- **One `IChannel` per `[Topic]` binding** — each consumer gets its own channel with its own `BasicQos` setting.

So a service with 5 handler classes × 3 topics each runs `1` connection + `1` publish channel + `15` consumer channels = **16 channels on 1 TCP socket**.

Why per-binding channels (rather than sharing one channel across topics):

- **Prefetch is per-channel.** `BasicQos(prefetchCount: 20)` means "20 unacked messages on this channel". A shared channel would mean one slow topic eats the whole budget and starves the rest.
- **Fault isolation.** If the broker closes a channel (rejected publish, protocol error), only that binding stalls — the others keep running.
- **Natural per-topic concurrency.** A channel processes its `ReceivedAsync` callbacks serially (one message at a time per channel, even with async handlers). Separate channels = separate event loops = topics run in parallel.

Practical knob: set `Prefetch` per topic based on the cost of the handler.

```csharp
[Topic("orders.bulk-import", Prefetch = 1)]    // serialize — heavy
public Task BulkImport() => ...;

[Topic("orders.ping", Prefetch = 200)]         // fan out — cheap
public Task Ping() => ...;
```

You don't need to worry about channel count for any realistic workload — connections are the limited resource (one per process), channels effectively are not.

## Custom serializer

Default is `JsonBunnySerializer` (System.Text.Json). To plug a different format, register your own **before** `AddBunny`:

```csharp
public sealed class MessagePackBunnySerializer : IBunnySerializer
{
    public string ContentType => "application/x-msgpack";
    public byte[] Serialize<T>(T value) => MessagePackSerializer.Serialize(value);
    public T? Deserialize<T>(ReadOnlySpan<byte> bytes) => MessagePackSerializer.Deserialize<T>(bytes.ToArray());
}

builder.Services.AddSingleton<IBunnySerializer, MessagePackBunnySerializer>();
builder.Services.AddBunny(typeof(Program).Assembly);
```

Or configure STJ options:

```csharp
var jsonOpts = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    Converters = { new JsonStringEnumConverter() }
};
builder.Services.AddSingleton<IBunnySerializer>(new JsonBunnySerializer(jsonOpts));
```

## Poison message defence

By default, a thrown exception nacks **without** requeue (`RequeueOnError = false`) — the message is discarded unless the queue has a DLX bound. Two real-world strategies:

**1. DLX on the queue (configured outside Bunny — for now)**

When declaring queues with management UI or migrations, set `x-dead-letter-exchange` so nack-no-requeue routes there. Bind a separate inspection queue to that DLX.

**2. Manual retry counter via headers**

```csharp
[Topic("order.{id:guid}.created")]
public async Task<AckResult> OnCreated(Guid id, CancellationToken ct)
{
    var attempts = ReadAttempts(Properties);
    if (attempts >= 5) return Reject(requeue: false);  // give up

    try
    {
        await orders.HandleAsync(id, ct);
        return Ack();
    }
    catch (TransientException)
    {
        await PublishAsync(Exchange, RoutingKey, BodyAsString(), p =>
        {
            p.Headers = new Dictionary<string, object?> { ["x-attempts"] = attempts + 1 };
        }, ct);
        return Ack();  // we re-published; drop the original
    }
}

private static int ReadAttempts(IReadOnlyBasicProperties p)
    => p.Headers is { } h && h.TryGetValue("x-attempts", out var v) && v is int i ? i : 0;
```

DLX helpers are on the roadmap — until then, configure on the broker side and use `RequeueOnError = false`.

## Inject other services

Handlers are plain DI-resolved scoped services. Inject anything:

```csharp
[Exchange("orders")]
public class OrderHandler(
    IOrderService orders,
    AppDbContext db,                          // scoped — safe
    IBunnyPublisher bus,                      // singleton — also works
    ILogger<OrderHandler> logger,
    IOptions<MyOptions> opts
) : EventHandler
{
    [Topic("order.{id:guid}.created")]
    public async Task OnCreated(Guid id, CancellationToken ct)
    {
        db.Audit.Add(new(id, "received"));
        await db.SaveChangesAsync(ct);
        await orders.HandleAsync(id, BodyAs<OrderDto>()!, ct);
    }
}
```

## Multiple exchanges in one project

One handler per exchange. Bunny declares each exchange once at startup even if many handlers target it:

```csharp
[Exchange("orders")]   public class OrderHandler   : EventHandler { ... }
[Exchange("payments")] public class PaymentHandler : EventHandler { ... }
[Exchange("audit")]    public class AuditHandler   : EventHandler { ... }
```

## Pointing at multiple assemblies

If your handlers live in another assembly (e.g., a domain library):

```csharp
builder.Services.AddBunny(b => b
    .ScanAssembly(typeof(Program).Assembly)
    .ScanAssembly(typeof(OrderHandler).Assembly));

// or:
builder.Services.AddBunny(b => b
    .ScanAssemblies(typeof(Program).Assembly, typeof(OrderHandler).Assembly));
```

## Publish-only exchanges

Use `DeclareExchange` for exchanges you publish to but no handler in this service consumes from. RabbitMQ refuses publishes to non-existent exchanges, so without declaration the target exchange must exist already (created by another service / management UI / migration).

```csharp
builder.Services.AddBunny(b => b
    .ScanAssembly(typeof(Program).Assembly)
    .DeclareExchange("audit")                                      // topic, durable
    .DeclareExchange("metrics", ExchangeType.Fanout, durable: false)
    .DeclareExchange("dlx-orders"));                               // pair with x-dead-letter-exchange on the queue
```

Declaration is idempotent — if the exchange already exists with the same parameters RabbitMQ accepts it; mismatched parameters crash the channel at startup with a clear error.

## Publisher-only mode (REST API, background job)

If a service only **publishes** and never consumes, use `AddBunnyPublisher` instead of `AddBunny`. No handler scanning, no queue declarations, no consumer channels — just the connection and the publisher. The builder doesn't even expose `ScanAssembly`, so the publisher-only intent is enforced by the type system.

```csharp
// In a REST API
builder.Services.AddBunnyPublisher(b => b
    .DeclareExchange("orders")
    .DeclareExchange("audit"));

// Inject anywhere
public class CheckoutController(IBunnyPublisher bus) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateOrderDto dto, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        await bus.PublishAsync("orders", $"order.{id}.created", dto, ct);
        return Accepted(new { id });
    }
}
```

The publisher uses the same `Bunny` config section by default; override with `.ConfigureFrom("MyApp:Bus")` if you bind it elsewhere. You can mix and match in different services of the same solution — the consumer service uses `AddBunny`, the API uses `AddBunnyPublisher`, both share the same `BunnyOptions` schema.

## Custom config section name

```csharp
// appsettings.json: { "MyApp": { "Messaging": { "HostName": "rabbit", ... } } }
builder.Services.AddBunny(b => b
    .ScanAssembly(typeof(Program).Assembly)
    .ConfigureFrom("MyApp:Messaging"));
```

## Testing handlers

Handlers are plain classes — unit-test them by `new`-ing one up with fakes and calling the method directly. The `BunnyContext` is only needed if your code reads from it; otherwise pass the route param directly:

```csharp
[Fact]
public async Task OnCreated_handles_order()
{
    var orders = Substitute.For<IOrderService>();
    var handler = new OrderHandler(orders, NullLogger<OrderHandler>.Instance);

    await handler.OnCreated(Guid.NewGuid(), CancellationToken.None);

    await orders.Received(1).HandleAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
}
```

For tests that need the full pipeline (route extraction, body deserialization, ack assertions), see `Bunny.Tests/HandlerDispatcherTests.cs` for the integration-test pattern.
