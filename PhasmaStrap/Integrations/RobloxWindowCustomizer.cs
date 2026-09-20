using System.Runtime.InteropServices;

using PhasmaStrap.Integrations.Overlays;
using PhasmaStrap.Models.Entities;

namespace PhasmaStrap.Integrations
{
    public static class RobloxWindowCustomizer
    {
        private const string LOG_IDENT = "RobloxWindowCustomizer";

        private const int WM_SETICON = 0x0080;
        private const int WM_GETICON = 0x007F;
        private static readonly IntPtr ICON_SMALL = IntPtr.Zero;
        private static readonly IntPtr ICON_BIG = new(1);
        private const uint SMTO_ABORTIFHUNG = 0x0002;
        private const uint IconMessageTimeoutMs = 1000;
        private const int GameIconMaxBytes = 2 * 1024 * 1024;

        private static readonly object _iconGate = new();

        private static IDisposable? _lease;
        private static ActivityWatcher? _activityWatcher;
        private static IntPtr _hwnd;
        private static IntPtr _originalSmallIcon;
        private static IntPtr _originalBigIcon;
        private static IntPtr _ownedSmallIcon;
        private static IntPtr _ownedBigIcon;
        private static int _started;

        public static bool IsEnabled =>
            !string.IsNullOrWhiteSpace(App.Settings.Prop.RobloxTitle) ||
            App.Settings.Prop.CycleTitleWithGameName ||
            App.Settings.Prop.UseGameIconForRobloxWindow;

        public static void Start(ActivityWatcher? activityWatcher)
        {
            if (!IsEnabled || Interlocked.Exchange(ref _started, 1) != 0)
                return;

            _activityWatcher = activityWatcher;
            if (_activityWatcher != null)
            {
                _activityWatcher.OnGameJoin += OnGameJoin;
                _activityWatcher.OnGameLeave += OnGameLeave;
            }

            _lease = RobloxWindowTracker.Acquire();
            RobloxWindowTracker.Changed += OnWindowChanged;

            RobloxWindowRect current = RobloxWindowTracker.Current;
            if (current.Valid)
                OnWindowChanged(null, current);
        }

        public static void Shutdown()
        {
            if (Interlocked.Exchange(ref _started, 0) == 0)
                return;

            RobloxWindowTracker.Changed -= OnWindowChanged;

            if (_activityWatcher != null)
            {
                _activityWatcher.OnGameJoin -= OnGameJoin;
                _activityWatcher.OnGameLeave -= OnGameLeave;
                _activityWatcher = null;
            }

            try { ResetIcons(); } catch { }

            _lease?.Dispose();
            _lease = null;
            _hwnd = IntPtr.Zero;
        }

        private static string BaseTitle => App.Settings.Prop.RobloxTitle ?? "";

        private static void OnWindowChanged(object? sender, RobloxWindowRect rect)
        {
            if (!rect.Valid || rect.Hwnd == IntPtr.Zero || rect.Hwnd == _hwnd)
                return;

            _hwnd = rect.Hwnd;

            if (BaseTitle.Length > 0)
                SetWindowText(_hwnd, BaseTitle);

            _originalSmallIcon = SendIconMessage(WM_GETICON, ICON_SMALL, IntPtr.Zero);
            _originalBigIcon = SendIconMessage(WM_GETICON, ICON_BIG, IntPtr.Zero);

            if (_activityWatcher is { InGame: true } watcher && watcher.Data.UniverseId > 0)
            {
                long universeId = watcher.Data.UniverseId;

                if (App.Settings.Prop.UseGameIconForRobloxWindow)
                    _ = ApplyGameIconAsync(universeId);

                if (App.Settings.Prop.CycleTitleWithGameName)
                    _ = UpdateTitleWithGameNameAsync(universeId);
            }
        }

        private static void OnGameJoin(object? sender, EventArgs e)
        {
            if (_hwnd == IntPtr.Zero || _activityWatcher == null)
                return;

            long universeId = _activityWatcher.Data.UniverseId;
            if (universeId <= 0)
                return;

            if (App.Settings.Prop.UseGameIconForRobloxWindow)
                _ = ApplyGameIconAsync(universeId);

            if (App.Settings.Prop.CycleTitleWithGameName)
                _ = UpdateTitleWithGameNameAsync(universeId);
        }

        private static void OnGameLeave(object? sender, EventArgs e)
        {
            if (_hwnd == IntPtr.Zero)
                return;

            if (App.Settings.Prop.UseGameIconForRobloxWindow)
                ResetIcons();

            if (App.Settings.Prop.CycleTitleWithGameName)
                SetWindowText(_hwnd, BaseTitle.Length > 0 ? BaseTitle : "Roblox");
        }

        private static async Task UpdateTitleWithGameNameAsync(long universeId)
        {
            try
            {
                UniverseDetails? details = UniverseDetails.LoadFromCache(universeId);
                if (details == null)
                {
                    await UniverseDetails.FetchSingle(universeId).ConfigureAwait(false);
                    details = UniverseDetails.LoadFromCache(universeId);
                }

                string gameName = details?.Data?.Name ?? "";
                if (string.IsNullOrWhiteSpace(gameName) || _hwnd == IntPtr.Zero)
                    return;

                string baseTitle = BaseTitle;
                string newTitle = baseTitle.Length == 0 ? gameName : $"{baseTitle}: {gameName}";

                if (App.Settings.Prop.ShowServerInfoInTitle)
                {
                    long playing = details?.Data?.Playing ?? 0;
                    if (playing > 0)
                        newTitle += $" ({playing:N0} playing)";
                }

                SetWindowText(_hwnd, newTitle);
            }
            catch (Exception ex)
            {
                App.Logger?.WriteLine(LOG_IDENT, "Failed to update the window title: " + ex.Message);
            }
        }

        private static async Task ApplyGameIconAsync(long universeId)
        {
            try
            {
                UniverseDetails? details = UniverseDetails.LoadFromCache(universeId);
                if (string.IsNullOrWhiteSpace(details?.Thumbnail?.ImageUrl))
                {
                    await UniverseDetails.FetchSingle(universeId).ConfigureAwait(false);
                    details = UniverseDetails.LoadFromCache(universeId);
                }

                string? url = details?.Thumbnail?.ImageUrl;
                if (string.IsNullOrWhiteSpace(url) || _hwnd == IntPtr.Zero)
                    return;

                using HttpResponseMessage response = await App.HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    return;

                byte[] data = await Utility.Http.ReadBytesBoundedAsync(response.Content, GameIconMaxBytes).ConfigureAwait(false);
                if (data.Length == 0 || _hwnd == IntPtr.Zero)
                    return;

                IntPtr small;
                IntPtr big;
                using (var stream = new MemoryStream(data))
                using (var bitmap = new System.Drawing.Bitmap(stream))
                {
                    small = HIconFromBitmap(bitmap, 32);
                    big = HIconFromBitmap(bitmap, 64);
                }

                if (small != IntPtr.Zero || big != IntPtr.Zero)
                    ApplyIcons(small, big);
            }
            catch (Exception ex)
            {
                App.Logger?.WriteLine(LOG_IDENT, "Failed to set the game icon: " + ex.Message);
            }
        }

        private static IntPtr HIconFromBitmap(System.Drawing.Bitmap source, int size)
        {
            try
            {
                using var scaled = new System.Drawing.Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (var graphics = System.Drawing.Graphics.FromImage(scaled))
                {
                    graphics.Clear(System.Drawing.Color.Transparent);
                    graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    graphics.DrawImage(source, new System.Drawing.Rectangle(0, 0, size, size));
                }
                return scaled.GetHicon();
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        private static void ApplyIcons(IntPtr small, IntPtr big)
        {
            if (small == IntPtr.Zero) small = big;
            if (big == IntPtr.Zero) big = small;
            if (small == IntPtr.Zero && big == IntPtr.Zero)
                return;

            lock (_iconGate)
            {
                IntPtr oldSmall = _ownedSmallIcon;
                IntPtr oldBig = _ownedBigIcon;

                SendIconMessage(WM_SETICON, ICON_SMALL, small);
                SendIconMessage(WM_SETICON, ICON_BIG, big);

                _ownedSmallIcon = small;
                _ownedBigIcon = big;

                if (oldSmall != IntPtr.Zero && oldSmall != small && oldSmall != big)
                    DestroyIconSafe(oldSmall);
                if (oldBig != IntPtr.Zero && oldBig != small && oldBig != big && oldBig != oldSmall)
                    DestroyIconSafe(oldBig);
            }
        }

        private static void ResetIcons()
        {
            lock (_iconGate)
            {
                IntPtr oldSmall = _ownedSmallIcon;
                IntPtr oldBig = _ownedBigIcon;

                if (_hwnd != IntPtr.Zero)
                {
                    SendIconMessage(WM_SETICON, ICON_SMALL, _originalSmallIcon);
                    SendIconMessage(WM_SETICON, ICON_BIG, _originalBigIcon);
                }

                _ownedSmallIcon = IntPtr.Zero;
                _ownedBigIcon = IntPtr.Zero;

                if (oldSmall != IntPtr.Zero)
                    DestroyIconSafe(oldSmall);
                if (oldBig != IntPtr.Zero && oldBig != oldSmall)
                    DestroyIconSafe(oldBig);
            }
        }

        private static void DestroyIconSafe(IntPtr icon)
        {
            try { DestroyIconW(icon); } catch { }
        }

        private static IntPtr SendIconMessage(int message, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (_hwnd == IntPtr.Zero || !OverlayInterop.IsWindow(_hwnd))
                    return IntPtr.Zero;

                if (SendMessageTimeout(_hwnd, message, wParam, lParam, SMTO_ABORTIFHUNG, IconMessageTimeoutMs, out IntPtr result) == IntPtr.Zero)
                    return IntPtr.Zero;

                return result;
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        private static void SetWindowText(IntPtr hwnd, string text)
        {
            try
            {
                if (hwnd != IntPtr.Zero && OverlayInterop.IsWindow(hwnd))
                    SetWindowTextW(hwnd, text);
            }
            catch
            {
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetWindowTextW(IntPtr hWnd, string lpString);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);

        [DllImport("user32.dll")]
        private static extern bool DestroyIconW(IntPtr hIcon);
    }
}
