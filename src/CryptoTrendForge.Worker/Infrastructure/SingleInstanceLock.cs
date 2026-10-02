using System.Diagnostics;
using Serilog;

namespace CryptoTrendForge.Worker.Infrastructure;

public sealed class SingleInstanceLock : IDisposable
{
    private static readonly TimeSpan StaleLockAge = TimeSpan.FromMinutes(12);

    private readonly FileStream _lockStream;
    private readonly string? _pidPath;

    private SingleInstanceLock(FileStream lockStream, string? pidPath)
    {
        _lockStream = lockStream;
        _pidPath = pidPath;
    }

    public static SingleInstanceLock? TryAcquire()
    {
        var lockPath = Path.Combine(AppContext.BaseDirectory, "cryptotrendforge.worker.lock");
        var pidPath = lockPath + ".pid";

        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var stream = new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None);
                WriteCurrentProcessId(pidPath);
                return new SingleInstanceLock(stream, pidPath);
            }
            catch (IOException) when (attempt == 0 && TryClearStaleLock(lockPath, pidPath))
            {
            }
            catch (IOException)
            {
                LogLockHolder(pidPath);
                return null;
            }
        }

        return null;
    }

    public void Dispose()
    {
        _lockStream.Dispose();
        if (_pidPath is not null)
        {
            try
            {
                File.Delete(_pidPath);
            }
            catch (IOException)
            {
            }
        }
    }

    private static void WriteCurrentProcessId(string pidPath)
    {
        File.WriteAllText(pidPath, Environment.ProcessId.ToString());
    }

    private static void LogLockHolder(string pidPath)
    {
        if (!File.Exists(pidPath))
        {
            Log.Warning("Instance lock is held but {PidPath} is missing.", pidPath);
            return;
        }

        var pidText = File.ReadAllText(pidPath).Trim();
        var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(pidPath);
        Log.Warning(
            "Instance lock is held. RecordedPid={RecordedPid} LockAgeMinutes={LockAgeMinutes:F1}",
            pidText,
            age.TotalMinutes);
    }

    private static bool TryClearStaleLock(string lockPath, string pidPath)
    {
        if (!File.Exists(pidPath) || !int.TryParse(File.ReadAllText(pidPath).Trim(), out var pid))
        {
            return TryClearOrphanLockFile(lockPath, pidPath);
        }

        var lockAge = DateTime.UtcNow - File.GetLastWriteTimeUtc(pidPath);
        Process? holder = null;
        try
        {
            holder = Process.GetProcessById(pid);
        }
        catch (ArgumentException)
        {
            return DeleteLockFiles(lockPath, pidPath);
        }

        if (lockAge <= StaleLockAge)
        {
            return false;
        }

        try
        {
            Log.Warning(
                "Stale worker lock detected (PID {Pid}, age {AgeMinutes:F1} min). Terminating holder process.",
                pid,
                lockAge.TotalMinutes);
            holder.Kill(entireProcessTree: true);
            holder.WaitForExit(5_000);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to terminate stale lock holder PID {Pid}.", pid);
            return false;
        }

        return DeleteLockFiles(lockPath, pidPath);
    }

    private static bool TryClearOrphanLockFile(string lockPath, string pidPath)
    {
        if (!File.Exists(lockPath))
        {
            return false;
        }

        var lockAge = DateTime.UtcNow - File.GetLastWriteTimeUtc(lockPath);
        if (lockAge <= StaleLockAge)
        {
            return false;
        }

        Log.Warning("Clearing orphan lock file older than {AgeMinutes:F1} minutes.", lockAge.TotalMinutes);
        return DeleteLockFiles(lockPath, pidPath);
    }

    private static bool DeleteLockFiles(string lockPath, string pidPath)
    {
        try
        {
            if (File.Exists(lockPath))
            {
                File.Delete(lockPath);
            }

            if (File.Exists(pidPath))
            {
                File.Delete(pidPath);
            }

            return true;
        }
        catch (IOException ex)
        {
            Log.Error(ex, "Failed to delete stale lock files.");
            return false;
        }
    }
}
