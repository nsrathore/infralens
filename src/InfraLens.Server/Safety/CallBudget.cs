using Microsoft.Extensions.Options;

namespace InfraLens.Server.Safety;

/// <summary>
/// Caps tool calls per process (stdio = one session). Stops a looping agent from hammering AWS APIs.
/// Token and dollar budgets are enforced client-side by the eval harness, which sees usage.
/// </summary>
public sealed class CallBudget(IOptions<InfraLensOptions> options)
{
    private int _used;

    public int Limit { get; } = options.Value.MaxCallsPerSession;

    public int Used => Volatile.Read(ref _used);

    /// <summary>Reserves one call. Returns false when the budget is spent.</summary>
    public bool TryConsume() => Interlocked.Increment(ref _used) <= Limit;
}

public static class OutputBudget
{
    /// <summary>Truncates on a line boundary and says how much was dropped, so the model knows to narrow its query.</summary>
    public static string Truncate(string text, int maxChars)
    {
        if (text.Length <= maxChars)
        {
            return text;
        }

        var cut = text.LastIndexOf('\n', Math.Max(0, maxChars - 1));
        if (cut < maxChars / 2)
        {
            cut = maxChars;
        }

        return text[..cut] +
               $"\n[truncated: {text.Length - cut} of {text.Length} chars omitted. Narrow the time window, add a filter, or lower the limit.]";
    }
}
