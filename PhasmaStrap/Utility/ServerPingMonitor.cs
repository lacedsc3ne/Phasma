using System.Net.NetworkInformation;

namespace PhasmaStrap.Utility
{
    internal static class ServerPingMonitor
    {
        private const string LOG_IDENT = "ServerPingMonitor";
        private const int PollIntervalMs = 2000;
        private const int TimeoutMs = 1200;

        private static CancellationTokenSource? _cts;
        private static Task? _loopTask;
        private static volatile int _latestMs = -1;

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
            try
            {
                string? pingable = await ConnectionDoctor.FindPingableAsync(address, token).ConfigureAwait(false);
                if (pingable is not null && pingable != address)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"{address} does not answer pings - measuring {pingable} in the same datacenter instead");
                    address = pingable;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }

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
