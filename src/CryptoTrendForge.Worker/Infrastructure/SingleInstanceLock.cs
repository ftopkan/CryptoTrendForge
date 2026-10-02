using System.Diagnostics;

namespace CryptoTrendForge.Worker.Infrastructure;

public sealed class SingleInstanceLock : IDisposable
{
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

    private static bool TryClearStaleLock(string lockPath, string pidPath)
    {
        if (!File.Exists(pidPath) || !int.TryParse(File.ReadAllText(pidPath).Trim(), out var pid))
        {
            return false;
        }

        try
        {
            _ = Process.GetProcessById(pid);
            return false;
        }
        catch (ArgumentException)
        {
        }

        try
        {
            if (File.Exists(lockPath))
            {
                File.Delete(lockPath);
            }

            File.Delete(pidPath);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
