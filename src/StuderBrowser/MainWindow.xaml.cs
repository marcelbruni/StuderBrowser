using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace StuderBrowser;

public partial class MainWindow : Window
{
    private const string HomeUrl = "https://www.google.com";
    private const string SearchUrl = "https://www.google.com/search?q=";
    private const string StarGlyph = "";
    private const string FilledStarGlyph = "";
    private const string FolderGlyph = "";
    private const string GlobeGlyph = "";
    private const double DetachDistance = 30;
    private const double DetachedWindowScale = 0.7;
    private const string BookmarkDragFormat = "StuderBrowser.Bookmark";

    private static BookmarkNode? _bookmarkInDrag;

    private readonly ObservableCollection<BrowserTab> _tabs = [];
    private readonly BrowserTab? _adoptedTab;
    private CoreWebView2Environment? _environment;
    private BrowserTab? _activeTab;
    private BrowserTab? _draggedTab;
    private Point _dragStart;
    private MainWindow? _mergeTarget;
    private BookmarkNode? _pressedBookmark;
    private Point _bookmarkDragStart;
    private InsertionMarker? _bookmarkMarker;

    public MainWindow()
        : this(null)
    {
    }

    private MainWindow(BrowserTab? adoptedTab)
    {
        _adoptedTab = adoptedTab;
        InitializeComponent();
        TabStrip.ItemsSource = _tabs;
        RenderBookmarkBar();
        Loaded += OnLoaded;
        Closed += OnClosed;
        SourceInitialized += (_, _) => OnWindowStateChanged(this, EventArgs.Empty);
    }

    public IEnumerable<string> TabUrls => _tabs.Select(tab => tab.Url).Where(url => !string.IsNullOrEmpty(url));

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _environment = await BrowserSession.GetEnvironmentAsync();
        }
        catch (WebView2RuntimeNotFoundException)
        {
            MessageBox.Show(this, "Die Microsoft Edge WebView2 Runtime ist nicht installiert.", "StuderBrowser",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
            return;
        }

        BrowserSession.BookmarksChanged += OnBookmarksChanged;
        BrowserSession.UpdateAvailable += OnUpdateAvailable;
        if (BrowserSession.IsUpdatePending)
        {
            OnUpdateAvailable(null, EventArgs.Empty);
        }

        if (_adoptedTab is not null)
        {
            Attach(_adoptedTab, 0);
            Activate(_adoptedTab);
            DragWindowWithTab();
            return;
        }

        var startUrls = Environment.GetCommandLineArgs().Skip(1).ToList();
        foreach (var url in startUrls.DefaultIfEmpty(HomeUrl))
        {
            await OpenTabAsync(NormalizeInput(url));
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        BrowserSession.BookmarksChanged -= OnBookmarksChanged;
        BrowserSession.UpdateAvailable -= OnUpdateAvailable;
        foreach (var tab in _tabs)
        {
            tab.WebView.Dispose();
        }
    }

    private async Task OpenTabAsync(string url, bool activate = true)
    {
        if (_environment is null)
        {
            return;
        }

        var tab = new BrowserTab();
        Attach(tab, _activeTab is null ? _tabs.Count : _tabs.IndexOf(_activeTab) + 1);

        if (activate || _activeTab is null)
        {
            Activate(tab);
        }

        await tab.InitializeAsync(_environment, url);
    }

    private void Attach(BrowserTab tab, int index)
    {
        tab.PropertyChanged += OnTabPropertyChanged;
        tab.NewWindowRequested += OnTabNewWindowRequested;
        _tabs.Insert(index, tab);
        WebViewHost.Children.Add(tab.WebView);
    }

    private void Detach(BrowserTab tab)
    {
        var index = _tabs.IndexOf(tab);
        tab.PropertyChanged -= OnTabPropertyChanged;
        tab.NewWindowRequested -= OnTabNewWindowRequested;
        tab.IsActive = false;
        _tabs.Remove(tab);
        WebViewHost.Children.Remove(tab.WebView);

        if (_tabs.Count == 0)
        {
            Close();
            return;
        }

        if (tab == _activeTab)
        {
            _activeTab = null;
            Activate(_tabs[Math.Min(index, _tabs.Count - 1)]);
        }
    }

    private void CloseTab(BrowserTab tab)
    {
        Detach(tab);
        tab.WebView.Dispose();
    }

    private void Activate(BrowserTab tab)
    {
        if (_activeTab is not null)
        {
            _activeTab.IsActive = false;
        }

        _activeTab = tab;
        tab.IsActive = true;
        UpdateChrome();

        if (string.IsNullOrEmpty(tab.Url))
        {
            AddressBar.Focus();
        }
        else
        {
            tab.WebView.Focus();
        }
    }

    private void DetachToNewWindow(BrowserTab tab)
    {
        var cursor = PresentationSource.FromVisual(this)!.CompositionTarget.TransformFromDevice
            .Transform(PointToScreen(Mouse.GetPosition(this)));
        var tabPosition = Mouse.GetPosition(TabStrip);
        var width = (WindowState == WindowState.Maximized ? ActualWidth : RestoreBounds.Width) * DetachedWindowScale;
        var height = (WindowState == WindowState.Maximized ? ActualHeight : RestoreBounds.Height) * DetachedWindowScale;

        Detach(tab);
        var window = new MainWindow(tab)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            WindowState = WindowState.Normal,
            Width = width,
            Height = height,
            Left = cursor.X - Math.Clamp(tabPosition.X, 0, width) - 8,
            Top = cursor.Y - 18,
        };
        window.Show();
    }

    private void DragWindowWithTab()
    {
        if (Mouse.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        LocationChanged += OnTabDragMoved;
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // The button was released between detaching and the window becoming ready to move.
        }
        finally
        {
            LocationChanged -= OnTabDragMoved;
        }

        var target = _mergeTarget;
        _mergeTarget = null;
        if (target is not null && _tabs.Count == 1)
        {
            MergeInto(target);
        }
    }

    private void OnTabDragMoved(object? sender, EventArgs e)
    {
        _mergeTarget = FindMergeTarget(NativeMethods.CursorPosition());
        if (_mergeTarget is not null)
        {
            // Losing the capture ends the modal move loop of DragMove, so the tab snaps in while still held.
            NativeMethods.ReleaseCapture();
        }
    }

    private MainWindow? FindMergeTarget(Point cursor)
    {
        var visibleWindowBelow = NativeMethods.WindowsBelowContaining(WindowHandle, cursor).FirstOrDefault();
        var window = Application.Current.Windows.OfType<MainWindow>()
            .FirstOrDefault(candidate => candidate != this && candidate.WindowHandle == visibleWindowBelow);
        return window is not null && window.IsOverTabBar(cursor) ? window : null;
    }

    private void MergeInto(MainWindow target)
    {
        var index = target.TabInsertIndexAt(NativeMethods.CursorPosition());
        var tab = _tabs[0];
        Detach(tab);
        target.Adopt(tab, index);
    }

    private void Adopt(BrowserTab tab, int index)
    {
        Attach(tab, index);
        Activate(tab);
        base.Activate();
        Dispatcher.BeginInvoke(() => ContinueTabDrag(tab), DispatcherPriority.Loaded);
    }

    private void ContinueTabDrag(BrowserTab tab)
    {
        var container = TabStrip.ItemContainerGenerator.ContainerFromItem(tab) as DependencyObject;
        if (Mouse.LeftButton != MouseButtonState.Pressed || container is null
            || VisualTreeHelper.GetChild(container, 0) is not UIElement tabElement)
        {
            return;
        }

        _draggedTab = tab;
        _dragStart = Mouse.GetPosition(TabStrip);
        tabElement.CaptureMouse();
    }

    private bool IsOverTabBar(Point screenPoint)
    {
        var position = TabBar.PointFromScreen(screenPoint);
        return position.X >= 0 && position.Y >= -TabBar.Margin.Top
            && position.X < TabBar.ActualWidth && position.Y < TabBar.ActualHeight;
    }

    private int TabInsertIndexAt(Point screenPoint)
    {
        var x = TabStrip.PointFromScreen(screenPoint).X;
        return _tabs.Select(tab => TabStrip.ItemContainerGenerator.ContainerFromItem(tab))
            .OfType<FrameworkElement>()
            .Select(container => container.TransformToAncestor(TabStrip).TransformBounds(new Rect(container.RenderSize)))
            .Count(bounds => bounds.Left + bounds.Width / 2 < x);
    }

    private void OnTabNewWindowRequested(object? sender, string url) => _ = OpenTabAsync(url);

    private void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BrowserTab.Favicon) && sender is BrowserTab tab)
        {
            RememberFaviconIfBookmarked(tab);
        }

        if (sender == _activeTab)
        {
            UpdateChrome();
        }
    }

    private void OnBookmarksChanged(object? sender, EventArgs e)
    {
        RenderBookmarkBar();
        UpdateChrome();
    }

    private void OnUpdateAvailable(object? sender, EventArgs e)
    {
        UpdateButton.Visibility = Visibility.Visible;
        UpdateButton.ToolTip = $"Chromium {BrowserSession.PendingVersion} ist installiert (aktuell {_environment?.BrowserVersionString}).\n"
            + "Klicken, um StuderBrowser neu zu starten. Alle Tabs werden wieder geöffnet.";
    }

    private void UpdateChrome()
    {
        if (_activeTab is null)
        {
            return;
        }

        Title = $"{_activeTab.Title} – StuderBrowser";
        BackButton.IsEnabled = _activeTab.CanGoBack;
        ForwardButton.IsEnabled = _activeTab.CanGoForward;
        ReloadButton.Content = _activeTab.IsLoading ? "" : "";

        if (!AddressBar.IsKeyboardFocused)
        {
            AddressBar.Text = _activeTab.Url;
        }

        var isBookmarked = BrowserSession.Bookmarks.FindByUrl(_activeTab.Url) is not null;
        BookmarkToggleButton.Content = isBookmarked ? FilledStarGlyph : StarGlyph;
        BookmarkToggleButton.Foreground = isBookmarked ? Brushes.DodgerBlue : (Brush)FindResource("Foreground");
    }

    private void OpenInActiveTab(string url)
    {
        if (url.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
        {
            _ = _activeTab?.RunBookmarkletAsync(url);
            return;
        }

        _activeTab?.Navigate(url);
        _activeTab?.WebView.Focus();
    }

    private static string NormalizeInput(string input)
    {
        var text = input.Trim();
        if (text.Contains("://") || text.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
        {
            return text;
        }

        if (text.StartsWith("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return "http://" + text;
        }

        var looksLikeHost = !text.Contains(' ') && text.Contains('.');
        return looksLikeHost ? "https://" + text : SearchUrl + Uri.EscapeDataString(text);
    }

    private void ToggleBookmarkForActiveTab()
    {
        if (_activeTab is null || string.IsNullOrEmpty(_activeTab.Url))
        {
            return;
        }

        var bookmarks = BrowserSession.Bookmarks;
        if (bookmarks.FindByUrl(_activeTab.Url) is { } existing)
        {
            bookmarks.Remove(existing);
        }
        else
        {
            bookmarks.Add(_activeTab.Title, _activeTab.Url);
            RememberFaviconIfBookmarked(_activeTab);
        }

        BrowserSession.SaveBookmarks();
    }

    private static void RememberFaviconIfBookmarked(BrowserTab tab)
    {
        var host = FaviconCache.HostOf(tab.Url);
        var isBookmarkedHost = host is not null && BrowserSession.Bookmarks.All.Any(node => FaviconCache.HostOf(node.Url) == host);
        if (isBookmarkedHost && tab.FaviconPng is { } png && BrowserSession.Favicons.Save(tab.Url, png))
        {
            BrowserSession.NotifyFaviconsChanged();
        }
    }

    private static void RemoveBookmark(BookmarkNode node)
    {
        BrowserSession.Bookmarks.Remove(node);
        BrowserSession.SaveBookmarks();
    }

    private void RenderBookmarkBar()
    {
        BookmarkBar.Children.Clear();
        foreach (var node in BrowserSession.Bookmarks.Bar)
        {
            BookmarkBar.Children.Add(node.IsFolder ? CreateFolderButton(node) : CreateBookmarkButton(node));
        }
    }

    private Button CreateBookmarkButton(BookmarkNode node)
    {
        var button = new Button
        {
            Style = (Style)FindResource("BookmarkButton"),
            Content = CreateHeader(CreateBookmarkIcon(node), node.Title),
            ToolTip = node.Url,
            ContextMenu = CreateBookmarkContextMenu(node),
        };
        EnableReordering(button, node);
        button.Click += (_, _) => OpenInActiveTab(node.Url!);
        button.MouseUp += async (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Middle)
            {
                await OpenTabAsync(node.Url!, activate: false);
            }
        };
        return button;
    }

    private Button CreateFolderButton(BookmarkNode folder)
    {
        var menu = new ContextMenu();
        foreach (var child in folder.Children!)
        {
            menu.Items.Add(CreateMenuItem(child));
        }

        var button = new Button
        {
            Style = (Style)FindResource("BookmarkButton"),
            Content = CreateHeader(CreateGlyph(FolderGlyph), folder.Title),
            ContextMenu = menu,
        };
        EnableReordering(button, folder);
        button.Click += (_, _) =>
        {
            menu.PlacementTarget = button;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        };
        return button;
    }

    private void EnableReordering(Button button, BookmarkNode node)
    {
        button.PreviewMouseLeftButtonDown += (_, e) =>
        {
            _pressedBookmark = node;
            _bookmarkDragStart = e.GetPosition(BookmarkBar);
        };
        button.PreviewMouseLeftButtonUp += (_, _) => _pressedBookmark = null;
        button.PreviewMouseMove += (_, e) =>
        {
            if (_pressedBookmark != node || e.LeftButton != MouseButtonState.Pressed
                || !IsDragGesture(_bookmarkDragStart, e.GetPosition(BookmarkBar)))
            {
                return;
            }

            _pressedBookmark = null;
            button.ReleaseMouseCapture();
            _bookmarkInDrag = node;
            DragDrop.DoDragDrop(button, new DataObject(BookmarkDragFormat, node.Title), DragDropEffects.Move);
            _bookmarkInDrag = null;
        };
    }

    private void OnBookmarkBarDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (_bookmarkInDrag is null || !e.Data.GetDataPresent(BookmarkDragFormat))
        {
            e.Effects = DragDropEffects.None;
            return;
        }

        e.Effects = DragDropEffects.Move;
        var slot = FindBookmarkSlot(e.GetPosition(BookmarkBar));
        _bookmarkMarker = InsertionMarker.Show(_bookmarkMarker, BookmarkBar, slot.MarkerTop, slot.MarkerHeight);
    }

    private void OnBookmarkBarDragLeave(object sender, DragEventArgs e) => InsertionMarker.Hide(ref _bookmarkMarker);

    private void OnBookmarkBarDrop(object sender, DragEventArgs e)
    {
        InsertionMarker.Hide(ref _bookmarkMarker);
        if (_bookmarkInDrag is not { } node)
        {
            return;
        }

        BrowserSession.Bookmarks.MoveOnBar(node, FindBookmarkSlot(e.GetPosition(BookmarkBar)).Index);
        BrowserSession.SaveBookmarks();
    }

    private DropSlot FindBookmarkSlot(Point position)
    {
        var items = BookmarkBar.Children.Cast<FrameworkElement>()
            .Select((element, index) =>
                (Index: index, Bounds: element.TransformToAncestor(BookmarkBar).TransformBounds(new Rect(element.RenderSize))))
            .ToList();
        if (items.Count == 0)
        {
            return new DropSlot(0, new Point(0, 0), BookmarkBar.ActualHeight);
        }

        var rowTop = items.MinBy(item => Math.Abs(item.Bounds.Top + item.Bounds.Height / 2 - position.Y)).Bounds.Top;
        var row = items.Where(item => Math.Abs(item.Bounds.Top - rowTop) < 1).ToList();
        foreach (var item in row)
        {
            if (position.X < item.Bounds.Left + item.Bounds.Width / 2)
            {
                return new DropSlot(item.Index, item.Bounds.TopLeft, item.Bounds.Height);
            }
        }

        var last = row[^1];
        return new DropSlot(last.Index + 1, last.Bounds.TopRight, last.Bounds.Height);
    }

    private static bool IsDragGesture(Point start, Point current) =>
        Math.Abs(current.X - start.X) > SystemParameters.MinimumHorizontalDragDistance
        || Math.Abs(current.Y - start.Y) > SystemParameters.MinimumVerticalDragDistance;

    private MenuItem CreateMenuItem(BookmarkNode node)
    {
        if (node.IsFolder)
        {
            var folderItem = new MenuItem { Header = node.Title, Icon = CreateGlyph(FolderGlyph) };
            foreach (var child in node.Children!)
            {
                folderItem.Items.Add(CreateMenuItem(child));
            }

            return folderItem;
        }

        var item = new MenuItem
        {
            Header = node.Title,
            Icon = CreateBookmarkIcon(node),
            ToolTip = node.Url,
            ContextMenu = CreateBookmarkContextMenu(node),
        };
        item.Click += (_, _) => OpenInActiveTab(node.Url!);
        return item;
    }

    private ContextMenu CreateBookmarkContextMenu(BookmarkNode node)
    {
        var openInNewTab = new MenuItem { Header = "In neuem Tab öffnen" };
        openInNewTab.Click += async (_, _) => await OpenTabAsync(node.Url!);
        var remove = new MenuItem { Header = "Entfernen" };
        remove.Click += (_, _) => RemoveBookmark(node);
        return new ContextMenu { Items = { openInNewTab, remove } };
    }

    private static FrameworkElement CreateBookmarkIcon(BookmarkNode node) =>
        BrowserSession.Favicons.Get(node.Url) is { } favicon
            ? new Image { Source = favicon, Width = 16, Height = 16 }
            : CreateGlyph(GlobeGlyph);

    private static TextBlock CreateGlyph(string glyph) => new()
    {
        Text = glyph,
        FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
    };

    private static StackPanel CreateHeader(FrameworkElement icon, string title)
    {
        icon.Margin = new Thickness(0, 0, 6, 0);
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { icon, new TextBlock { Text = title, TextTrimming = TextTrimming.CharacterEllipsis } },
        };
    }

    private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        var alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        switch (key)
        {
            case Key.T when ctrl:
                e.Handled = true;
                await OpenTabAsync(HomeUrl);
                break;
            case Key.W when ctrl && _activeTab is not null:
                e.Handled = true;
                CloseTab(_activeTab);
                break;
            case Key.L when ctrl:
                e.Handled = true;
                AddressBar.Focus();
                break;
            case Key.D when ctrl:
                e.Handled = true;
                ToggleBookmarkForActiveTab();
                break;
            case Key.Tab when ctrl && _activeTab is not null:
                e.Handled = true;
                var offset = shift ? _tabs.Count - 1 : 1;
                Activate(_tabs[(_tabs.IndexOf(_activeTab) + offset) % _tabs.Count]);
                break;
            case Key.Left when alt:
                e.Handled = true;
                _activeTab?.WebView.GoBack();
                break;
            case Key.Right when alt:
                e.Handled = true;
                _activeTab?.WebView.GoForward();
                break;
            case Key.F5:
                e.Handled = true;
                _activeTab?.WebView.Reload();
                break;
        }
    }

    private void OnAddressBarKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(AddressBar.Text))
        {
            OpenInActiveTab(NormalizeInput(AddressBar.Text));
        }
        else if (e.Key == Key.Escape && _activeTab is not null)
        {
            AddressBar.Text = _activeTab.Url;
            _activeTab.WebView.Focus();
        }
    }

    private void OnAddressBarFocused(object sender, KeyboardFocusChangedEventArgs e) => AddressBar.SelectAll();

    private void OnAddressBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!AddressBar.IsKeyboardFocusWithin)
        {
            e.Handled = true;
            AddressBar.Focus();
        }
    }

    private void OnTabMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var tab = TabOf(sender);
        Activate(tab);
        _draggedTab = tab;
        _dragStart = e.GetPosition(TabStrip);
        ((UIElement)sender).CaptureMouse();
    }

    private void OnTabMouseMove(object sender, MouseEventArgs e)
    {
        if (_draggedTab is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var position = e.GetPosition(TabStrip);
        var tab = _draggedTab;

        if (_tabs.Count == 1)
        {
            if (IsDragGesture(_dragStart, position))
            {
                EndTabDrag(sender);
                DragWindowWithTab();
            }

            return;
        }

        var isOutsideStrip = position.Y < -DetachDistance || position.Y > TabStrip.ActualHeight + DetachDistance;
        if (isOutsideStrip)
        {
            EndTabDrag(sender);
            DetachToNewWindow(tab);
        }
    }

    private void OnTabMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            EndTabDrag(sender);
        }
        else if (e.ChangedButton == MouseButton.Middle)
        {
            CloseTab(TabOf(sender));
        }
    }

    private void EndTabDrag(object sender)
    {
        _draggedTab = null;
        ((UIElement)sender).ReleaseMouseCapture();
    }

    private void OnCloseTabClicked(object sender, RoutedEventArgs e) => CloseTab(TabOf(sender));

    private async void OnNewTabClicked(object sender, RoutedEventArgs e) => await OpenTabAsync(HomeUrl);

    private void OnBackClicked(object sender, RoutedEventArgs e) => _activeTab?.WebView.GoBack();

    private void OnForwardClicked(object sender, RoutedEventArgs e) => _activeTab?.WebView.GoForward();

    private void OnReloadClicked(object sender, RoutedEventArgs e)
    {
        if (_activeTab?.IsLoading == true)
        {
            _activeTab.WebView.Stop();
        }
        else
        {
            _activeTab?.WebView.Reload();
        }
    }

    private void OnHomeClicked(object sender, RoutedEventArgs e) => OpenInActiveTab(HomeUrl);

    private void OnToggleBookmarkClicked(object sender, RoutedEventArgs e) => ToggleBookmarkForActiveTab();

    private async void OnUpdateClicked(object sender, RoutedEventArgs e)
    {
        var urls = Application.Current.Windows.OfType<MainWindow>().SelectMany(window => window.TabUrls).ToList();
        await BrowserSession.RestartAsync(urls);
    }

    private void OnMinimizeClicked(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeClicked(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnCloseWindowClicked(object sender, RoutedEventArgs e) => Close();

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        var isMaximized = WindowState == WindowState.Maximized;
        // With WindowChrome a maximized window extends past the screen edges by the resize border.
        RootGrid.Margin = isMaximized ? SystemParameters.WindowResizeBorderThickness : new Thickness(0);
        MaximizeButton.Content = isMaximized ? "" : "";
        MaximizeButton.ToolTip = isMaximized ? "Wiederherstellen" : "Maximieren";
    }

    private static BrowserTab TabOf(object sender) => (BrowserTab)((FrameworkElement)sender).DataContext;

    private IntPtr WindowHandle => new WindowInteropHelper(this).Handle;

    private readonly record struct DropSlot(int Index, Point MarkerTop, double MarkerHeight);
}
