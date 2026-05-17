using System.Text.Json;

namespace Bunny;

public sealed class JsonBunnySerializer(JsonSerializerOptions? options = null) : IBunnySerializer
{
    private readonly JsonSerializerOptions _options = options ?? BuildDefaults();

    public string ContentType => "application/json";

    public byte[] Serialize<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, _options);

    public T? Deserialize<T>(ReadOnlySpan<byte> bytes) => JsonSerializer.Deserialize<T>(bytes, _options);

    private static JsonSerializerOptions BuildDefaults() => new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };
}
