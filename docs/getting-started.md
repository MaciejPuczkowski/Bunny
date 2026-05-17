# Getting started

**public**

This tutorial walks through building a minimal Bunny consumer + publisher in a fresh .NET 10 worker service. You will:

1. Install Bunny
2. Configure the connection
3. Write a handler that consumes a topic pattern
4. Publish a message
5. Understand the ack lifecycle

The same flow works in a web app — the only difference is `WebApplication.CreateBuilder` instead of `Host.CreateApplicationBuilder`.

---

## 1. Prerequisites

- .NET 10 SDK (`dotnet --list-sdks` should show `10.x.x`)
- A running RabbitMQ broker. For local dev:
  ```bash
  docker run -d --name rabbit -p 5672:5672 -p 15672:15672 rabbitmq:3-management
  ```
  Management UI: <http://localhost:15672> (guest/guest).

## 2. Create the project

```bash
dotnet new worker -n OrderProcessor
cd OrderProcessor
dotnet add reference ../../Libs/Bunny/Bunny.csproj
```

## 3. Configure

Add a `Bunny` section to `appsettings.json`:

```json
{
  "Bunny": {
    "HostName": "localhost",
    "Port": 5672,
    "UserName": "guest",
    "Password": "guest",
    "VirtualHost": "/",
    "ClientProvidedName": "order-processor",
    "DefaultPrefetch": 10
  }
}
```

All keys are optional — defaults are sane for local dev.

## 4. Wire up DI

In `Program.cs`:

```csharp
using Bunny;

var builder = Host.CreateApplicationBuilder(args);

// Your domain services
builder.Services.AddSingleton<IOrderService, InMemoryOrderService>();

// Bunny — fluent configuration
builder.Services.AddBunny(b => b
    .ScanAssembly(typeof(Program).Assembly));

var app = builder.Build();
await app.RunAsync();
```

`AddBunny` takes a `BunnyBuilder` callback and does three things:

- Binds `BunnyOptions` from the `"Bunny"` config section (override with `ConfigureFrom`)
- Registers core services (publisher, serializer, hosted service)
- Scans the assemblies added via `ScanAssembly` for classes inheriting `Bunny.EventHandler` decorated with `[Exchange]`, and registers each as **scoped**

`BunnyBuilder` also exposes `DeclareExchange("name")` for publish-only exchanges — exchanges you publish to but no handler in this service consumes from:

```csharp
builder.Services.AddBunny(b => b
    .ScanAssembly(typeof(Program).Assembly)
    .DeclareExchange("audit")              // declared at startup, idempotent
    .DeclareExchange("dlx", durable: true));
```

This is useful because RabbitMQ refuses publishes to non-existent exchanges (with `mandatory: true`). Without `DeclareExchange`, the target exchange must be created out-of-band (other service, management UI, migration).

## 5. Write a handler

Create `OrderHandler.cs`:

```csharp
using Bunny;
using Microsoft.Extensions.Logging;

// Without this alias, `EventHandler` resolves to System.EventHandler (a delegate) and the
// compiler reports CS0104: ambiguous reference.
using EventHandler = Bunny.EventHandler;

[Exchange("orders")]
public class OrderHandler(IOrderService orders, ILogger<OrderHandler> logger) : EventHandler
{
    [Topic("order.<id:guid>.created", Queue = "orders.created", Prefetch = 20)]
    public async Task OnCreated(Guid id, [FromBody] OrderCreatedDto dto, CancellationToken ct)
    {
        logger.LogInformation("Got order {Id} via {RoutingKey}", id, RoutingKey);
        await orders.HandleAsync(dto, ct);
    }
}

public record OrderCreatedDto(string Customer, decimal Total);
```

What's happening:

- `[Exchange("orders")]` — declares the topic exchange `orders` at startup (idempotent).
- `[Topic("order.<id:guid>.created", ...)]` — declares the queue `orders.created`, binds it to the exchange with key `order.*.created`, starts a consumer with prefetch 20.
- `<id:guid>` — `id` is bound to the `Guid` method parameter automatically.
- `[FromBody]` — `dto` is deserialized from the message body before the handler runs (System.Text.Json by default). Same convention as ASP.NET controllers.
- `OrderHandler` is constructed **once per message** inside its own DI scope, so injecting `DbContext` or other scoped services is safe.
- The method returns `Task` (no `AckResult`), so Bunny acks the message implicitly on success and nacks (without requeue) on exception.

## 6. Publish

You can publish from any DI-resolved service:

```csharp
public sealed class CheckoutWorker(IBunnyPublisher bus, ILogger<CheckoutWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var id = Guid.NewGuid();
            await bus.PublishAsync("orders", $"order.{id}.created",
                new OrderCreatedDto("acme", 99.95m), stoppingToken);
            logger.LogInformation("Published order {Id}", id);
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }
}
```

Register it next to your handler:

```csharp
builder.Services.AddHostedService<CheckoutWorker>();
```

Run `dotnet run` — the worker publishes one message every 2s, the handler logs it.

## 7. Understand ack semantics

The handler's **return type** decides what happens after it finishes:

| Return type | On success | On exception |
|---|---|---|
| `void` / `Task` / `ValueTask` | `Ack` | `Nack(RequeueOnError)` |
| `AckResult` / `Task<AckResult>` / `ValueTask<AckResult>` | the returned result | `Nack(RequeueOnError)` |

Explicit example:

```csharp
[Topic("order.<id:guid>.created", RequeueOnError = false)]
public async Task<AckResult> OnCreated(Guid id, CancellationToken ct)
{
    if (!TryBodyAs<OrderCreatedDto>(out var dto)) return Reject(requeue: false);  // malformed → DLX
    return await orders.TryHandle(dto!, ct)
        ? Ack()
        : Nack(requeue: true);  // transient → retry
}
```

If you want to free the prefetch slot **before** finishing (at-most-once, useful for metrics / audit):

```csharp
[Topic("metrics.<source:string>.event", Prefetch = 100)]
public async Task OnMetric(string source, CancellationToken ct)
{
    await AckNowAsync();           // broker frees the slot now
    await metrics.IngestAsync(BodyAs<MetricEvent>()!, ct);
    // return value is ignored after Ack/Nack/RejectNowAsync
}
```

See [examples.md](examples.md) for more patterns.

## 8. Inspect what was bound

Bunny logs every binding at `Information` level on startup:

```
info: Bunny.Internal.BunnyHostedService[0]
      Bound OrderHandler.OnCreated -> orders / order.<id:guid>.created (queue: orders.created)
info: Bunny.Internal.BunnyHostedService[0]
      Bunny started: 1 binding(s) on localhost:5672
```

## Next

- [Examples & recipes](examples.md) — multiple topics on one handler, custom serializers, publish-from-handler, poison messages
