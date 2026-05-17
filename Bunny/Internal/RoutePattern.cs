using System.Text.RegularExpressions;

namespace Bunny.Internal;

internal sealed partial class RoutePattern
{
    public string Pattern { get; }
    public string BindingKey { get; }
    public IReadOnlyList<RouteParameter> Parameters { get; }
    private readonly Regex _matcher;

    public RoutePattern(string pattern)
    {
        Pattern = pattern;
        Parameters = ExtractParameters(pattern);
        BindingKey = ParamRegex().Replace(pattern, "*");
        _matcher = BuildMatcher(pattern);
    }

    public bool TryMatch(string routingKey, out IReadOnlyDictionary<string, object?> values)
    {
        var match = _matcher.Match(routingKey);
        if (!match.Success) { values = EmptyValues; return false; }
        return TryExtractValues(match, out values);
    }

    private static readonly IReadOnlyDictionary<string, object?> EmptyValues
        = new Dictionary<string, object?>();

    private bool TryExtractValues(Match match, out IReadOnlyDictionary<string, object?> values)
    {
        var result = new Dictionary<string, object?>(Parameters.Count);
        foreach (var p in Parameters)
        {
            if (!TryConvert(match.Groups[p.Name].Value, p.Type, out var converted)) { values = EmptyValues; return false; }
            result[p.Name] = converted;
        }
        values = result;
        return true;
    }

    private static bool TryConvert(string raw, Type targetType, out object? value)
    {
        try { value = ConvertValue(raw, targetType); return true; }
        catch { value = null; return false; }
    }

    private static IReadOnlyList<RouteParameter> ExtractParameters(string pattern)
        => ParamRegex().Matches(pattern).Select(MakeParameter).ToList();

    private static RouteParameter MakeParameter(Match m)
        => new(m.Groups[1].Value, ResolveType(m.Groups[2].Value));

    private static Regex BuildMatcher(string pattern)
    {
        var escaped = Regex.Escape(pattern);
        var body = ParamRegex().Replace(escaped, m => $"(?<{m.Groups[1].Value}>[^.]+)");
        return new Regex($"^{body}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    }

    private static Type ResolveType(string name) => name.ToLowerInvariant() switch
    {
        "int" => typeof(int),
        "long" => typeof(long),
        "guid" => typeof(Guid),
        "string" or "str" => typeof(string),
        "bool" => typeof(bool),
        "double" => typeof(double),
        "float" => typeof(float),
        _ => throw new ArgumentException($"Unsupported route parameter type: {name}")
    };

    private static object ConvertValue(string raw, Type targetType)
        => targetType == typeof(Guid)
            ? Guid.Parse(raw)
            : Convert.ChangeType(raw, targetType, System.Globalization.CultureInfo.InvariantCulture)!;

    [GeneratedRegex(@"<(\w+):(\w+)>")]
    private static partial Regex ParamRegex();
}

internal sealed record RouteParameter(string Name, Type Type);
