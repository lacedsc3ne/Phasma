using DiscordRPC;

using Microsoft.Win32;

using PhasmaStrap.Models.Entities;

namespace PhasmaStrap.Integrations
{
    public static class DiscordJoin
    {
        private const string LOG_IDENT = "DiscordJoin";
        private const string SecretPrefix = "phx1|";

        public static bool Enabled => !App.Settings.Prop.HideRPCButtons && App.Settings.Prop.DiscordNativeJoin;

        public static string? MakeSecret(ActivityData data)
        {
            if (data.ServerType != ServerType.Public || data.PlaceId <= 0 || string.IsNullOrEmpty(data.JobId))
                return null;

            string secret = $"{SecretPrefix}{data.PlaceId}|{data.JobId}";
            return secret.Length <= 128 ? secret : null;
        }

        public static (long PlaceId, string JobId)? ParseSecret(string? secret)
        {
            if (string.IsNullOrEmpty(secret) || !secret.StartsWith(SecretPrefix, StringComparison.Ordinal))
                return null;

            string[] parts = secret[SecretPrefix.Length..].Split('|');
            if (parts.Length != 2 || !long.TryParse(parts[0], out long placeId) || placeId <= 0)
                return null;

            if (!Guid.TryParse(parts[1], out Guid jobId))
                return null;

            return (placeId, jobId.ToString());
        }

        public static void Apply(DiscordRPC.RichPresence presence, ActivityData data, int maxPlayers)
        {
            string? secret = Enabled ? MakeSecret(data) : null;

            if (secret is null)
            {
                presence.Party = null;
                presence.Secrets = null;
                return;
            }

            presence.Buttons = null;
            presence.Party = new Party
            {
                ID = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(data.JobId)))[..32],
                Size = 1,
                Max = Math.Max(2, maxPlayers),
                Privacy = Party.PrivacySetting.Public,
            };
            presence.Secrets = new Secrets { JoinSecret = secret };
        }

        public static bool Launch(string? secret)
        {
            if (ParseSecret(secret) is not (long placeId, string jobId))
            {
                App.Logger.WriteLine(LOG_IDENT, "Ignored a join secret that isn't a PhasmaStrap one");
                return false;
            }

            App.Logger.WriteLine(LOG_IDENT, $"Joining place {placeId}, server {jobId} from Discord");
            Process.Start(Paths.Application, $"-player \"roblox://experiences/start?placeId={placeId}&gameInstanceId={jobId}\"");
            return true;
        }

        public static void RegisterLaunchCommands()
        {
            foreach (string appId in new[] { DiscordRichPresence.PhasmaStrapApplicationId, DiscordRichPresence.RobloxApplicationId })
                RegisterLaunchCommand(appId);
        }

        public static void RegisterLaunchCommand(string appId)
        {
            try
            {
                string command = $"\"{Paths.Application}\" -discordjoin {appId}";
                string key = $@"Software\Classes\discord-{appId}";

                using RegistryKey? existing = Registry.CurrentUser.OpenSubKey($@"{key}\shell\open\command");
                if (existing?.GetValue(null) as string == command)
                    return;

                using RegistryKey root = Registry.CurrentUser.CreateSubKey(key);
                root.SetValue(null, $"URL:Run game {appId} protocol");
                root.SetValue("URL Protocol", "");

                using (RegistryKey icon = root.CreateSubKey("DefaultIcon"))
                    icon.SetValue(null, $"\"{Paths.Application}\",0");

                using (RegistryKey open = root.CreateSubKey(@"shell\open\command"))
                    open.SetValue(null, command);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not register the Discord join command for {appId}: {ex.Message}");
            }
        }

        public static void Subscribe(DiscordRpcClient client, string appId)
        {
            try
            {
                client.RegisterUriScheme();
                RegisterLaunchCommand(appId);
                client.Subscribe(EventType.Join);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not listen for Discord joins: {ex.Message}");
            }
        }

        public static void RunJoinHandler(string? appId)
        {
            if (appId != DiscordRichPresence.PhasmaStrapApplicationId && appId != DiscordRichPresence.RobloxApplicationId)
                appId = DiscordRichPresence.PhasmaStrapApplicationId;

            Task.Run(async () =>
            {
                using var client = new DiscordRpcClient(appId);
                var received = new TaskCompletionSource<string>();

                client.OnJoin += (_, e) => received.TrySetResult(e.Secret);
                client.OnError += (_, e) => App.Logger.WriteLine(LOG_IDENT, $"Discord error: {e.Message}");

                Subscribe(client, appId);
                client.Initialize();

                Task finished = await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(20)));

                if (finished == received.Task)
                    Launch(received.Task.Result);
                else
                    App.Logger.WriteLine(LOG_IDENT, "Discord started PhasmaStrap for a join, but no join arrived");

                App.Current.Dispatcher.Invoke(() => App.Terminate());
            });
        }
    }
}
