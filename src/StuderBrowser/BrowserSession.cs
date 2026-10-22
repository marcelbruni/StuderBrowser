using System.Diagnostics;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace StuderBrowser;

public static class BrowserSession
{
    private static readonly TimeSpan BrowserProcessExitTimeout = TimeSpan.FromSeconds(10);
    private static Task<CoreWebView2Environment>? _environment;

    public static event EventHandler? BookmarksChanged;

    public static event EventHandler? UpdateAvailable;

    public static BookmarkStore Bookmarks { get; } = BookmarkStore.Load(ProfilePaths.Bookmarks);

    public static FaviconCache Favicons { get; } = new(ProfilePaths.Favicons);

    public static bool IsUpdatePending { get; private set; }

    public static string? PendingVersion { get; private set; }

    public static Task<CoreWebView2Environment> GetEnvironmentAsync() => _environment ??= CreateEnvironmentAsync();

    public static void SaveBookmarks()
    {
        Bookmarks.Save();
        BookmarksChanged?.Invoke(null, EventArgs.Empty);
    }

    public static void NotifyFaviconsChanged() => BookmarksChanged?.Invoke(null, EventArgs.Empty);

    public static async Task RestartAsync(IReadOnlyList<string> urls)
    {
        var environment = await GetEnvironmentAsync();
        var browserProcessExited = new TaskCompletionSource();
        environment.BrowserProcessExited += (_, _) => browserProcessExited.TrySetResult();

        Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        foreach (var window in Application.Current.Windows.OfType<Window>().ToList())
        {
            window.Close();
        }

        // The new runtime is only picked up once every browser process of the old one is gone.
        await Task.WhenAny(browserProcessExited.Task, Task.Delay(BrowserProcessExitTimeout));

        Process.Start(new ProcessStartInfo(Environment.ProcessPath!, urls) { UseShellExecute = false });
        Application.Current.Shutdown();
    }

    private static async Task<CoreWebView2Environment> CreateEnvironmentAsync()
    {
        var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: ProfilePaths.WebViewUserData);
        environment.NewBrowserVersionAvailable += (_, _) =>
        {
            IsUpdatePending = true;
            PendingVersion = CoreWebView2Environment.GetAvailableBrowserVersionString();
            UpdateAvailable?.Invoke(null, EventArgs.Empty);
        };
        return environment;
    }
}
