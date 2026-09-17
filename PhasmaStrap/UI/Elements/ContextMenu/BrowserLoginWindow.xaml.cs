using System.Windows;

using Microsoft.Web.WebView2.Core;

using PhasmaStrap.UI.Elements.Base;

namespace PhasmaStrap.UI.Elements.ContextMenu
{
    /// <summary>
    /// A real, isolated embedded browser (WebView2 - the same engine Roblox's own client already
    /// depends on and normally installs, see Bootstrapper's WebView2RuntimeInstaller handling) for
    /// signing into Roblox the normal way instead of hunting down a raw cookie value manually. Once
    /// login succeeds, the .ROBLOSECURITY cookie is read straight out of THIS window's own isolated
    /// browser profile via WebView2's CookieManager API - nothing is read from the user's actual
    /// browser, which has no sanctioned way to hand its cookies to another app anyway.
    /// </summary>
    public partial class BrowserLoginWindow : WpfUiWindow
    {
        private const string LOG_IDENT = "BrowserLoginWindow";

        private readonly TaskCompletionSource<string?> _tcs = new();
        private bool _completed;

        public BrowserLoginWindow()
        {
            InitializeComponent();
            Loaded += BrowserLoginWindow_Loaded;
        }

        /// <summary>Shows the window and waits for a successful login, returning the .ROBLOSECURITY
        /// cookie value, or null if the window was closed/failed before that happened.</summary>
        public static async Task<string?> ShowAndWaitForCookieAsync(Window? owner)
        {
            var window = new BrowserLoginWindow();

            if (owner is not null)
                window.Owner = owner;

            window.Show();
            string? cookie = await window._tcs.Task;

            try
            {
                window.Close();
            }
            catch
            {
                // already closing/closed - fine
            }

            return cookie;
        }

        private async void BrowserLoginWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // a real, persistent profile folder (not the default next-to-the-exe location) so a
                // session survives between uses, same as any normal browser would
                string userDataFolder = Path.Combine(Paths.Base, "BrowserLogin");
                Directory.CreateDirectory(userDataFolder);

                CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
                await Browser.EnsureCoreWebView2Async(env);

                Browser.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;
                Browser.Source = new Uri("https://www.roblox.com/login");

                LoadingPanel.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Failed to initialize WebView2: {ex.Message}");
                Frontend.ShowMessageBox(
                    "Could not open the embedded browser. This needs the Microsoft Edge WebView2 Runtime, which Roblox itself normally installs already - try launching Roblox at least once first, or install the runtime from Microsoft's website.",
                    MessageBoxImage.Error);
                Complete(null);
            }
        }

        private async void CoreWebView2_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (_completed || Browser.CoreWebView2 is null)
                return;

            try
            {
                List<CoreWebView2Cookie> cookies = await Browser.CoreWebView2.CookieManager.GetCookiesAsync("https://www.roblox.com");
                CoreWebView2Cookie? cookie = cookies.FirstOrDefault(c => c.Name == ".ROBLOSECURITY");

                if (cookie is not null && !string.IsNullOrEmpty(cookie.Value))
                    Complete(cookie.Value);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Cookie check failed: {ex.Message}");
            }
        }

        private void Complete(string? cookie)
        {
            if (_completed)
                return;

            _completed = true;
            _tcs.TrySetResult(cookie);
        }

        protected override void OnClosed(EventArgs e)
        {
            Complete(null);
            base.OnClosed(e);
        }
    }
}
