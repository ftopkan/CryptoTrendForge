using System.Diagnostics;

namespace CryptoTrendForge.Worker.Infrastructure;

internal static class HardKill
{
    public static void Now()
    {
        Environment.Exit(0);
    }

    public static void Force()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "taskkill.exe",
                    ArgumentList = { "/F", "/T", "/PID", Environment.ProcessId.ToString() },
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
            }
            else if (File.Exists("/bin/kill"))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "/bin/kill",
                    ArgumentList = { "-9", Environment.ProcessId.ToString() },
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
            }
        }
        catch (Exception)
        {
        }

        Thread.Sleep(TimeSpan.FromSeconds(2));
        Environment.Exit(0);
    }
}
