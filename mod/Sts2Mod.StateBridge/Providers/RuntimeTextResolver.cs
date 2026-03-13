using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Sts2Mod.StateBridge.Providers;

internal sealed record TextResolutionResult(string? Text, string Status, string Source, string? Detail = null)
{
    public bool HasText => !string.IsNullOrWhiteSpace(Text);
}

internal sealed class TextDiagnosticsCollector
{
    private readonly List<IReadOnlyDictionary<string, object?>> _entries = new();
    private int _resolved;
    private int _fallback;
    private int _unresolved;

    public void Record(string path, TextResolutionResult result)
    {
        switch (result.Status)
        {
            case "resolved":
                _resolved++;
                break;
            case "fallback":
                _fallback++;
                break;
            default:
                _unresolved++;
                break;
        }

        if (!string.Equals(result.Status, "resolved", StringComparison.Ordinal))
        {
            _entries.Add(RuntimeTextResolver.BuildDiagnosticsEntry(path, result));
        }
    }

    public IReadOnlyDictionary<string, object?> ToMetadata()
    {
        return new Dictionary<string, object?>
        {
            ["resolved"] = _resolved,
            ["fallback"] = _fallback,
            ["unresolved"] = _unresolved,
            ["entries"] = _entries.ToArray(),
        };
    }
}

internal static class RuntimeTextResolver
{
    private static readonly string[] PreferredTextMembers =
    [
        "Name",
        "Title",
        "Description",
        "Label",
        "Text",
        "DisplayName",
        "FlavorText",
        "LocString",
        "Relic",
        "Potion",
        "Reward",
        "Intent",
    ];

    public static TextResolutionResult Resolve(object? value, string path, TextDiagnosticsCollector? collector = null, params string[] preferredMembers)
    {
        var orderedMembers = preferredMembers
            .Concat(PreferredTextMembers)
            .Where(member => !string.IsNullOrWhiteSpace(member))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var result = ResolveCore(value, visited, orderedMembers, depth: 0);
        collector?.Record(path, result);
        return result;
    }

    public static IReadOnlyDictionary<string, object?> BuildDiagnosticsEntry(string path, TextResolutionResult result)
    {
        return new Dictionary<string, object?>
        {
            ["path"] = path,
            ["status"] = result.Status,
            ["source"] = result.Source,
            ["detail"] = result.Detail,
        };
    }

    public static IReadOnlyDictionary<string, object?> CreateActionDiagnostics(string path, TextResolutionResult result)
    {
        return new Dictionary<string, object?>
        {
            ["diagnostics"] = BuildDiagnosticsEntry(path, result),
        };
    }

    private static TextResolutionResult ResolveCore(object? value, HashSet<object> visited, IReadOnlyList<string> preferredMembers, int depth)
    {
        if (value is null)
        {
            return new TextResolutionResult(null, "unresolved", "null", "value is null");
        }

        if (value is string text)
        {
            return string.IsNullOrWhiteSpace(text)
                ? new TextResolutionResult(null, "unresolved", "string", "string is empty")
                : new TextResolutionResult(text, "resolved", "string");
        }

        if (depth > 6)
        {
            return new TextResolutionResult(null, "unresolved", "depth_limit", "text resolution depth exceeded");
        }

        if (!value.GetType().IsValueType && !visited.Add(value))
        {
            return new TextResolutionResult(null, "unresolved", "cycle", "cyclic object graph detected");
        }

        if (TryResolveLocString(value, out var locStringResult))
        {
            return locStringResult;
        }

        if (TryResolvePrimitive(value, out var primitiveResult))
        {
            return primitiveResult;
        }

        foreach (var memberName in preferredMembers)
        {
            var memberValue = GetMemberValue(value, memberName);
            if (memberValue is null)
            {
                continue;
            }

            var nestedResult = ResolveCore(memberValue, visited, PreferredTextMembers, depth + 1);
            if (nestedResult.HasText)
            {
                return new TextResolutionResult(
                    nestedResult.Text,
                    nestedResult.Status,
                    $"member:{memberName}->{nestedResult.Source}",
                    nestedResult.Detail);
            }
        }

        var toStringValue = value.ToString();
        if (IsAcceptableToString(value, toStringValue))
        {
            return new TextResolutionResult(toStringValue, "fallback", "to_string");
        }

        return new TextResolutionResult(null, "unresolved", "unsupported", $"no readable text found for {value.GetType().FullName}");
    }

    private static bool TryResolveLocString(object value, out TextResolutionResult result)
    {
        var type = value.GetType();
        var name = type.FullName ?? type.Name;
        if (!name.Contains("LocString", StringComparison.Ordinal))
        {
            result = new TextResolutionResult(null, "unresolved", "not_loc_string");
            return false;
        }

        var formatted = InvokeStringMethod(value, "GetFormattedText");
        if (!string.IsNullOrWhiteSpace(formatted))
        {
            result = new TextResolutionResult(formatted, "resolved", "loc_string.formatted");
            return true;
        }

        var raw = InvokeStringMethod(value, "GetRawText");
        if (!string.IsNullOrWhiteSpace(raw))
        {
            result = new TextResolutionResult(raw, "fallback", "loc_string.raw");
            return true;
        }

        var entryKey = GetMemberValue(value, "LocEntryKey") as string;
        if (!string.IsNullOrWhiteSpace(entryKey))
        {
            result = new TextResolutionResult(entryKey, "fallback", "loc_string.key");
            return true;
        }

        result = new TextResolutionResult(null, "unresolved", "loc_string", "LocString did not produce formatted or raw text");
        return true;
    }

    private static bool TryResolvePrimitive(object value, out TextResolutionResult result)
    {
        if (value is Enum)
        {
            result = new TextResolutionResult(value.ToString(), "fallback", "enum");
            return true;
        }

        if (value is char or bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal)
        {
            result = new TextResolutionResult(Convert.ToString(value), "fallback", "primitive");
            return true;
        }

        result = new TextResolutionResult(null, "unresolved", "non_primitive");
        return false;
    }

    private static string? InvokeStringMethod(object target, string methodName)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
        if (method is null)
        {
            return null;
        }

        try
        {
            return method.Invoke(target, null) as string;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsAcceptableToString(object value, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var type = value.GetType();
        if (string.Equals(text, type.FullName, StringComparison.Ordinal) ||
            string.Equals(text, type.Name, StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    private static object? GetMemberValue(object? target, string memberName)
    {
        if (target is null)
        {
            return null;
        }

        var type = target as Type ?? target.GetType();
        var flags = BindingFlags.Public | BindingFlags.NonPublic |
                    (target is Type ? BindingFlags.Static : BindingFlags.Instance);
        var property = type.GetProperty(memberName, flags);
        if (property is not null && property.GetIndexParameters().Length == 0)
        {
            try
            {
                return property.GetValue(target is Type ? null : target);
            }
            catch
            {
                return null;
            }
        }

        var field = type.GetField(memberName, flags);
        if (field is null)
        {
            return null;
        }

        try
        {
            return field.GetValue(target is Type ? null : target);
        }
        catch
        {
            return null;
        }
    }
}
