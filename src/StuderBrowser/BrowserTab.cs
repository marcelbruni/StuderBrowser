using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace StuderBrowser;

public sealed class BrowserTab : INotifyPropertyChanged
{
    private string _title = "Neuer Tab";
    private string _url = string.Empty;
    private bool _isLoading;
    private bool _isActive;
    private ImageSource? _favicon;

    public BrowserTab()
    {
        WebView = new WebView2 { Visibility = Visibility.Collapsed };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler<string>? NewWindowRequested;

    public WebView2 WebView { get; }

    public string Title { get => _title; private set => Set(ref _title, value); }

    public string Url { get => _url; private set => Set(ref _url, value); }

    public bool IsLoading { get => _isLoading; private set => Set(ref _isLoading, value); }

    public ImageSource? Favicon { get => _favicon; private set => Set(ref _favicon, value); }

    public byte[]? FaviconPng { get; private set; }

    public bool IsActive
    {
        get => _isActive;
        set
        {
            Set(ref _isActive, value);
            WebView.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    public bool CanGoBack => WebView.CoreWebView2?.CanGoBack == true;

    public bool CanGoForward => WebView.CoreWebView2?.CanGoForward == true;

    public async Task InitializeAsync(CoreWebView2Environment environment, string url)
    {
        await WebView.EnsureCoreWebView2Async(environment);
        var core = WebView.CoreWebView2;

        core.DocumentTitleChanged += (_, _) => Title = string.IsNullOrWhiteSpace(core.DocumentTitle) ? core.Source : core.DocumentTitle;
        core.SourceChanged += (_, _) => Url = core.Source;
        core.NavigationStarting += (_, _) => IsLoading = true;
        core.NavigationCompleted += (_, _) =>
        {
            IsLoading = false;
            OnPropertyChanged(nameof(CanGoBack));
            OnPropertyChanged(nameof(CanGoForward));
        };
        core.HistoryChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(CanGoBack));
            OnPropertyChanged(nameof(CanGoForward));
        };
        core.FaviconChanged += async (_, _) =>
        {
            FaviconPng = await LoadFaviconPngAsync(core);
            Favicon = FaviconPng is null ? null : BitmapFrame.Create(new MemoryStream(FaviconPng), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        };
        core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            NewWindowRequested?.Invoke(this, e.Uri);
        };

        core.Navigate(url);
    }

    public void Navigate(string url) => WebView.CoreWebView2?.Navigate(url);

    public Task RunBookmarkletAsync(string javascriptUrl) =>
        WebView.CoreWebView2?.ExecuteScriptAsync(Uri.UnescapeDataString(javascriptUrl["javascript:".Length..])) ?? Task.CompletedTask;

    private static async Task<byte[]?> LoadFaviconPngAsync(CoreWebView2 core)
    {
        try
        {
            await using var stream = await core.GetFaviconAsync(CoreWebView2FaviconImageFormat.Png);
            if (stream is null || stream.Length == 0)
            {
                return null;
            }

            using var copy = new MemoryStream();
            await stream.CopyToAsync(copy);
            return copy.ToArray();
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException or COMException)
        {
            return null;
        }
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged(string? propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
