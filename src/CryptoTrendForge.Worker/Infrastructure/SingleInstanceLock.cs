namespace CryptoTrendForge.Worker.Infrastructure;

public sealed class SingleInstanceLock : IDisposable
{
    private readonly FileStream _lockStream;

    private SingleInstanceLock(FileStream lockStream)
    {
        _lockStream = lockStream;
    }

    public static SingleInstanceLock? TryAcquire()
    {
        var lockPath = Path.Combine(AppContext.BaseDirectory, "cryptotrendforge.worker.lock");

        try
        {
            var stream = new FileStream(
                lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
            return new SingleInstanceLock(stream);
        }
        catch (IOException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        _lockStream.Dispose();
    }
}
