using System.Reflection;

namespace Bunny.Internal;

internal sealed class BunnyRegistry
{
    private readonly List<HandlerBinding> _bindings = [];
    private readonly List<ExchangeAttribute> _explicitExchanges = [];

    public IReadOnlyList<HandlerBinding> Bindings => _bindings;
    public IEnumerable<Type> HandlerTypes => _bindings.Select(b => b.HandlerType).Distinct();
    public IEnumerable<ExchangeAttribute> Exchanges => AllExchanges().DistinctBy(e => e.Name);

    public void Scan(Assembly assembly)
    {
        foreach (var type in HandlerCandidates(assembly)) RegisterType(type);
    }

    public void DeclareExchange(ExchangeAttribute exchange) => _explicitExchanges.Add(exchange);

    private IEnumerable<ExchangeAttribute> AllExchanges()
        => _bindings.Select(b => b.Exchange).Concat(_explicitExchanges);

    private void RegisterType(Type type)
    {
        var exchange = type.GetCustomAttribute<ExchangeAttribute>()!;
        foreach (var method in TopicMethods(type)) RegisterMethod(type, exchange, method);
    }

    private void RegisterMethod(Type type, ExchangeAttribute exchange, MethodInfo method)
    {
        foreach (var topic in method.GetCustomAttributes<TopicAttribute>(inherit: false))
            _bindings.Add(new HandlerBinding(type, exchange, topic, method));
    }

    private static IEnumerable<Type> HandlerCandidates(Assembly assembly)
        => SafeGetTypes(assembly).Where(IsHandler);

    private static bool IsHandler(Type type)
        => type is { IsAbstract: false, IsClass: true }
           && typeof(EventHandler).IsAssignableFrom(type)
           && type.GetCustomAttribute<ExchangeAttribute>() is not null;

    private static IEnumerable<MethodInfo> TopicMethods(Type type)
        => type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
               .Where(HasTopic);

    private static bool HasTopic(MethodInfo method)
        => method.GetCustomAttributes<TopicAttribute>(inherit: false).Any();

    private static Type[] SafeGetTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t is not null).ToArray()!; }
    }
}
