using Serilog;

namespace CryptoTrendForge.Worker.Infrastructure;

internal static class RunOnceForceExit
{
    public static void ScheduleForcedExitIfStillRunning(TimeSpan shutdownGrace)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(shutdownGrace);
            Log.Warning(
                "RunOnce shutdown did not finish within {GraceSeconds}s; forcing process exit to release the instance lock.",
                (int)shutdownGrace.TotalSeconds);
            Environment.Exit(0);
        });
    }

    public static void Register(IHostApplicationLifetime lifetime, TimeSpan runOnceTimeout, TimeSpan shutdownGrace)
    {
        lifetime.ApplicationStarted.Register(() =>
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(runOnceTimeout);
                    Log.Warning(
                        "RunOnce cycle exceeded {TimeoutMinutes} minutes; requesting shutdown so the next cron run is not blocked.",
                        runOnceTimeout.TotalMinutes);
                    lifetime.StopApplication();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "RunOnce watchdog failed.");
                }
            });
        });

        lifetime.ApplicationStopping.Register(() =>
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(shutdownGrace);
                    Log.Warning(
                        "RunOnce shutdown did not finish within {GraceSeconds}s; forcing process exit to release the instance lock.",
                        (int)shutdownGrace.TotalSeconds);
                    Environment.Exit(0);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "RunOnce forced exit failed.");
                    Environment.Exit(1);
                }
            });
        });
    }
}
