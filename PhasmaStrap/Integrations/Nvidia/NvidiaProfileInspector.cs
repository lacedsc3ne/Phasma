using System.Runtime.InteropServices;

namespace PhasmaStrap.Integrations.Nvidia
{
    public sealed class NvidiaSetting
    {
        public uint Id { get; init; }

        public string Name { get; init; } = string.Empty;

        public uint Value { get; init; }

        public NvSettingType Type { get; init; }

        public bool IsPredefined { get; init; }

        public string HexId => "0x" + Id.ToString("X8", CultureInfo.InvariantCulture);
    }

    public sealed class NvidiaApplyResult
    {
        public bool Ok { get; init; }

        public string Message { get; init; } = string.Empty;

        public int Applied { get; init; }

        public List<string> Failures { get; } = new List<string>();
    }

    // Reads and writes a dedicated NVIDIA driver profile for Roblox via the raw NVAPI
    // wrapper in NvApi.cs. This talks to the driver directly - it is a completely
    // separate mechanism from Roblox's own FastFlags/ClientSettings, and affects
    // rendering at the driver level (DX/OpenGL/Vulkan submission), not the game client.
    //
    // Ported and trimmed down from Voidstrap's Integrations/Nvidia/NvidiaProfileInspector.cs.
    // The original also backed a full profile-inspector-style editor (arbitrary setting
    // ID/value rows, .nip import/export, "copy settings from another app" dialogs); none
    // of that generic editing machinery was ported, only the read/apply/reset primitives
    // that PhasmaStrap's curated NVIDIA settings page needs. See NvidiaViewModel for the
    // specific setting IDs actually exposed to the user.
    public static class NvidiaProfileInspector
    {
        private sealed class Session : IDisposable
        {
            private bool _disposed;

            public Session(IntPtr handle) => Handle = handle;

            public IntPtr Handle { get; }

            public void Dispose()
            {
                if (_disposed)
                    return;
                _disposed = true;
                NvApi.DestroySession(Handle);
                GC.SuppressFinalize(this);
            }
        }

        public const string DefaultProfileName = "PhasmaStrap";

        public const string NeedsElevationMessage = "Windows only lets an administrator change driver profile settings. Restart PhasmaStrap as administrator and apply again.";

        private static readonly string[] RobloxExecutables = new[] { "RobloxPlayerBeta.exe", "RobloxStudioBeta.exe" };

        public static bool IsAvailable => NvApi.IsAvailable;

        public static string UnavailableReason => NvApi.Failure;

        private static Session? Open(out string error)
        {
            error = string.Empty;
            if (!NvApi.IsAvailable)
            {
                error = NvApi.Failure.Length > 0 ? NvApi.Failure : "NVAPI is unavailable";
                return null;
            }
            NvStatus status = NvApi.CreateSession(out IntPtr session);
            if (status != NvStatus.Ok || session == IntPtr.Zero)
            {
                error = "Could not open a driver settings session (" + status + ")";
                return null;
            }
            status = NvApi.LoadSettings(session);
            if (status != NvStatus.Ok)
            {
                NvApi.DestroySession(session);
                error = "Could not read the driver settings (" + status + ")";
                return null;
            }
            return new Session(session);
        }

        public static List<NvidiaSetting> ReadProfile(string? profileName = null)
        {
            using Session? session = Open(out string error);
            if (session == null)
            {
                App.Logger.WriteLine("NvidiaProfileInspector::ReadProfile", "Skipped: " + error);
                return new List<NvidiaSetting>();
            }
            IntPtr profile = ResolveTargetProfile(session.Handle, profileName, create: false, out string _, out NvStatus status);
            if (profile == IntPtr.Zero)
            {
                App.Logger.WriteLine("NvidiaProfileInspector::ReadProfile", "Profile not found for read (" + status + ")");
                return new List<NvidiaSetting>();
            }
            return Enumerate(session.Handle, profile);
        }

        public static Dictionary<uint, uint> ReadValues(IEnumerable<uint> settingIds, string? profileName = null)
        {
            Dictionary<uint, uint> values = new Dictionary<uint, uint>();
            HashSet<uint> wanted = new HashSet<uint>(settingIds ?? Array.Empty<uint>());
            if (wanted.Count == 0)
                return values;
            foreach (NvidiaSetting setting in ReadProfile(profileName))
            {
                if (setting.Type == NvSettingType.Dword && wanted.Contains(setting.Id))
                    values[setting.Id] = setting.Value;
            }
            return values;
        }

        private static List<NvidiaSetting> Enumerate(IntPtr session, IntPtr profile)
        {
            List<NvidiaSetting> results = new List<NvidiaSetting>();
            const int chunk = 128;
            IntPtr buffer = Marshal.AllocHGlobal(NvApi.SettingSize * chunk);
            try
            {
                uint index = 0u;
                while (true)
                {
                    uint count = chunk;
                    for (int i = 0; i < chunk; i++)
                        NvApi.WriteSettingVersion(buffer, i);
                    if (NvApi.EnumSettings(session, profile, index, ref count, buffer) != NvStatus.Ok || count == 0u)
                        break;
                    for (int i = 0; i < count; i++)
                    {
                        NvSettingType type = NvApi.ReadSettingType(buffer, i);
                        results.Add(new NvidiaSetting
                        {
                            Id = NvApi.ReadSettingId(buffer, i),
                            Name = NvApi.ReadSettingName(buffer, i),
                            Value = type == NvSettingType.Dword ? NvApi.ReadSettingValue(buffer, i) : 0u,
                            Type = type,
                            IsPredefined = NvApi.ReadSettingIsPredefined(buffer, i),
                        });
                    }
                    if (count < chunk)
                        break;
                    index += count;
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("NvidiaProfileInspector::Enumerate", "Enumeration failed: " + ex.Message);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
            return results;
        }

        public static NvidiaApplyResult Apply(IEnumerable<KeyValuePair<uint, uint>> settings, string? profileName = null)
        {
            if (settings == null)
                return Failure("No settings were supplied");

            using Session? session = Open(out string error);
            if (session == null)
                return Failure(error);

            IntPtr profile = ResolveTargetProfile(session.Handle, profileName, create: true, out string targetName, out NvStatus status);
            if (profile == IntPtr.Zero)
                return Failure("Could not resolve a driver profile for Roblox (" + status + ")");

            List<string> rejected = new List<string>();
            Dictionary<uint, uint> requested = new Dictionary<uint, uint>();
            int applied = 0;
            foreach (KeyValuePair<uint, uint> setting in settings)
            {
                NvStatus set = NvApi.SetDwordSetting(session.Handle, profile, setting.Key, setting.Value);
                if (set == NvStatus.Ok)
                {
                    applied++;
                    requested[setting.Key] = setting.Value;
                    continue;
                }
                if (set == NvStatus.InvalidUserPrivilege)
                    return Failure(NeedsElevationMessage);
                rejected.Add("0x" + setting.Key.ToString("X8", CultureInfo.InvariantCulture) + " -> " + set);
            }

            foreach (NvidiaSetting live in Enumerate(session.Handle, profile))
            {
                if (!requested.TryGetValue(live.Id, out uint wanted))
                    continue;
                requested.Remove(live.Id);
                if (live.Type != NvSettingType.Dword || live.Value == wanted)
                    continue;
                applied--;
                rejected.Add("0x" + live.Id.ToString("X8", CultureInfo.InvariantCulture) + " -> the driver stored " + live.Value + " instead of " + wanted);
            }
            foreach (uint missing in requested.Keys)
            {
                applied--;
                rejected.Add("0x" + missing.ToString("X8", CultureInfo.InvariantCulture) + " -> the driver did not keep this setting");
            }

            NvStatus save = NvApi.SaveSettings(session.Handle);
            if (save == NvStatus.InvalidUserPrivilege)
                return Failure(NeedsElevationMessage);
            if (save != NvStatus.Ok)
            {
                NvidiaApplyResult saveFailed = new NvidiaApplyResult
                {
                    Ok = false,
                    Message = "The driver rejected the save (" + save + ")",
                    Applied = applied,
                };
                saveFailed.Failures.AddRange(rejected);
                return saveFailed;
            }

            NvidiaApplyResult result = new NvidiaApplyResult
            {
                Ok = applied > 0,
                Applied = applied,
                Message = rejected.Count == 0
                    ? "Applied " + applied + " setting(s) to the \"" + targetName + "\" driver profile"
                    : applied > 0
                        ? "Applied " + applied + " setting(s) to \"" + targetName + "\", " + rejected.Count + " did not stick"
                        : "The driver did not keep any of the " + rejected.Count + " setting(s)",
            };
            result.Failures.AddRange(rejected);
            App.Logger.WriteLine("NvidiaProfileInspector::Apply", result.Message);
            return result;
        }

        public static NvidiaApplyResult Reset(IEnumerable<uint> settingIds, string? profileName = null)
        {
            using Session? session = Open(out string error);
            if (session == null)
                return new NvidiaApplyResult { Ok = false, Message = error };

            IntPtr profile = ResolveTargetProfile(session.Handle, profileName, create: false, out string _, out NvStatus _);
            if (profile == IntPtr.Zero)
                return new NvidiaApplyResult { Ok = true, Message = "There was no profile to reset" };

            List<string> rejected = new List<string>();
            int cleared = 0;
            foreach (uint id in settingIds ?? Array.Empty<uint>())
            {
                NvStatus status = NvApi.DeleteSetting(session.Handle, profile, id);
                if (status == NvStatus.Ok)
                {
                    cleared++;
                    continue;
                }
                if (status == NvStatus.InvalidUserPrivilege)
                    return new NvidiaApplyResult { Ok = false, Message = NeedsElevationMessage };
                if (status == NvStatus.SettingNotFound)
                    continue;
                rejected.Add("0x" + id.ToString("X8", CultureInfo.InvariantCulture) + " -> " + status);
            }

            NvStatus save = NvApi.SaveSettings(session.Handle);
            if (save == NvStatus.InvalidUserPrivilege)
                return new NvidiaApplyResult { Ok = false, Message = NeedsElevationMessage };

            NvidiaApplyResult result = new NvidiaApplyResult
            {
                Ok = save == NvStatus.Ok,
                Applied = cleared,
                Message = save == NvStatus.Ok
                    ? "Reset " + cleared + " setting(s) to the driver default"
                    : "The driver rejected the save (" + save + ")",
            };
            result.Failures.AddRange(rejected);
            return result;
        }

        private static NvidiaApplyResult Failure(string message)
        {
            return new NvidiaApplyResult { Ok = false, Message = message };
        }

        private static IntPtr ResolveProfile(IntPtr session, string? profileName, bool create, out NvStatus status)
        {
            string name = string.IsNullOrWhiteSpace(profileName) ? DefaultProfileName : profileName.Trim();
            status = NvApi.FindProfileByName(session, name, out IntPtr profile);
            if (status == NvStatus.Ok && profile != IntPtr.Zero)
                return profile;
            if (!create)
                return IntPtr.Zero;
            status = NvApi.CreateProfile(session, name, out profile);
            return status == NvStatus.Ok ? profile : IntPtr.Zero;
        }

        private static IntPtr ResolveTargetProfile(IntPtr session, string? profileName, bool create, out string targetName, out NvStatus status)
        {
            string preferred = string.IsNullOrWhiteSpace(profileName) ? DefaultProfileName : profileName.Trim();
            foreach (string executable in RobloxExecutables)
            {
                try
                {
                    if (NvApi.FindApplication(session, executable, out IntPtr owner) == NvStatus.Ok && owner != IntPtr.Zero)
                    {
                        targetName = NvApi.GetProfileName(session, owner, preferred);
                        status = NvStatus.Ok;
                        return owner;
                    }
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine("NvidiaProfileInspector::ResolveTargetProfile", "Owner lookup for " + executable + " failed: " + ex.Message);
                }
            }

            IntPtr profile = ResolveProfile(session, preferred, create, out status);
            targetName = preferred;
            if (profile == IntPtr.Zero)
                return IntPtr.Zero;
            if (!create)
                return profile;

            foreach (string executable in RobloxExecutables)
            {
                try
                {
                    NvStatus attach = NvApi.CreateApplication(session, profile, executable);
                    if (attach != NvStatus.Ok && attach != NvStatus.ExecutableAlreadyInProfile)
                        App.Logger.WriteLine("NvidiaProfileInspector::ResolveTargetProfile", "Could not attach " + executable + " (" + attach + ")");
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine("NvidiaProfileInspector::ResolveTargetProfile", "Attaching " + executable + " failed: " + ex.Message);
                }
            }
            return profile;
        }
    }
}
