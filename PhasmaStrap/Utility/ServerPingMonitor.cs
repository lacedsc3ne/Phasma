using System.Net.NetworkInformation;

namespace PhasmaStrap.Utility
{
    // Backs the overlay HUD's "Show ping" row. Pings the current server's real machine address -
    // captured by ActivityWatcher from Roblox's own join log line (GameJoiningEntryPattern /
    // GameJoiningUDMUXPattern), the same address ServerInformationViewModel's "server location"
    // lookup already relies on being real. Runs its own slow background loop (ICMP round-trips
    // aren't something to do on the render thread) rather than sampling inline from
    // OverlayCompositor.UpdateHudIfDue like SystemStatsSampler does.
    internal static class ServerPingMonitor
    {
        private const string LOG_IDENT = "ServerPingMonitor";
        private const int PollIntervalMs = 2000;
        private const int TimeoutMs = 1200;

        private static CancellationTokenSource? _cts;
        private static Task? _loopTask;
        private static volatile int _latestMs = -1;

        /// <summary>Last measured round-trip in milliseconds, or -1 if there's no reading yet.</summary>
        public static int LatestMs => _latestMs;

        public static void Start(string? address)
        {
            Stop();

            if (string.IsNullOrEmpty(address))
                return;

            _latestMs = -1;
            var cts = new CancellationTokenSource();
            _cts = cts;
            _loopTask = Task.Run(() => LoopAsync(address, cts.Token));
        }

        public static void Stop()
        {
            _cts?.Cancel();
            _cts = null;
            _loopTask = null;
            _latestMs = -1;
        }

        private static async Task LoopAsync(string address, CancellationToken token)
        {
            using var ping = new Ping();

            while (!token.IsCancellationRequested)
            {
                try
                {
                    PingReply reply = await ping.SendPingAsync(address, TimeoutMs).ConfigureAwait(false);
                    _latestMs = reply.Status == IPStatus.Success ? (int)reply.RoundtripTime : -1;
                }
                catch (Exception ex)
                {
                    _latestMs = -1;
                    App.Logger.WriteException(LOG_IDENT, ex);
                }

                try
                {
                    await Task.Delay(PollIntervalMs, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }
}
