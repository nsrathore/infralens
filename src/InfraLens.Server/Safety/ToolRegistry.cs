using System.Reflection;
using ModelContextProtocol.Server;

namespace InfraLens.Server.Safety;

/// <summary>
/// Maps every MCP tool name to its permission tier by reflecting over [McpServerToolType] classes.
/// A tool without an explicit Name or [ToolTier] fails at startup, so nothing ships untiered.
/// </summary>
public sealed class ToolRegistry
{
    private readonly Dictionary<string, ToolTier> _tiers;

    public ToolRegistry(IReadOnlyDictionary<string, ToolTier> tiers) => _tiers = new(tiers);

    public static ToolRegistry FromAssembly(Assembly assembly)
    {
        var tiers = new Dictionary<string, ToolTier>(StringComparer.Ordinal);
        foreach (var method in ToolMethodsIn(assembly))
        {
            var tool = method.GetCustomAttribute<McpServerToolAttribute>()!;
            var name = tool.Name ?? throw new InvalidOperationException(
                $"{method.DeclaringType!.Name}.{method.Name} must set an explicit tool Name.");
            var tier = method.GetCustomAttribute<ToolTierAttribute>()?.Tier ?? throw new InvalidOperationException(
                $"Tool '{name}' has no [ToolTier] attribute.");
            if (!tiers.TryAdd(name, tier))
            {
                throw new InvalidOperationException($"Duplicate tool name '{name}'.");
            }
        }
        return new ToolRegistry(tiers);
    }

    /// <summary>Tool classes whose every tool is at or below <paramref name="maxTier"/>.</summary>
    public static IEnumerable<Type> ToolTypesUpTo(Assembly assembly, ToolTier maxTier) =>
        assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
            .Where(t => ToolMethodsOf(t).All(m => (m.GetCustomAttribute<ToolTierAttribute>()?.Tier ?? ToolTier.Write) <= maxTier));

    public ToolTier? TierOf(string toolName) => _tiers.TryGetValue(toolName, out var tier) ? tier : null;

    public IReadOnlyCollection<string> Names => _tiers.Keys;

    private static IEnumerable<MethodInfo> ToolMethodsIn(Assembly assembly) =>
        assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
            .SelectMany(t => ToolMethodsOf(t));

    private static IEnumerable<MethodInfo> ToolMethodsOf(Type type) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null);
}
