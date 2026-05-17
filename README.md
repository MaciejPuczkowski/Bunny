# Bunny

Controller-style RabbitMQ consumer/publisher for .NET 10. Route AMQP messages to handler methods with attributes — like ASP.NET controllers, but for the bus.

```csharp
[Exchange("orders")]
public class OrderHandler(IOrderService orders, ILogger<OrderHandler> logger) : Bunny.EventHandler
{
    [Topic("order.<id:guid>.created")]
    public async Task<AckResult> OnCreated(Guid id, [FromBody] OrderCreatedDto dto, CancellationToken ct)
    {
        logger.LogInformation("Order {Id} from {RoutingKey}", id, RoutingKey);
        return await orders.TryHandle(dto, ct) ? Ack() : Nack(requeue: true);
    }
}
```

## Why

The .NET ecosystem has plenty of RabbitMQ libraries — MassTransit, EasyNetQ, Rebus, raw `RabbitMQ.Client`. Each comes with its own mental model: sagas, consumers, message contexts, handlers, pipeline behaviors. Every team picks one and then *reinvents how to organize code around it*.

Bunny is opinionated about exactly that one thing: **organize event handling the same way you organize REST controllers.**

```csharp
// REST controller — every .NET developer knows this shape
[Route("orders")]
public class OrdersController(IOrderService orders) : ControllerBase
{
    [HttpPost("{id:guid}/cancel")]
    public Task Cancel(Guid id, [FromBody] CancelDto dto) => orders.Cancel(id, dto);
}

// Bunny handler — same shape, AMQP routing key instead of an HTTP route
[Exchange("orders")]
public class OrderHandler(IOrderService orders) : Bunny.EventHandler
{
    [Topic("order.<id:guid>.cancel")]
    public Task Cancel(Guid id, [FromBody] CancelDto dto, CancellationToken ct) => orders.Cancel(id, dto, ct);
}
```

If you've ever written an ASP.NET controller, you already know Bunny. **Class = group of related handlers. Method = one handler. Attribute = route. Return value = response.** No new concepts to learn — even a developer who's never touched RabbitMQ can read a handler and understand what it does on the first try.

Trade-off: Bunny doesn't compete on features with MassTransit & co. No built-in sagas, no Outbox pattern, no cross-broker abstraction, no scheduling. It does one thing: attribute-routed AMQP handlers that look like REST controllers. Need more than that and you'll outgrow Bunny.

## Install

```bash
dotnet add package BunnyMQ
```

Code still references the `Bunny` namespace (and assembly) — only the NuGet listing name is `BunnyMQ` (the simpler `Bunny` package id is taken on nuget.org by an unrelated project).

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
    public Task OnCreated(Guid id, [FromBody] OrderDto dto, CancellationToken ct) => orders.HandleAsync(id, dto, ct);
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

Alpha. Public API may change. No DLX / dead-letter helpers yet (configure on the queue directly). No retry policies. No publisher channel pool (single shared channel + semaphore).

## License

MIT — see [LICENSE](LICENSE).
