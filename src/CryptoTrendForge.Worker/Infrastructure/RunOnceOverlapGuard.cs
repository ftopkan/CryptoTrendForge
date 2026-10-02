using Serilog;

namespace CryptoTrendForge.Worker.Infrastructure;

public sealed class RunOnceOverlapGuard : IDisposable
{
    private static readonly TimeSpan StaleMarkerAge = TimeSpan.FromMinutes(14);

    private readonly string _markerPath;
    private bool _disposed;

    private RunOnceOverlapGuard(string markerPath)
    {
        _markerPath = markerPath;
    }

    public static bool TryEnter(string logDirectory, out RunOnceOverlapGuard? guard)
    {
        Directory.CreateDirectory(logDirectory);
        var markerPath = Path.Combine(logDirectory, "runonce-active.marker");
        if (File.Exists(markerPath))
        {
            var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(markerPath);
            if (age < StaleMarkerAge)
            {
                Log.Warning(
                    "RunOnce skipped because a previous run marker exists (age {AgeMinutes:F1} min).",
                    age.TotalMinutes);
                guard = null;
                return false;
            }

            Log.Warning(
                "Removing stale RunOnce marker (age {AgeMinutes:F1} min) and starting a new run.",
                age.TotalMinutes);
        }

        File.WriteAllText(markerPath, $"{DateTimeOffset.UtcNow:O}{Environment.NewLine}pid={Environment.ProcessId}");
        guard = new RunOnceOverlapGuard(markerPath);
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            if (File.Exists(_markerPath))
            {
                File.Delete(_markerPath);
            }
        }
        catch (IOException ex)
        {
            Log.Warning(ex, "Failed to delete RunOnce marker file.");
        }
    }
}
