namespace Bunny;

/// <summary>
/// Binds a handler method parameter to the deserialized message body — the same way ASP.NET's
/// <c>[FromBody]</c> binds a controller parameter to the HTTP request body. The parameter type
/// is passed through the configured <see cref="IBunnySerializer"/>.
/// </summary>
/// <remarks>
/// Without this attribute, complex parameters are matched against route parameters by name.
/// Decorating a parameter with <see cref="FromBodyAttribute"/> forces body binding regardless
/// of the parameter's name. If deserialization throws, the dispatcher treats it like any other
/// handler exception and applies the topic's <c>RequeueOnError</c> policy.
/// </remarks>
/// <example>
/// <code><![CDATA[
/// [Topic("order.<id:guid>.created")]
/// public async Task OnCreated(Guid id, [FromBody] OrderCreatedDto dto, CancellationToken ct)
/// {
///     // id from routing key, dto from body, ct from host - no BodyAs<T>() call needed
///     await orders.HandleAsync(id, dto, ct);
/// }
/// ]]></code>
/// </example>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = true)]
public sealed class FromBodyAttribute : Attribute;
