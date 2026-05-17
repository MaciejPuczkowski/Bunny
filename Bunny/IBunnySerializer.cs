namespace Bunny;

/// <summary>
/// Pluggable serialization. Default is <see cref="JsonBunnySerializer"/> (System.Text.Json,
/// web defaults, case-insensitive). Override by registering your own implementation
/// before <c>AddBunny(...)</c>:
/// <code>services.AddSingleton&lt;IBunnySerializer&gt;(new MyCustomSerializer());</code>
/// </summary>
public interface IBunnySerializer
{
    /// <summary>MIME content type stamped on outgoing messages (e.g., <c>application/json</c>).</summary>
    string ContentType { get; }

    /// <summary>Serializes a value to UTF-8 bytes.</summary>
    byte[] Serialize<T>(T value);

    /// <summary>Deserializes UTF-8 bytes back to a typed value.</summary>
    T? Deserialize<T>(ReadOnlySpan<byte> bytes);
}
