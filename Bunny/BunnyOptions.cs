namespace Bunny;

/// <summary>
/// Bunny connection settings, bound from the <c>"Bunny"</c> configuration section by default.
/// </summary>
public sealed class BunnyOptions
{
    /// <summary>RabbitMQ host. Default <c>localhost</c>.</summary>
    public string HostName { get; set; } = "localhost";

    /// <summary>AMQP port. Default 5672.</summary>
    public int Port { get; set; } = 5672;

    /// <summary>Username. Default <c>guest</c>.</summary>
    public string UserName { get; set; } = "guest";

    /// <summary>Password. Default <c>guest</c>.</summary>
    public string Password { get; set; } = "guest";

    /// <summary>vhost. Default <c>/</c>.</summary>
    public string VirtualHost { get; set; } = "/";

    /// <summary>Use TLS when connecting. Default false.</summary>
    public bool UseSsl { get; set; } = false;

    /// <summary>Connection name surfaced in the RabbitMQ management UI. Default <c>bunny</c>.</summary>
    public string ClientProvidedName { get; set; } = "bunny";

    /// <summary>Fallback prefetch count used when a topic does not specify its own. Default 10.</summary>
    public ushort DefaultPrefetch { get; set; } = 10;
}
