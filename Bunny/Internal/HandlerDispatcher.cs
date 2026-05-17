using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Bunny.Internal;

internal sealed class HandlerDispatcher(
    HandlerBinding binding,
    RoutePattern pattern,
    IServiceScopeFactory scopeFactory,
    IBunnyPublisher publisher,
    IBunnySerializer serializer,
    ILoggerFactory loggerFactory)
{
    private readonly ILogger _logger = loggerFactory.CreateLogger(binding.HandlerType);
    private readonly ParameterInfo[] _parameters = binding.Method.GetParameters();

    public async Task DispatchAsync(IChannel channel, BasicDeliverEventArgs ea, CancellationToken hostToken)
    {
        if (!pattern.TryMatch(ea.RoutingKey, out var routeValues))
        {
            await NackUnmatchedAsync(channel, ea, hostToken);
            return;
        }
        await ProcessAsync(channel, ea, routeValues, hostToken);
    }

    private async Task ProcessAsync(IChannel channel, BasicDeliverEventArgs ea, IReadOnlyDictionary<string, object?> routeValues, CancellationToken hostToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = BuildContext(ea, routeValues, hostToken);
        var handler = ResolveHandler(scope, context, channel);
        var result = await TryInvokeAsync(handler, context);
        if (handler.Replied) return;
        await AckExecutor.ApplyAsync(channel, ea.DeliveryTag, result, hostToken);
    }

    private async Task<AckResult> TryInvokeAsync(EventHandler handler, BunnyContext context)
    {
        try { return await InvokeMethodAsync(handler, context); }
        catch (Exception ex) { LogFailure(ex); return AckResult.Nack(binding.Topic.RequeueOnError); }
    }

    private async Task<AckResult> InvokeMethodAsync(EventHandler handler, BunnyContext context)
    {
        var args = BuildArguments(context);
        var result = binding.Method.Invoke(handler, args);
        return await AwaitResultAsync(result);
    }

    private static async Task<AckResult> AwaitResultAsync(object? result)
        => result switch
        {
            AckResult ack => ack,
            Task<AckResult> task => await task,
            ValueTask<AckResult> task => await task,
            Task task => await DefaultAckAfterAsync(task),
            ValueTask task => await DefaultAckAfterAsync(task),
            _ => AckResult.Ack()
        };

    private static async Task<AckResult> DefaultAckAfterAsync(Task task)
    {
        await task;
        return AckResult.Ack();
    }

    private static async Task<AckResult> DefaultAckAfterAsync(ValueTask task)
    {
        await task;
        return AckResult.Ack();
    }

    private async Task NackUnmatchedAsync(IChannel channel, BasicDeliverEventArgs ea, CancellationToken ct)
    {
        _logger.LogWarning("Routing key {RoutingKey} did not match pattern {Pattern}", ea.RoutingKey, pattern.Pattern);
        await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, ct);
    }

    private void LogFailure(Exception ex)
        => _logger.LogError(ex, "Handler {Handler}.{Method} failed", binding.HandlerType.Name, binding.Method.Name);

    private BunnyContext BuildContext(BasicDeliverEventArgs ea, IReadOnlyDictionary<string, object?> routeValues, CancellationToken hostToken)
        => new(serializer, ea.ConsumerTag, ea.DeliveryTag, ea.Redelivered, ea.Exchange, ea.RoutingKey, ea.BasicProperties, ea.Body, routeValues, hostToken);

    private EventHandler ResolveHandler(AsyncServiceScope scope, BunnyContext context, IChannel channel)
    {
        var handler = (EventHandler)scope.ServiceProvider.GetRequiredService(binding.HandlerType);
        handler.Initialize(context, publisher, channel);
        return handler;
    }

    private object?[] BuildArguments(BunnyContext context)
    {
        var args = new object?[_parameters.Length];
        for (var i = 0; i < _parameters.Length; i++) args[i] = BindArgument(_parameters[i], context);
        return args;
    }

    private static object? BindArgument(ParameterInfo param, BunnyContext context)
    {
        if (param.ParameterType == typeof(CancellationToken)) return context.CancellationToken;
        if (param.ParameterType == typeof(BunnyContext)) return context;
        return BindRouteValue(param, context);
    }

    private static object? BindRouteValue(ParameterInfo param, BunnyContext context)
    {
        if (!context.RouteParameters.TryGetValue(param.Name ?? "", out var raw) || raw is null)
            return DefaultValue(param.ParameterType);
        return param.ParameterType.IsInstanceOfType(raw)
            ? raw
            : Convert.ChangeType(raw, param.ParameterType, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static object? DefaultValue(Type type)
        => type.IsValueType ? Activator.CreateInstance(type) : null;
}
