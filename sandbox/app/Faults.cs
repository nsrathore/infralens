namespace Sandbox.Api;

/// <summary>
/// Application-level fault switches. Infrastructure faults (IAM, networking, capacity, config) live in Terraform
/// variables instead; see incidents/scenarios.yaml for which scenario uses which.
/// </summary>
public sealed class Faults
{
    private readonly HashSet<string> _modes;
    private static readonly List<byte[]> Leak = [];

    public Faults(string faultMode)
    {
        _modes = faultMode
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public bool MemoryLeak => _modes.Contains("leak");
    public bool RejectOrders => _modes.Contains("reject_orders");
    public bool SlowDependency => _modes.Contains("slow_db");
    public bool SlowStart => _modes.Contains("slow_start");
    public bool CpuHeavy => _modes.Contains("cpu_heavy");
    public bool NoisyLogs => _modes.Contains("noisy_logs");
    public bool InjectedLog => _modes.Contains("injected_log");

    /// <summary>Retains ~2 MB per request so a small task hits its memory limit under modest load.</summary>
    public void LeakOnRequest()
    {
        if (MemoryLeak)
        {
            var block = new byte[2 * 1024 * 1024];
            Random.Shared.NextBytes(block); // touch the pages so they count against the cgroup
            lock (Leak)
            {
                Leak.Add(block);
            }
        }
    }

    /// <summary>Simulates an expensive pricing calculation: ~150 ms of CPU per order.</summary>
    public void MaybeBurnCpu()
    {
        if (!CpuHeavy)
        {
            return;
        }

        var deadline = Environment.TickCount64 + 150;
        var buffer = new byte[4096];
        while (Environment.TickCount64 < deadline)
        {
            System.Security.Cryptography.SHA256.HashData(buffer, buffer.AsSpan(0, 32));
        }
    }

    /// <summary>Adds 8 s before dependency calls; longer than the ALB idle timeout set in incident 'alb-timeout-slow-dependency'.</summary>
    public Task MaybeSlowDependencyAsync(CancellationToken ct) =>
        SlowDependency ? Task.Delay(TimeSpan.FromSeconds(8), ct) : Task.CompletedTask;
}
