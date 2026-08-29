using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PhasmaStrap.Integrations.GameChat
{
    /// <summary>
    /// A small popup shown when clicking a name in the chat overlay.
    ///
    /// Voidstrap's original GameChatProfileWindow rendered a full social profile (banner, gradient,
    /// avatar border, badges, friend/follow counts, an "add friend" button) sourced from Voidstrap's
    /// own website API. PhasmaStrap has no equivalent backend, so this port only shows what's available
    /// directly from Roblox: headshot, username/user id, a link to the user's Roblox profile, and a
    /// "Report" button that goes through the same optional chat server as the rest of GameChat.
    /// </summary>
    public class GameChatProfileWindow : Window
    {
        private readonly long _robloxId;
        private readonly Func<Task<GameChatBugResult>>? _reporter;
        private bool _closed;

        public GameChatProfileWindow(long robloxId, string fallbackName, Func<Task<GameChatBugResult>>? reporter = null)
        {
            _robloxId = robloxId;
            _reporter = reporter;

            Title = fallbackName;
            Width = 260;
            Height = 180;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            var image = new Image { Width = 48, Height = 48, Margin = new Thickness(0, 0, 0, 8) };

            var nameText = new TextBlock
            {
                Text = fallbackName,
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
            };

            var idText = new TextBlock
            {
                Text = robloxId > 0 ? "User ID: " + robloxId : "",
                FontSize = 11,
                Foreground = Brushes.LightGray,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 10),
            };

            var openProfileButton = new Button { Content = "Open Roblox Profile", Margin = new Thickness(0, 0, 0, 6), Padding = new Thickness(8, 4, 8, 4) };
            openProfileButton.Click += (_, _) => OpenRobloxProfile();

            var reportButton = new Button { Content = "Report", Padding = new Thickness(8, 4, 8, 4) };
            reportButton.Click += async (_, _) => await OnReportClickedAsync();
            reportButton.IsEnabled = _reporter != null;

            var closeButton = new Button
            {
                Content = "✕",
                Width = 22,
                Height = 22,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Padding = new Thickness(0),
            };
            closeButton.Click += (_, _) => Close();

            var stack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            stack.Children.Add(image);
            stack.Children.Add(nameText);
            stack.Children.Add(idText);
            stack.Children.Add(openProfileButton);
            stack.Children.Add(reportButton);

            var grid = new Grid();
            grid.Children.Add(stack);
            grid.Children.Add(closeButton);

            Content = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(235, 24, 24, 27)),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16),
                Child = grid,
            };

            if (robloxId > 0)
                _ = LoadHeadshotAsync(image, robloxId);

            Deactivated += (_, _) => Close();
            Closed += (_, _) => _closed = true;
        }

        private async Task LoadHeadshotAsync(Image image, long robloxId)
        {
            try
            {
                ImageSource? source = await GameChatRoblox.GetHeadshotAsync(robloxId).ConfigureAwait(true);
                if (source != null && !_closed)
                    image.Source = source;
            }
            catch
            {
            }
        }

        private void OpenRobloxProfile()
        {
            if (_robloxId <= 0)
                return;
            try
            {
                Process.Start(new ProcessStartInfo("https://www.roblox.com/users/" + _robloxId + "/profile") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("GameChatProfileWindow", "Failed to open profile: " + ex.Message);
            }
        }

        private async Task OnReportClickedAsync()
        {
            if (_reporter == null)
                return;
            await _reporter().ConfigureAwait(true);
            Close();
        }
    }
}
