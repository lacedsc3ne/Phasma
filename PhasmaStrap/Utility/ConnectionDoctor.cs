using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace PhasmaStrap.Utility
{
    public sealed class RouteHop
    {
        public int Ttl;
        public string Address = "";     // "" = no answer at this distance
        public int Milliseconds = -1;
        public bool IsDestination;
    }

    public sealed class PingTarget
    {
        public string Label = "";
        public string Address = "";
        public List<int> Samples = new();   // round trip in ms, -1 = lost

        public int Sent => Samples.Count;
        public int Lost => Samples.Count(s => s < 0);
        public double LossPercent => Sent == 0 ? 0 : Lost * 100.0 / Sent;
        public double Average => Samples.Any(s => s >= 0) ? Samples.Where(s => s >= 0).Average() : 0;
        public int Worst => Samples.Any(s => s >= 0) ? Samples.Max() : 0;

        // mean difference between consecutive replies - what "jitter" means for a game
        public double Jitter
        {
            get
            {
                List<int> ok = Samples.Where(s => s >= 0).ToList();
                if (ok.Count < 2)
                    return 0;

                double sum = 0;
                for (int i = 1; i < ok.Count; i++)
                    sum += Math.Abs(ok[i] - ok[i - 1]);
                return sum / (ok.Count - 1);
            }
        }

        // replies that took more than twice the usual time (and at least 30 ms more) - the rubber-band moments
        public int Spikes
        {
            get
            {
                double average = Average;
                return Samples.Count(s => s >= 0 && s > average * 2 && s > average + 30);
            }
        }

        public bool Answers => Samples.Any(s => s >= 0);
    }

    public sealed class ConnectionReport
    {
        public string Server = "";
        public string Adapter = "";
        public bool Wireless;
        public List<RouteHop> Route = new();
        public List<PingTarget> Targets = new();
        public string Verdict = "";
        public List<string> Findings = new();

        // the server itself ignores pings, so a machine beside it in the same datacenter was measured
        public bool MeasuredNeighbour;
    }

    // The Diagnostics page's connection test: where between this PC and the game server does the
    // trouble start? A route trace finds the stations on the way; then the router, the first
    // station at the internet provider and the game server are pinged side by side for a while, so
    // loss and jitter can be compared at the same moments.
    //
    // One rule keeps the verdict honest: stations along the way are allowed to answer pings badly
    // (routers deliberately treat them as low priority), so a station is only blamed when the game
    // server is suffering too.
    //
    // Plain ICMP through the Ping class - no admin rights, nothing installed. No App dependencies.
    public static class ConnectionDoctor
    {
        public static Action<string>? Log;

        // the address of the server Roblox connected to last, from its newest logs
        public static string? FindLastServer(string robloxLogsDirectory)
        {
            try
            {
                foreach (FileInfo file in new DirectoryInfo(robloxLogsDirectory).GetFiles("*.log").OrderByDescending(f => f.LastWriteTime).Take(4))
                {
                    string text;
                    using (var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var reader = new StreamReader(stream))
                        text = reader.ReadToEnd();

                    MatchCollection matches = Regex.Matches(text, @"UDMUX Address = ([0-9.]+)|Connecting to ([0-9.]+):");
                    for (int i = matches.Count - 1; i >= 0; i--)
                    {
                        string ip = matches[i].Groups[1].Success ? matches[i].Groups[1].Value : matches[i].Groups[2].Value;
                        if (!ip.StartsWith("10.") && IPAddress.TryParse(ip, out _))
                            return ip;
                    }
                }
            }
            catch (Exception ex)
            {
                Log?.Invoke($"Could not read the Roblox logs: {ex.Message}");
            }

            return null;
        }

        // Roblox game servers do not answer pings. Machines next to them in the same datacenter
        // rack do (x.y.z.3, .4, .8, .9 in every Roblox /24 looked at, with round trips that match
        // the geography), and the way there is the same - so that is what gets measured.
        // Returns the server itself when it does answer, null when nothing nearby does.
        public static async Task<string?> FindPingableAsync(string server, CancellationToken token = default)
        {
            if (!IPAddress.TryParse(server, out IPAddress? ip) || ip.AddressFamily != AddressFamily.InterNetwork)
                return null;

            byte[] b = ip.GetAddressBytes();
            var candidates = new List<string> { server };
            foreach (int last in new[] { 3, 4, 8, 9 })
                candidates.Add($"{b[0]}.{b[1]}.{b[2]}.{last}");

            using var ping = new Ping();

            foreach (string candidate in candidates)
            {
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    token.ThrowIfCancellationRequested();

                    try
                    {
                        PingReply reply = await ping.SendPingAsync(IPAddress.Parse(candidate), 900);
                        if (reply.Status == IPStatus.Success)
                            return candidate;
                    }
                    catch (PingException)
                    {
                    }
                }
            }

            return null;
        }

        public static bool IsPrivate(string address)
        {
            if (!IPAddress.TryParse(address, out IPAddress? ip) || ip.AddressFamily != AddressFamily.InterNetwork)
                return false;

            byte[] b = ip.GetAddressBytes();
            return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254);
        }

        public static async Task<ConnectionReport> RunAsync(string server, int seconds, Action<string> status, Action<double> progress, CancellationToken token)
        {
            var report = new ConnectionReport { Server = server };

            // ---- how is this PC connected
            string gateway = "";
            foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up || adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                    continue;

                GatewayIPAddressInformation? route = adapter.GetIPProperties().GatewayAddresses
                    .FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));

                if (route is null)
                    continue;

                gateway = route.Address.ToString();
                report.Adapter = adapter.Description;
                report.Wireless = adapter.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;
                break;
            }

            // ---- the route
            status("Tracing the route to the game server...");
            byte[] payload = new byte[32];

            using (var ping = new Ping())
            {
                for (int ttl = 1; ttl <= 30; ttl++)
                {
                    token.ThrowIfCancellationRequested();
                    progress(ttl / 30.0 * 0.25);

                    var hop = new RouteHop { Ttl = ttl };

                    for (int attempt = 0; attempt < 2 && hop.Address.Length == 0; attempt++)
                    {
                        try
                        {
                            PingReply reply = await ping.SendPingAsync(IPAddress.Parse(server), 900, payload, new PingOptions(ttl, true));

                            if (reply.Status is IPStatus.TtlExpired or IPStatus.Success)
                            {
                                hop.Address = reply.Address.ToString();
                                hop.IsDestination = reply.Status == IPStatus.Success;

                                // a TTL-expired reply carries no timing of its own
                                hop.Milliseconds = reply.Status == IPStatus.Success ? (int)reply.RoundtripTime : -1;
                            }
                        }
                        catch (PingException)
                        {
                        }
                    }

                    report.Route.Add(hop);

                    if (hop.IsDestination)
                        break;

                    // the last stretch of many routes stays silent - no point in knocking 20 more times
                    if (ttl >= 12 && report.Route.TakeLast(6).All(h => h.Address.Length == 0))
                        break;
                }
            }

            // ---- who to watch: the router, the first station outside the home that answers, the server
            var targets = new List<PingTarget>();

            if (gateway.Length > 0)
                targets.Add(new PingTarget { Label = "Your router", Address = gateway });

            using (var probe = new Ping())
            {
                foreach (RouteHop hop in report.Route.Where(h => h.Address.Length > 0 && !h.IsDestination && !IsPrivate(h.Address) && h.Address != gateway))
                {
                    token.ThrowIfCancellationRequested();

                    try
                    {
                        PingReply reply = await probe.SendPingAsync(IPAddress.Parse(hop.Address), 900);
                        if (reply.Status == IPStatus.Success)
                        {
                            targets.Add(new PingTarget { Label = "Your internet provider", Address = hop.Address });
                            break;
                        }
                    }
                    catch (PingException)
                    {
                    }
                }
            }

            status("Finding something in the server's datacenter that answers...");
            string? measured = await FindPingableAsync(server, token);
            report.MeasuredNeighbour = measured is not null && measured != server;

            targets.Add(new PingTarget { Label = "The game server", Address = measured ?? server });
            report.Targets = targets;

            // ---- side by side, five times a second
            status($"Measuring for {seconds} seconds...");
            var clock = Stopwatch.StartNew();
            var pingers = targets.Select(_ => new Ping()).ToList();

            try
            {
                int round = 0;
                while (clock.Elapsed.TotalSeconds < seconds)
                {
                    token.ThrowIfCancellationRequested();

                    Task<int>[] sends = targets.Select((target, index) => SendAsync(pingers[index], target.Address)).ToArray();
                    int[] replies = await Task.WhenAll(sends);

                    for (int i = 0; i < targets.Count; i++)
                        targets[i].Samples.Add(replies[i]);

                    progress(0.25 + clock.Elapsed.TotalSeconds / seconds * 0.75);

                    round++;
                    double wait = round * 200 - clock.Elapsed.TotalMilliseconds;
                    if (wait > 1)
                        await Task.Delay((int)wait, token);
                }
            }
            finally
            {
                foreach (Ping pinger in pingers)
                    pinger.Dispose();
            }

            Judge(report);
            progress(1);
            return report;
        }

        private static async Task<int> SendAsync(Ping ping, string address)
        {
            try
            {
                PingReply reply = await ping.SendPingAsync(IPAddress.Parse(address), 1000);
                return reply.Status == IPStatus.Success ? (int)reply.RoundtripTime : -1;
            }
            catch (PingException)
            {
                return -1;
            }
        }

        private static bool Troubled(PingTarget target, double jitterLimit) =>
            target.Answers && (target.LossPercent >= 1.5 || target.Jitter >= jitterLimit || target.Spikes >= Math.Max(3, target.Sent / 40));

        public static void Judge(ConnectionReport report)
        {
            PingTarget? router = report.Targets.FirstOrDefault(t => t.Label == "Your router");
            PingTarget? provider = report.Targets.FirstOrDefault(t => t.Label == "Your internet provider");
            PingTarget server = report.Targets.Last();

            var findings = report.Findings;

            if (!server.Answers)
            {
                report.Verdict = "This game server does not answer pings, so the connection to it cannot be measured this way.";
                findings.Add("Some Roblox servers ignore pings. The route below still shows how far the trace got; try again on another server.");

                if (router is not null && Troubled(router, 6))
                    findings.Add($"Your own network is unsteady though: {Describe(router)}.{(report.Wireless ? " You are on Wi-Fi - that is the usual cause." : "")}");

                return;
            }

            bool serverBad = Troubled(server, 10);
            bool routerBad = router is not null && Troubled(router, 6);
            bool providerBad = provider is not null && Troubled(provider, 10);

            findings.Add(report.MeasuredNeighbour
                ? $"Game server's datacenter ({server.Address}, a machine beside the server - Roblox game servers themselves ignore pings): {Describe(server)}."
                : $"Game server: {Describe(server)}.");
            if (router is not null) findings.Add($"Your router: {(router.Answers ? Describe(router) : "does not answer pings")}.");
            if (provider is not null) findings.Add($"Your internet provider ({provider.Address}): {Describe(provider)}.");

            if (!serverBad)
            {
                report.Verdict = server.Average > 140
                    ? $"The connection is steady, but the server is far away (about {server.Average:0} ms). Nothing is broken - a closer server would feel better."
                    : $"The connection to this server is healthy: about {server.Average:0} ms, steady, nothing lost.";

                if (routerBad || providerBad)
                    findings.Add("A station on the way answered pings unevenly while the game server itself was fine. Routers treat pings as low priority, so that alone is not a fault.");

                return;
            }

            if (routerBad)
            {
                report.Verdict = "The trouble starts inside your own network - between this PC and your router.";
                findings.Add(report.Wireless
                    ? "You are on Wi-Fi. Interference and distance to the router cause exactly this pattern; a cable, the 5 GHz band or moving closer are the fixes that work."
                    : "You are on a cable, so look at the router itself (restart it, check who else is saturating the line) and at the cable or switch in between.");
            }
            else if (providerBad)
            {
                report.Verdict = "Your own network is fine; the trouble starts at your internet provider.";
                findings.Add("Loss or jitter appears at the first station outside your home and carries through to the game server. That is the provider's network (or the line to it) - restarting the modem is worth one try, after that it is a call to them with these numbers.");
            }
            else
            {
                report.Verdict = "Your network and your provider look fine; the trouble is further out - on the route to Roblox or at this server.";
                findings.Add("Another server (or another region) will most likely behave better. If every server is like this for days, the path between your provider and Roblox is congested, which only they can fix.");
            }
        }

        private static string Describe(PingTarget target) =>
            $"{target.Average:0} ms on average, jitter {target.Jitter:0.0} ms, worst {target.Worst} ms, {target.Lost} of {target.Sent} lost ({target.LossPercent:0.#} %)" + (target.Spikes > 0 ? $", {target.Spikes} spike(s)" : "");
    }
}
