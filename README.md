# Bunny

Controller-style RabbitMQ consumer/publisher for .NET 10. Route AMQP messages to handler methods with attributes — like ASP.NET controllers, but for the bus.

```csharp
[Exchange("orders")]
public class OrderHandler(IOrderService orders, ILogger<OrderHandler> logger) : Bunny.EventHandler
{
    [Topic("order.<id:guid>.created")]
    public async Task<AckResult> OnCreated(Guid id, CancellationToken ct)
    {
        var dto = BodyAs<OrderCreatedDto>();
        logger.LogInformation("Order {Id} from {RoutingKey}", id, RoutingKey);
        return await orders.TryHandle(dto!, ct) ? Ack() : Nack(requeue: true);
    }
}
```

## Why

| Problem in plain RabbitMQ.Client | Bunny fix |
|---|---|
| Channel/consumer plumbing per handler | One attribute pair (`[Exchange]` + `[Topic]`) |
| Scoped DI doesn't work — root provider only | Per-message `IServiceScope`, handlers registered as `AddScoped` |
| Sync `IBasicConsumer` + `task.Wait()` deadlocks | Async-first (`IAsyncBasicConsumer`, RabbitMQ.Client v7) |
| Manual ack everywhere, easy to forget | Return `Ack()` / `Nack()` / `Reject()` (or just `void` = ack-on-success) |
| Newtonsoft hardcoded | `IBunnySerializer` — STJ default, pluggable |
| Tied to `WebApplication` | `IHostedService` — works in web apps and worker services |

## Install

```bash
dotnet add package Bunny
```

Or as a project reference during local development:

```xml
<ProjectReference Include="..\..\Libs\Bunny\Bunny\Bunny.csproj" />
```

## Quick start

```csharp
// Program.cs
builder.Services.AddBunny(b => b
    .ScanAssembly(typeof(Program).Assembly)
    .DeclareExchange("audit"));   // optional: publish-only exchanges
```

```json
// appsettings.json
{
  "Bunny": {
    "HostName": "rabbitmq",
    "Port": 5672,
    "UserName": "guest",
    "Password": "guest",
    "VirtualHost": "/"
  }
}
```

Write a handler:

```csharp
using EventHandler = Bunny.EventHandler;  // avoids clash with System.EventHandler delegate

[Exchange("orders")]
public class OrderHandler(IOrderService orders) : EventHandler
{
    [Topic("order.<id:guid>.created", Queue = "orders.created", Prefetch = 20)]
    public Task OnCreated(Guid id, CancellationToken ct) => orders.HandleAsync(id, BodyAs<OrderDto>()!, ct);
}
```

Publish from anywhere:

```csharp
public class CheckoutService(IBunnyPublisher bus)
{
    public Task Emit(Order o, CancellationToken ct)
        => bus.PublishAsync("orders", $"order.{o.Id}.created", o, ct);
}
```

If a service only publishes (REST API, background job) and never consumes, use the lightweight `AddBunnyPublisher` — no scanning, no consumer channels:

```csharp
builder.Services.AddBunnyPublisher(b => b
    .DeclareExchange("orders")
    .DeclareExchange("audit"));
```

## Docs

- **[Getting started](docs/getting-started.md)** — install, configure, write your first handler step-by-step
- **[Examples & recipes](docs/examples.md)** — route params, publishing from handlers, early ack, custom serializers, poison messages

## Repo layout

```
Bunny/                   <-- repo root
├── Bunny.sln
├── README.md
├── docs/
├── Bunny/               <-- library project
│   └── Bunny.csproj
└── Bunny.Tests/         <-- xUnit + NSubstitute + FluentAssertions
    └── Bunny.Tests.csproj
```

Build & test from the root:

```bash
dotnet build Bunny.sln
dotnet test  Bunny.sln
```

## Features

- Attribute routing: `[Exchange]` on class, `[Topic("order.<id:guid>.created")]` on methods
- Typed route parameter binding: `int`, `long`, `guid`, `string`, `bool`, `double`, `float`
- Scoped DI per message — `DbContext` & friends just work
- Async pipeline end-to-end (RabbitMQ.Client v7)
- Return-value ack: `return Ack();` / `Nack(requeue: true)` / `Reject()` — or `void` for implicit ack
- Early reply: `AckNowAsync()` / `NackNowAsync()` / `RejectNowAsync()` when you want to free the prefetch slot before finishing
- Pluggable serialization (`IBunnySerializer`, STJ default)
- Per-topic prefetch & requeue-on-error
- Configurable connection (`IOptions<BunnyOptions>`)

## Status

`0.1.0` — alpha. Public API may change. No DLX / dead-letter helpers yet (configure on the queue directly). No retry policies. No publisher channel pool (single shared channel + semaphore).

## License

TBD.
