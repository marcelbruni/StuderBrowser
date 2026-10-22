using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StuderBrowser;

public sealed class BookmarkNode
{
    public required string Title { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Url { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<BookmarkNode>? Children { get; set; }

    [JsonIgnore]
    public bool IsFolder => Children is not null;
}

public sealed class BookmarkStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string _path;

    private BookmarkStore(string path, List<BookmarkNode> bar)
    {
        _path = path;
        Bar = bar;
    }

    public List<BookmarkNode> Bar { get; }

    public static BookmarkStore Load(string path)
    {
        var bar = File.Exists(path)
            ? JsonSerializer.Deserialize<List<BookmarkNode>>(File.ReadAllText(path), SerializerOptions) ?? []
            : [];
        return new BookmarkStore(path, bar);
    }

    public IEnumerable<BookmarkNode> All => Flatten(Bar);

    public BookmarkNode? FindByUrl(string url) => Flatten(Bar).FirstOrDefault(node => node.Url == url);

    public void Add(string title, string url) => Bar.Add(new BookmarkNode { Title = title, Url = url });

    public void MoveOnBar(BookmarkNode node, int targetIndex)
    {
        var index = Bar.IndexOf(node);
        if (index < 0)
        {
            return;
        }

        Bar.RemoveAt(index);
        Bar.Insert(index < targetIndex ? targetIndex - 1 : targetIndex, node);
    }

    public void Remove(BookmarkNode node)
    {
        if (Bar.Remove(node))
        {
            return;
        }

        foreach (var folder in Flatten(Bar).Where(candidate => candidate.IsFolder))
        {
            if (folder.Children!.Remove(node))
            {
                return;
            }
        }
    }

    public void Save() => File.WriteAllText(_path, JsonSerializer.Serialize(Bar, SerializerOptions));

    private static IEnumerable<BookmarkNode> Flatten(IEnumerable<BookmarkNode> nodes) =>
        nodes.SelectMany(node => node.Children is null ? [node] : Flatten(node.Children).Prepend(node));
}
