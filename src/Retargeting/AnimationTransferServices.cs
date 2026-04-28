using System.Reflection;
using System.Text;
using UpkManager.Models.UpkFile.Engine.Anim;

namespace OmegaAssetStudio.Retargeting;

public sealed class AnimationTransferEntry
{
    public string PropertyName { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Details { get; init; } = string.Empty;
    public int ItemCount { get; init; }
}

public sealed class AnimationTransferReport
{
    public string TransferKind { get; init; } = string.Empty;
    public string SourceLabel { get; init; } = string.Empty;
    public string TargetLabel { get; init; } = string.Empty;
    public UAnimSet? DestinationAnimSet { get; internal set; }
    public List<AnimationTransferEntry> Entries { get; } = [];

    public int CopiedCount => Entries.Count(entry => entry.Status.Equals("Copied", StringComparison.OrdinalIgnoreCase));
    public int SkippedCount => Entries.Count(entry => entry.Status.Equals("Skipped", StringComparison.OrdinalIgnoreCase));
}

public static class AnimationTransferReflection
{
    private static readonly BindingFlags Flags = BindingFlags.Public | BindingFlags.Instance;

    public static AnimationTransferReport Transfer(
        UAnimSet source,
        UAnimSet? target,
        string transferKind,
        IReadOnlyList<string> keywords,
        Action<string>? log = null)
    {
        if (source is null)
            throw new ArgumentNullException(nameof(source));

        UAnimSet destination = target is null || ReferenceEquals(source, target)
            ? CloneAnimationSet(source)
            : target;

        AnimationTransferReport report = new()
        {
            TransferKind = transferKind,
            SourceLabel = DescribeAnimSet(source),
            TargetLabel = DescribeAnimSet(destination),
            DestinationAnimSet = destination
        };

        HashSet<string> keywordSet = keywords
            .Where(static keyword => !string.IsNullOrWhiteSpace(keyword))
            .Select(static keyword => keyword.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        List<PropertyInfo> sourceProperties = source.GetType()
            .GetProperties(Flags)
            .Where(property =>
                property.CanRead &&
                keywordSet.Any(keyword => property.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(property => property.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (PropertyInfo sourceProperty in sourceProperties)
        {
            object? sourceValue = SafeGetValue(sourceProperty, source);
            if (sourceValue is null)
            {
                report.Entries.Add(new AnimationTransferEntry
                {
                    PropertyName = sourceProperty.Name,
                    Status = "Skipped",
                    Details = "Source property was null."
                });
                continue;
            }

            PropertyInfo? targetProperty = ResolveTargetProperty(destination.GetType(), sourceProperty, keywordSet);
            if (targetProperty is null)
            {
                report.Entries.Add(new AnimationTransferEntry
                {
                    PropertyName = sourceProperty.Name,
                    Status = "Skipped",
                    Details = "Target does not expose a matching property."
                });
                continue;
            }

            if (TryTransferProperty(destination, targetProperty, sourceValue, out string details))
            {
                int count = CountItems(sourceValue);
                report.Entries.Add(new AnimationTransferEntry
                {
                    PropertyName = sourceProperty.Name,
                    Status = "Copied",
                    Details = details,
                    ItemCount = count
                });
                log?.Invoke($"{transferKind}: copied '{sourceProperty.Name}' ({count} item(s)).");
            }
            else
            {
                report.Entries.Add(new AnimationTransferEntry
                {
                    PropertyName = sourceProperty.Name,
                    Status = "Skipped",
                    Details = details
                });
            }
        }

        if (report.Entries.Count == 0)
        {
            report.Entries.Add(new AnimationTransferEntry
            {
                PropertyName = transferKind,
                Status = "Skipped",
                Details = $"No matching {transferKind.ToLowerInvariant()} properties were detected on this AnimSet."
            });
        }

        log?.Invoke($"{transferKind}: {report.CopiedCount} copied, {report.SkippedCount} skipped.");
        return report;
    }

    public static AnimationTransferReport Merge(string transferKind, string sourceLabel, string targetLabel, UAnimSet? destinationAnimSet, params AnimationTransferReport[] reports)
    {
        AnimationTransferReport merged = new()
        {
            TransferKind = transferKind,
            SourceLabel = sourceLabel,
            TargetLabel = targetLabel,
            DestinationAnimSet = destinationAnimSet
        };

        foreach (AnimationTransferReport report in reports)
        {
            if (report is null)
                continue;

            merged.Entries.AddRange(report.Entries);
            merged.DestinationAnimSet ??= report.DestinationAnimSet;
        }

        if (merged.Entries.Count == 0)
        {
            merged.Entries.Add(new AnimationTransferEntry
            {
                PropertyName = transferKind,
                Status = "Skipped",
                Details = $"No matching {transferKind.ToLowerInvariant()} properties were detected on this AnimSet."
            });
        }

        return merged;
    }

    public static UAnimSet CloneAnimationSet(UAnimSet source)
    {
        if (source is null)
            throw new ArgumentNullException(nameof(source));

        MethodInfo? cloneMethod = source.GetType().GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);
        UAnimSet clone = cloneMethod?.Invoke(source, null) is UAnimSet result ? result : source;
        if (ReferenceEquals(clone, source))
            return source;

        foreach (PropertyInfo property in clone.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead || !property.CanWrite)
                continue;

            object? value = null;
            try
            {
                value = property.GetValue(clone);
            }
            catch
            {
                continue;
            }

            if (value is null)
                continue;

            Type valueType = value.GetType();
            if (!valueType.IsGenericType)
                continue;

            Type genericDef = valueType.GetGenericTypeDefinition();
            if (genericDef != typeof(List<>))
                continue;

            object? newList = Activator.CreateInstance(valueType, value);
            if (newList is null)
                continue;

            try
            {
                property.SetValue(clone, newList);
            }
            catch
            {
            }
        }

        return clone;
    }

    private static object? SafeGetValue(PropertyInfo property, object instance)
    {
        try
        {
            return property.GetValue(instance);
        }
        catch
        {
            return null;
        }
    }

    private static bool TryTransferProperty(object destination, PropertyInfo targetProperty, object sourceValue, out string details)
    {
        try
        {
            if (sourceValue is string stringValue)
            {
                if (targetProperty.CanWrite && targetProperty.PropertyType.IsAssignableFrom(sourceValue.GetType()))
                {
                    targetProperty.SetValue(destination, stringValue);
                    details = "Copied string value.";
                    return true;
                }

                details = "Target string property was not writable.";
                return false;
            }

            if (sourceValue is System.Collections.IEnumerable enumerable && sourceValue is not byte[])
            {
                if (TryAssignEnumerable(destination, targetProperty, enumerable, out details))
                    return true;
            }

            if (targetProperty.CanWrite && targetProperty.PropertyType.IsAssignableFrom(sourceValue.GetType()))
            {
                targetProperty.SetValue(destination, CloneScalar(sourceValue));
                details = "Copied property value.";
                return true;
            }

            details = $"Target property '{targetProperty.PropertyType.Name}' is not assignable from '{sourceValue.GetType().Name}'.";
            return false;
        }
        catch (Exception ex)
        {
            details = ex.Message;
            return false;
        }
    }

    private static bool TryAssignEnumerable(object destination, PropertyInfo targetProperty, System.Collections.IEnumerable sourceEnumerable, out string details)
    {
        List<object?> clonedItems = [];
        foreach (object? item in sourceEnumerable)
            clonedItems.Add(CloneScalar(item));

        object? currentValue = SafeGetValue(targetProperty, destination);
        if (currentValue is not null && TryPopulateCollection(currentValue, clonedItems, out details))
        {
            return true;
        }

        if (!targetProperty.CanWrite)
        {
            details = "Target collection is read-only.";
            return false;
        }

        if (targetProperty.PropertyType.IsArray)
        {
            Type elementType = targetProperty.PropertyType.GetElementType() ?? typeof(object);
            Array array = Array.CreateInstance(elementType, clonedItems.Count);
            for (int i = 0; i < clonedItems.Count; i++)
            {
                object? item = clonedItems[i];
                if (item is null || elementType.IsInstanceOfType(item))
                {
                    array.SetValue(item, i);
                }
                else
                {
                    details = $"Item {i} could not be assigned to array element type '{elementType.Name}'.";
                    return false;
                }
            }

            targetProperty.SetValue(destination, array);
            details = $"Copied {clonedItems.Count} item(s) into array property.";
            return true;
        }

        if (TryInstantiateCollection(targetProperty.PropertyType, clonedItems, out object? instance))
        {
            targetProperty.SetValue(destination, instance);
            details = $"Copied {clonedItems.Count} item(s) into new '{targetProperty.PropertyType.Name}' instance.";
            return true;
        }

        details = "Target collection type is not supported for assignment.";
        return false;
    }

    private static PropertyInfo? ResolveTargetProperty(Type targetType, PropertyInfo sourceProperty, ISet<string> keywords)
    {
        PropertyInfo? exactMatch = targetType.GetProperty(sourceProperty.Name, Flags);
        if (exactMatch is not null)
            return exactMatch;

        string normalizedSource = NormalizeName(sourceProperty.Name);
        PropertyInfo? normalizedMatch = targetType
            .GetProperties(Flags)
            .FirstOrDefault(property => NormalizeName(property.Name).Equals(normalizedSource, StringComparison.OrdinalIgnoreCase));

        if (normalizedMatch is not null)
            return normalizedMatch;

        string sourceName = sourceProperty.Name;
        PropertyInfo? keywordMatch = targetType
            .GetProperties(Flags)
            .Where(property => property.CanWrite || property.PropertyType.IsClass || property.PropertyType.IsValueType)
            .FirstOrDefault(property =>
            {
                string normalizedTarget = NormalizeName(property.Name);
                return keywords.Any(keyword =>
                    property.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    sourceName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    normalizedTarget.Contains(NormalizeName(keyword), StringComparison.OrdinalIgnoreCase));
            });

        return keywordMatch;
    }

    private static string NormalizeName(string name)
    {
        Span<char> buffer = stackalloc char[name.Length];
        int index = 0;
        foreach (char ch in name)
        {
            if (char.IsLetterOrDigit(ch))
                buffer[index++] = char.ToLowerInvariant(ch);
        }

        return new string(buffer[..index]);
    }

    private static bool TryInstantiateCollection(Type targetType, IReadOnlyList<object?> clonedItems, out object? instance)
    {
        instance = null;
        if (targetType.IsAbstract || targetType.IsInterface)
            return false;

        try
        {
            instance = Activator.CreateInstance(targetType);
            if (instance is null || !TryPopulateCollection(instance, clonedItems, out _))
                return false;

            return true;
        }
        catch
        {
            instance = null;
            return false;
        }
    }

    private static object? CloneScalar(object? value)
    {
        if (value is null)
            return null;

        if (value is string || value.GetType().IsValueType)
            return value;

        if (value is ICloneable cloneable)
            return cloneable.Clone();

        MethodInfo? cloneMethod = value.GetType().GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);
        if (cloneMethod is not null)
        {
            try
            {
                return cloneMethod.Invoke(value, null);
            }
            catch
            {
                return value;
            }
        }

        return value;
    }

    private static int CountItems(object value)
    {
        if (value is string)
            return 1;

        if (value is System.Collections.IEnumerable enumerable)
            return enumerable.Cast<object?>().Count();

        return 1;
    }

    private static bool TryPopulateCollection(object collection, IReadOnlyList<object?> items, out string details)
    {
        details = string.Empty;
        MethodInfo? clearMethod = collection.GetType().GetMethod("Clear", Type.EmptyTypes);
        if (clearMethod is null)
        {
            details = "Target collection does not expose a Clear method.";
            return false;
        }

        MethodInfo? addMethod = collection.GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(method =>
                method.Name.Equals("Add", StringComparison.OrdinalIgnoreCase) &&
                method.GetParameters().Length == 1);

        if (addMethod is null)
        {
            details = "Target collection does not expose an Add method.";
            return false;
        }

        ParameterInfo addParameter = addMethod.GetParameters()[0];
        Type parameterType = addParameter.ParameterType;
        if (parameterType.IsByRef)
            parameterType = parameterType.GetElementType() ?? parameterType;

        foreach (object? item in items)
        {
            if (item is null)
            {
                if (parameterType.IsClass || Nullable.GetUnderlyingType(parameterType) is not null)
                    continue;

                details = $"Target collection parameter '{parameterType.Name}' does not accept null values.";
                return false;
            }

            if (!parameterType.IsInstanceOfType(item))
            {
                details = $"Item type '{item.GetType().Name}' is not assignable to '{parameterType.Name}'.";
                return false;
            }
        }

        clearMethod.Invoke(collection, null);
        int added = 0;
        foreach (object? item in items)
        {
            if (item is null)
            {
                addMethod.Invoke(collection, new object?[] { null });
                added++;
                continue;
            }

            addMethod.Invoke(collection, new object?[] { item });
            added++;
        }

        details = $"Copied {added} item(s) into existing collection.";
        return true;
    }

    private static string DescribeAnimSet(UAnimSet animSet)
    {
        string preview = animSet.PreviewSkelMeshName?.Name ?? animSet.GetType().Name;
        int notifyCount = CountKnownPropertyItems(animSet, "Notify");
        int curveCount = CountKnownPropertyItems(animSet, "Curve");
        return $"{preview} | notifies={notifyCount} | curves={curveCount}";
    }

    private static int CountKnownPropertyItems(UAnimSet animSet, string keyword)
    {
        int total = 0;
        foreach (PropertyInfo property in animSet.GetType().GetProperties(Flags))
        {
            if (!property.CanRead || !property.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                continue;

            object? value = SafeGetValue(property, animSet);
            if (value is null)
                continue;

            total += CountItems(value);
        }

        return total;
    }
}

public sealed class NotifyTransferService
{
    private static readonly string[] Keywords = ["notif", "notify", "notifies", "event", "footstep", "fx", "trigger"];

    public AnimationTransferReport Transfer(UAnimSet source, UAnimSet? target = null, Action<string>? log = null)
    {
        return AnimationTransferReflection.Transfer(source, target, "Notify Transfer", Keywords, log);
    }
}

public sealed class CurveTransferService
{
    private static readonly string[] Keywords = ["curve", "curves", "morph", "track"];

    public AnimationTransferReport Transfer(UAnimSet source, UAnimSet? target = null, Action<string>? log = null)
    {
        return AnimationTransferReflection.Transfer(source, target, "Curve Transfer", Keywords, log);
    }
}

