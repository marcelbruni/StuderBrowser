using System.IO;

namespace StuderBrowser;

public static class ProfilePaths
{
    public static string Root { get; } = Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "profile")).FullName;

    public static string WebViewUserData => Path.Combine(Root, "UserData");

    public static string Bookmarks => Path.Combine(Root, "bookmarks.json");

    public static string Favicons => Path.Combine(Root, "favicons");
}
