using System.Collections;
using System.Reflection;

namespace Goldpath;

/// <summary>
/// Walks a deserialized archive graph and redacts every classified string property through
/// the DataProtection seam — "classify once, every sink masks" now includes the archive
/// (archival RFC D4). Cycle-safe; returns how many values were redacted.
/// </summary>
public static class GoldpathDocumentRedactor
{
    /// <summary>Redacts in place; returns how many values CHANGED (idempotent evidence).</summary>
    public static int Redact(object root, IGoldpathDataProtector protector)
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        return Walk(root, protector, visited);
    }

    private static int Walk(object? node, IGoldpathDataProtector protector, HashSet<object> visited)
    {
        if (node is null || node is string || node.GetType().IsPrimitive || !visited.Add(node))
        {
            return 0;
        }

        if (node is IEnumerable enumerable)
        {
            var redactedItems = 0;
            foreach (var item in enumerable)
            {
                redactedItems += Walk(item, protector, visited);
            }

            return redactedItems;
        }

        var type = node.GetType();
        if (type.Namespace?.StartsWith("System", StringComparison.Ordinal) == true)
        {
            return 0;   // framework types carry no domain classification
        }

        var redacted = 0;
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0 || !property.CanRead)
            {
                continue;
            }

            if (property.PropertyType == typeof(string))
            {
                if (property.CanWrite && protector.IsClassified(type, property.Name)
                    && property.GetValue(node) is string value
                    && protector.Redact(type, property.Name, value) is { } masked
                    && !string.Equals(masked, value, StringComparison.Ordinal))
                {
                    // Only CHANGES count — erasure is idempotent evidence, and a second
                    // request against an already-redacted document reports "nothing left".
                    property.SetValue(node, masked);
                    redacted++;
                }

                continue;
            }

            redacted += Walk(property.GetValue(node), protector, visited);
        }

        return redacted;
    }
}
