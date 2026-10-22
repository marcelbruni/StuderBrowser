using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace StuderBrowser;

public sealed class FaviconCache(string directory)
{
    private readonly Dictionary<string, ImageSource?> _loaded = new(StringComparer.OrdinalIgnoreCase);

    public static string? HostOf(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host) ? uri.Host.ToLowerInvariant() : null;

    public ImageSource? Get(string? url)
    {
        if (HostOf(url) is not { } host)
        {
            return null;
        }

        if (!_loaded.TryGetValue(host, out var image))
        {
            var path = PathFor(host);
            image = File.Exists(path) ? Decode(File.ReadAllBytes(path)) : null;
            _loaded[host] = image;
        }

        return image;
    }

    public bool Save(string url, byte[] png)
    {
        if (HostOf(url) is not { } host)
        {
            return false;
        }

        var path = PathFor(host);
        if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(png))
        {
            return false;
        }

        Directory.CreateDirectory(directory);
        File.WriteAllBytes(path, png);
        _loaded[host] = Decode(png);
        return true;
    }

    private string PathFor(string host) => Path.Combine(directory, host + ".png");

    private static ImageSource? Decode(byte[] png)
    {
        try
        {
            return BitmapFrame.Create(new MemoryStream(png), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        }
        catch (Exception exception) when (exception is NotSupportedException or FileFormatException)
        {
            return null;
        }
    }
}
