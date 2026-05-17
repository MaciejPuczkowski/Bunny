using System.Reflection;

namespace Bunny.Internal;

internal sealed record HandlerBinding(
    Type HandlerType,
    ExchangeAttribute Exchange,
    TopicAttribute Topic,
    MethodInfo Method
);
