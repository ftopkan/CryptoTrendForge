using System.Diagnostics;

namespace CryptoTrendForge.Worker.Infrastructure;

internal static class StuckWorkerCleanup
{
    public static int KillOlderSiblingWorkers(TimeSpan minimumAge)
    {
        var killed = OperatingSystem.IsWindows()
            ? KillWindowsSiblings(minimumAge)
            : KillLinuxSiblings(minimumAge);

        if (killed > 0)
        {
            Thread.Sleep(TimeSpan.FromSeconds(2));
        }

        return killed;
    }

    private static int KillLinuxSiblings(TimeSpan minimumAge)
    {
        if (!Directory.Exists("/proc"))
        {
            return 0;
        }

        var killed = 0;
        var self = Environment.ProcessId;
        foreach (var pidDir in Directory.GetDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(pidDir), out var pid) || pid == self)
            {
                continue;
            }

            string commandLine;
            try
            {
                commandLine = File.ReadAllText(Path.Combine(pidDir, "cmdline")).Replace('\0', ' ');
            }
            catch (IOException)
            {
                continue;
            }

            if (!commandLine.Contains("CryptoTrendForge.Worker.dll", StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                var process = Process.GetProcessById(pid);
                var age = DateTime.UtcNow - process.StartTime.ToUniversalTime();
                if (age < minimumAge)
                {
                    continue;
                }

                process.Kill(entireProcessTree: true);
                killed++;
            }
            catch (Exception)
            {
            }
        }

        return killed;
    }

    private static int KillWindowsSiblings(TimeSpan minimumAge)
    {
        const string script =
            "Get-CimInstance Win32_Process -Filter \"Name = 'dotnet.exe'\" | " +
            "Where-Object { $_.CommandLine -like '*CryptoTrendForge.Worker.dll*' } | " +
            "ForEach-Object { '{0}|{1:o}' -f $_.ProcessId, $_.CreationDate.ToUniversalTime() }";

        Process? powerShell;
        try
        {
            powerShell = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                ArgumentList = { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch (Exception)
        {
            return 0;
        }

        if (powerShell is null)
        {
            return 0;
        }

        using (powerShell)
        {
            var stdout = powerShell.StandardOutput.ReadToEndAsync();
            var stderr = powerShell.StandardError.ReadToEndAsync();
            if (!powerShell.WaitForExit(15_000))
            {
                try
                {
                    powerShell.Kill(entireProcessTree: true);
                }
                catch (Exception)
                {
                }

                return 0;
            }

            var output = stdout.GetAwaiter().GetResult();
            _ = stderr.GetAwaiter().GetResult();

            var killed = 0;
            var self = Environment.ProcessId;
            foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Trim().Split('|', 2);
                if (parts.Length == 0 || !int.TryParse(parts[0], out var pid) || pid == self)
                {
                    continue;
                }

                try
                {
                    var process = Process.GetProcessById(pid);
                    if (parts.Length == 2 && DateTimeOffset.TryParse(parts[1], out var started))
                    {
                        var age = DateTimeOffset.UtcNow - started;
                        if (age < minimumAge)
                        {
                            continue;
                        }
                    }

                    process.Kill(entireProcessTree: true);
                    killed++;
                }
                catch (Exception)
                {
                }
            }

            return killed;
        }
    }
}
