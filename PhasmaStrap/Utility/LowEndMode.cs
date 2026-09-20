using System.Reflection;
using System.Runtime.InteropServices;

namespace PhasmaStrap.Utility
{
    public sealed record LowEndHardware(string GpuName, long GpuMemoryMb, bool IntegratedGpu, long RamMb, int Threads)
    {
        public bool WeakGpu => IntegratedGpu || GpuMemoryMb < 2048;
        public bool LowRam => RamMb < 8 * 1024;
        public bool FewThreads => Threads <= 4;
        public int WeakParts => (WeakGpu ? 1 : 0) + (LowRam ? 1 : 0) + (FewThreads ? 1 : 0);

        public string Recommended => WeakParts switch { 0 => "", 1 => LowEndMode.Light, _ => LowEndMode.Strong };

        public string Describe()
        {
            string gpu = IntegratedGpu ? $"{GpuName} (built into the processor)" : $"{GpuName} ({GpuMemoryMb / 1024.0:0.#} GB)";
            return $"{gpu}, {RamMb / 1024.0:0} GB memory, {Threads} processor threads";
        }
    }

    public sealed record LowEndChange(string Area, string What);

    public static class LowEndMode
    {
        private const string LOG_IDENT = "LowEndMode";

        public const string Light = "Light";
        public const string Strong = "Strong";

        public static string Active => App.Settings.Prop.LowEndModeLevel;

        private static Dictionary<string, string> Flags(string level)
        {
            var flags = new Dictionary<string, string>
            {
                ["DFFlagTextureQualityOverrideEnabled"] = "True",
                ["DFIntTextureQualityOverride"] = level == Strong ? "0" : "1",
                ["DFIntDebugFRMQualityLevelOverride"] = level == Strong ? "1" : "4",
                ["FIntDebugForceMSAASamples"] = "1",
                ["FIntFRMMinGrassDistance"] = "0",
                ["FIntFRMMaxGrassDistance"] = "0",
            };

            if (level == Strong)
            {
                flags["DFIntCSGLevelOfDetailSwitchingDistance"] = "0";
                flags["DFIntCSGLevelOfDetailSwitchingDistanceL12"] = "0";
                flags["DFIntCSGLevelOfDetailSwitchingDistanceL23"] = "0";
                flags["DFIntCSGLevelOfDetailSwitchingDistanceL34"] = "0";
                flags["DFFlagDebugPauseVoxelizer"] = "True";
                flags["FFlagDebugSkyGray"] = "True";
            }

            return flags;
        }

        private static Dictionary<string, object> SettingsFor(string level, LowEndHardware hardware)
        {
            var settings = new Dictionary<string, object>
            {
                [nameof(Models.Persistable.Settings.OptimizeRoblox)] = true,
                [nameof(Models.Persistable.Settings.RobloxEfficiencyMode)] = false,
                [nameof(Models.Persistable.Settings.ReduceMemoryOutOfFocus)] = hardware.LowRam,
                [nameof(Models.Persistable.Settings.RobloxPriorityLimit)] = "Above Normal",
            };

            if (level == Strong)
            {
                settings[nameof(Models.Persistable.Settings.RiShadeEnabled)] = false;
                settings[nameof(Models.Persistable.Settings.AntiAliasingEnabled)] = false;
                settings[nameof(Models.Persistable.Settings.FrameGenModeIndex)] = 0;
                settings[nameof(Models.Persistable.Settings.InstantReplayEnabled)] = false;

                if (App.Settings.Prop.NetworkingProxyEnabled)
                {
                    settings[nameof(Models.Persistable.Settings.AssetRouteEnabled)] = true;
                    settings[nameof(Models.Persistable.Settings.TextureShrinkEnabled)] = true;
                    settings[nameof(Models.Persistable.Settings.TextureShrinkMaxSize)] = 256;
                }
            }

            return settings;
        }

        public static List<LowEndChange> Describe(string level)
        {
            var list = new List<LowEndChange>
            {
                new("Graphics", level == Strong ? "Lowest texture quality" : "Lower texture quality"),
                new("Graphics", level == Strong ? "Graphics quality level 1 of 21" : "Graphics quality level 4 of 21"),
                new("Graphics", "No anti-aliasing"),
                new("Graphics", "No grass"),
            };

            if (level == Strong)
            {
                list.Add(new("Graphics", "Lowest-detail meshes"));
                list.Add(new("Graphics", "Cheaper lighting (paused light voxels)"));
                list.Add(new("Graphics", "Plain grey sky"));
            }

            list.Add(new("Roblox process", "Tuned for games, priority raised to Above Normal"));
            if (DetectHardware().LowRam)
                list.Add(new("Roblox process", "Frees memory when Roblox is in the background"));

            if (level == Strong)
            {
                list.Add(new("PhasmaStrap", "Shaders, anti-aliasing and frame generation off"));
                list.Add(new("PhasmaStrap", "Instant replay off"));
                list.Add(new("PhasmaStrap", App.Settings.Prop.NetworkingProxyEnabled
                    ? "Textures shrunk to 256 px by the Asset Engine"
                    : "(Texture shrinking needs the Asset Engine's proxy - not included)"));
            }

            return list;
        }

        private static PropertyInfo? SettingProperty(string name) => typeof(Models.Persistable.Settings).GetProperty(name);

        public static void Apply(string level)
        {
            App.SendStat("lowEndMode", level);

            if (Active.Length > 0)
                Undo();

            LowEndHardware hardware = DetectHardware();
            var settings = App.Settings.Prop;

            foreach (var (name, value) in SettingsFor(level, hardware))
            {
                PropertyInfo? property = SettingProperty(name);
                if (property is null)
                    continue;

                settings.LowEndBackupSettings[name] = JsonSerializer.Serialize(property.GetValue(settings), property.PropertyType);
                property.SetValue(settings, value);
            }

            foreach (var (name, value) in Flags(level))
            {
                settings.LowEndBackupFlags[name] = App.FastFlags.GetValue(name);
                App.FastFlags.SetValue(name, value);
            }

            settings.LowEndModeLevel = level;
            App.Logger.WriteLine(LOG_IDENT, $"{level} low-end mode set ({settings.LowEndBackupSettings.Count} settings, {settings.LowEndBackupFlags.Count} flags) - kept on Save");
        }

        public static void Undo()
        {
            var settings = App.Settings.Prop;

            foreach (var (name, json) in settings.LowEndBackupSettings)
            {
                PropertyInfo? property = SettingProperty(name);
                if (property is null)
                    continue;

                try
                {
                    property.SetValue(settings, JsonSerializer.Deserialize(json, property.PropertyType));
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Could not put back {name}: {ex.Message}");
                }
            }

            foreach (var (name, value) in settings.LowEndBackupFlags)
                App.FastFlags.SetValue(name, value);

            App.Logger.WriteLine(LOG_IDENT, $"Low-end mode undone ({settings.LowEndBackupSettings.Count} settings, {settings.LowEndBackupFlags.Count} flags put back)");

            settings.LowEndBackupSettings = new();
            settings.LowEndBackupFlags = new();
            settings.LowEndModeLevel = "";
        }

        private static LowEndHardware? _hardware;

        public static LowEndHardware DetectHardware()
        {
            if (_hardware is not null)
                return _hardware;

            string gpuName = "Unknown graphics";
            long gpuMb = 0;
            bool integrated = false;

            try
            {
                if (Vortice.DXGI.DXGI.CreateDXGIFactory1(out Vortice.DXGI.IDXGIFactory1? factory).Success && factory is not null)
                {
                    using (factory)
                    {
                        for (int index = 0; index < 16; index++)
                        {
                            if (factory.EnumAdapters1(index, out Vortice.DXGI.IDXGIAdapter1? adapter).Failure || adapter is null)
                                break;

                            using (adapter)
                            {
                                Vortice.DXGI.AdapterDescription1 description = adapter.Description1;
                                if ((description.Flags & Vortice.DXGI.AdapterFlags.Software) != 0)
                                    continue;

                                long mb = (long)(IntPtr)description.DedicatedVideoMemory / (1024 * 1024);
                                if (mb >= gpuMb)
                                {
                                    gpuMb = mb;
                                    gpuName = description.Description;

                                    integrated = mb < 512;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Graphics detection failed: {ex.Message}");
            }

            long ramMb = 0;
            var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (GlobalMemoryStatusEx(ref status))
                ramMb = (long)(status.ullTotalPhys / (1024 * 1024));

            _hardware = new LowEndHardware(gpuName.Trim(), gpuMb, integrated, ramMb, Environment.ProcessorCount);
            App.Logger.WriteLine(LOG_IDENT, $"Hardware: {_hardware.Describe()} - recommended: {(_hardware.Recommended.Length > 0 ? _hardware.Recommended : "not needed")}");
            return _hardware;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);
    }
}
