using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

using PhasmaStrap.AppData;

namespace PhasmaStrap.Integrations
{
    internal static class SystemPerformanceBoost
    {
        private const string LOG_IDENT = "SystemPerformanceBoost";

        [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
        private static extern uint TimeBeginPeriod(uint uMilliseconds);

        [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
        private static extern uint TimeEndPeriod(uint uMilliseconds);

        [DllImport("powrprof.dll")]
        private static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

        [DllImport("powrprof.dll")]
        private static extern uint PowerSetActiveScheme(IntPtr userRootPowerKey, ref Guid schemeGuid);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr hMem);

        private static readonly Guid HighPerformanceScheme = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");

        private static bool _timerResolutionActive;

        private static Guid? _previousPowerScheme;

        public static void ApplyGpuPreference()
        {
            try
            {
                string exePath = new RobloxPlayerData().ExecutablePath;
                using RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\DirectX\UserGpuPreference");

                if (App.Settings.Prop.ForceHighPerformanceGpu)
                {
                    key.SetValue(exePath, "GpuPreference=2;", RegistryValueKind.String);
                    App.Logger.WriteLine(LOG_IDENT, "Roblox pinned to the high-performance GPU");
                }
                else
                {
                    key.DeleteValue(exePath, throwOnMissingValue: false);
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
            }
        }

        public static void ApplyGameDvr()
        {
            bool disable = App.Settings.Prop.DisableGameDVR;
            try
            {
                using RegistryKey gameConfigStore = Registry.CurrentUser.CreateSubKey(@"System\GameConfigStore");
                gameConfigStore.SetValue("GameDVR_Enabled", disable ? 0 : 1, RegistryValueKind.DWord);

                using RegistryKey gameDvr = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\GameDVR");
                gameDvr.SetValue("AppCaptureEnabled", disable ? 0 : 1, RegistryValueKind.DWord);

                App.Logger.WriteLine(LOG_IDENT, disable ? "Game DVR background capture disabled" : "Game DVR background capture restored to Windows default");
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
            }
        }

        public static void OnGameJoin()
        {
            if (App.Settings.Prop.BoostTimerResolution)
            {
                try
                {
                    _timerResolutionActive = TimeBeginPeriod(1) == 0;
                }
                catch (Exception ex)
                {
                    App.Logger.WriteException(LOG_IDENT, ex);
                }
            }

            if (App.Settings.Prop.UseHighPerformancePowerPlan)
            {
                try
                {
                    if (PowerGetActiveScheme(IntPtr.Zero, out IntPtr activeGuidPtr) == 0 && activeGuidPtr != IntPtr.Zero)
                    {
                        _previousPowerScheme = Marshal.PtrToStructure<Guid>(activeGuidPtr);
                        LocalFree(activeGuidPtr);
                    }

                    Guid target = HighPerformanceScheme;
                    if (PowerSetActiveScheme(IntPtr.Zero, ref target) == 0)
                        App.Logger.WriteLine(LOG_IDENT, "Switched to the High performance power plan for this session");
                }
                catch (Exception ex)
                {
                    App.Logger.WriteException(LOG_IDENT, ex);
                }
            }
        }

        public static void OnGameLeave()
        {
            if (_timerResolutionActive)
            {
                try
                {
                    TimeEndPeriod(1);
                }
                catch (Exception ex)
                {
                    App.Logger.WriteException(LOG_IDENT, ex);
                }
                _timerResolutionActive = false;
            }

            if (_previousPowerScheme.HasValue)
            {
                try
                {
                    Guid previous = _previousPowerScheme.Value;
                    PowerSetActiveScheme(IntPtr.Zero, ref previous);
                }
                catch (Exception ex)
                {
                    App.Logger.WriteException(LOG_IDENT, ex);
                }
                _previousPowerScheme = null;
            }
        }
    }
}
