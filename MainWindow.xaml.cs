using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using SQLite;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TraceWeb.Models;

namespace TraceWeb;

public partial class MainWindow : Window
{
    private const int MaxHistoryPerDomain = 100;
    private static readonly HashSet<string> MultiPartPublicSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "com.cn", "net.cn", "org.cn", "gov.cn", "edu.cn",
        "co.uk", "org.uk", "gov.uk", "ac.uk",
        "com.au", "net.au", "org.au",
        "co.jp", "or.jp", "ne.jp",
        "co.kr", "or.kr",
        "com.hk", "com.sg", "com.tw"
    };
    private readonly SQLiteConnection _db;
    private readonly string _dbPath = Path.Combine(Environment.CurrentDirectory, "traceweb_archive.db");
    private readonly Dictionary<WebView2, TabItem> _tabsByWebView = new();
    private readonly Dictionary<TabItem, TextBlock> _tabHeaders = new();
    private readonly Dictionary<WebView2, Task> _webViewInitTasks = new();
    private readonly Task<CoreWebView2Environment> _webViewEnvironmentTask;
    private string _currentActiveDomain = string.Empty;
    private bool _ignoreHistorySelection;
    private bool _isHistoryCollapsed;

    public MainWindow()
    {
        InitializeComponent();
        _webViewEnvironmentTask = CoreWebView2Environment.CreateAsync();
        _db = new SQLiteConnection(_dbPath);
        InitDatabase();
        LoadHomeView();
        EnsureActiveWebView();
    }

    private void InitDatabase()
    {
        _db.CreateTable<HistoryRecord>();
        NormalizeStoredDomains();
        PruneAllDomainHistories();
    }

    private string ExtractRootDomain(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return "unknown";
        }

        var host = uri.Host.ToLowerInvariant();
        if (IPAddress.TryParse(host, out _))
        {
            return host;
        }

        var labels = host.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (labels.Length <= 2)
        {
            return host;
        }

        var lastTwo = $"{labels[^2]}.{labels[^1]}";
        if (MultiPartPublicSuffixes.Contains(lastTwo) && labels.Length >= 3)
        {
            return $"{labels[^3]}.{lastTwo}";
        }

        return lastTwo;
    }

    private void LoadHomeView()
    {
        var query = @"
            SELECT
                h1.RootDomain AS RootDomain,
                MAX(h1.VisitTime) AS LastVisit,
                COUNT(h1.Id) AS VisitCount,
                (
                    SELECT h2.Url
                    FROM HistoryRecord h2
                    WHERE h2.RootDomain = h1.RootDomain
                    ORDER BY h2.VisitTime DESC, h2.Id DESC
                    LIMIT 1
                ) AS LastUrl
            FROM HistoryRecord h1
            GROUP BY h1.RootDomain
            ORDER BY LastVisit DESC;";

        var groups = _db.Query<DomainGroup>(query);
        DomainListView.ItemsSource = groups;

        HomeView.Visibility = Visibility.Visible;
        BrowserView.Visibility = Visibility.Hidden;
    }

    private void DomainListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DomainListView.SelectedItem is not DomainGroup selectedGroup)
        {
            return;
        }

        DomainListView.SelectedItem = null;
        OpenInBrowser(selectedGroup.LastUrl, selectedGroup.RootDomain);
    }

    private void BackToHome_Click(object sender, RoutedEventArgs e)
    {
        _currentActiveDomain = string.Empty;
        LoadHomeView();
    }

    private void LoadDomainHistory()
    {
        if (string.IsNullOrWhiteSpace(_currentActiveDomain))
        {
            HistoryListBox.ItemsSource = null;
            return;
        }

        var history = _db.Table<HistoryRecord>()
            .Where(x => x.RootDomain == _currentActiveDomain)
            .OrderByDescending(x => x.VisitTime)
            .ThenByDescending(x => x.Id)
            .Take(MaxHistoryPerDomain)
            .ToList();

        _ignoreHistorySelection = true;
        HistoryListBox.ItemsSource = history;
        HistoryListBox.SelectedItem = null;
        _ignoreHistorySelection = false;
    }

    private void MyWebView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess || BrowserView.Visibility != Visibility.Visible || sender is not WebView2 webView || webView.Source is null)
        {
            return;
        }

        var currentUrl = webView.Source.ToString();
        var currentTitle = webView.CoreWebView2?.DocumentTitle;
        var currentDomain = ExtractRootDomain(currentUrl);
        var isActiveTab = ReferenceEquals(GetCurrentWebView(), webView);

        if (_tabsByWebView.TryGetValue(webView, out var ownerTab))
        {
            SetTabHeaderText(ownerTab, currentDomain);
        }

        if (isActiveTab)
        {
            UrlTextBox.Text = currentUrl;
        }

        if (isActiveTab && !string.Equals(currentDomain, _currentActiveDomain, StringComparison.OrdinalIgnoreCase))
        {
            _currentActiveDomain = currentDomain;
            CurrentDomainText.Text = $"正在浏览: {_currentActiveDomain}";
            LoadDomainHistory();
        }

        var lastRecord = _db.Table<HistoryRecord>()
            .Where(x => x.RootDomain == currentDomain)
            .OrderByDescending(x => x.VisitTime)
            .ThenByDescending(x => x.Id)
            .FirstOrDefault();

        if (lastRecord is not null && string.Equals(lastRecord.Url, currentUrl, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _db.Insert(new HistoryRecord
        {
            RootDomain = currentDomain,
            Url = currentUrl,
            Title = string.IsNullOrWhiteSpace(currentTitle) ? currentUrl : currentTitle,
            VisitTime = DateTime.Now
        });

        PruneDomainHistory(currentDomain);
        if (isActiveTab)
        {
            LoadDomainHistory();
        }
    }

    private void HistoryListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_ignoreHistorySelection)
        {
            return;
        }

        if (HistoryListBox.SelectedItem is not HistoryRecord selectedItem)
        {
            return;
        }

        UrlTextBox.Text = selectedItem.Url;
        NavigateCore(selectedItem.Url);
    }

    private void GoButton_Click(object sender, RoutedEventArgs e)
    {
        NavigateToUrl();
    }

    private void UrlTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            NavigateToUrl();
        }
    }

    private void NavigateToUrl()
    {
        var url = NormalizeUrl(UrlTextBox.Text);
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        NavigateCore(url);
    }

    private void NavigateCore(string url)
    {
        var activeWebView = EnsureActiveWebView();
        if (activeWebView is null)
        {
            return;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            if (activeWebView.CoreWebView2 is not null)
            {
                activeWebView.CoreWebView2.Navigate(uri.ToString());
                return;
            }

            InitializeWebViewAsync(activeWebView, uri.ToString());
        }
    }

    private void HomeGoButton_Click(object sender, RoutedEventArgs e)
    {
        NavigateFromHomeInput();
    }

    private void HomeUrlTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            NavigateFromHomeInput();
        }
    }

    private void NavigateFromHomeInput()
    {
        var url = NormalizeUrl(HomeUrlTextBox.Text);
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        OpenInBrowser(url);
    }

    private void OpenInBrowser(string url, string? domainHint = null)
    {
        EnsureActiveWebView();
        _currentActiveDomain = string.IsNullOrWhiteSpace(domainHint) ? ExtractRootDomain(url) : domainHint;
        CurrentDomainText.Text = $"正在浏览: {_currentActiveDomain}";
        HomeView.Visibility = Visibility.Hidden;
        BrowserView.Visibility = Visibility.Visible;
        LoadDomainHistory();
        UrlTextBox.Text = url;
        NavigateCore(url);
    }

    private WebView2? EnsureActiveWebView()
    {
        if (BrowserTabs.Items.Count == 0)
        {
            return CreateBrowserTab(null, true);
        }

        var selectedTab = BrowserTabs.SelectedItem as TabItem;
        if (selectedTab is null)
        {
            BrowserTabs.SelectedIndex = 0;
            selectedTab = BrowserTabs.SelectedItem as TabItem;
        }

        return selectedTab?.Tag as WebView2;
    }

    private WebView2 CreateBrowserTab(string? initialUrl, bool activate)
    {
        var webView = new WebView2();
        webView.NavigationCompleted += MyWebView_NavigationCompleted;

        var tab = new TabItem
        {
            Content = webView,
            Tag = webView
        };

        var initialHeader = string.IsNullOrWhiteSpace(initialUrl) ? "新标签页" : ExtractRootDomain(initialUrl);
        tab.Header = BuildTabHeader(tab, initialHeader);

        _tabsByWebView[webView] = tab;
        BrowserTabs.Items.Add(tab);

        if (activate)
        {
            BrowserTabs.SelectedItem = tab;
            UrlTextBox.Text = initialUrl ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(initialUrl))
            {
                _currentActiveDomain = ExtractRootDomain(initialUrl);
                CurrentDomainText.Text = $"正在浏览: {_currentActiveDomain}";
                LoadDomainHistory();
            }
        }

        InitializeWebViewAsync(webView, initialUrl);
        return webView;
    }

    private async void InitializeWebViewAsync(WebView2 webView, string? initialUrl)
    {
        if (!_webViewInitTasks.TryGetValue(webView, out var initTask))
        {
            initTask = InitializeWebViewCoreAsync(webView);
            _webViewInitTasks[webView] = initTask;
        }

        await initTask;

        if (!string.IsNullOrWhiteSpace(initialUrl))
        {
            webView.CoreWebView2?.Navigate(initialUrl);
        }
    }

    private async Task InitializeWebViewCoreAsync(WebView2 webView)
    {
        var environment = await _webViewEnvironmentTask;
        await webView.EnsureCoreWebView2Async(environment);
        if (webView.CoreWebView2 is null)
        {
            return;
        }

        webView.CoreWebView2.NewWindowRequested -= CoreWebView2_NewWindowRequested;
        webView.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;
    }

    private void CoreWebView2_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        var targetUrl = NormalizeUrl(e.Uri);

        if (string.IsNullOrWhiteSpace(targetUrl))
        {
            return;
        }

        CreateBrowserTab(targetUrl, true);
    }

    private WebView2? GetCurrentWebView()
    {
        return (BrowserTabs.SelectedItem as TabItem)?.Tag as WebView2;
    }

    private void BrowserTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var currentWebView = GetCurrentWebView();
        if (currentWebView is null)
        {
            return;
        }

        var currentUrl = currentWebView.Source?.ToString();
        UrlTextBox.Text = currentUrl ?? string.Empty;

        if (string.IsNullOrWhiteSpace(currentUrl))
        {
            return;
        }

        _currentActiveDomain = ExtractRootDomain(currentUrl);
        CurrentDomainText.Text = $"正在浏览: {_currentActiveDomain}";
        LoadDomainHistory();
    }

    private object BuildTabHeader(TabItem ownerTab, string title)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal
        };

        var titleText = new TextBlock
        {
            Text = title,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        var closeButton = new Button
        {
            Content = "x",
            Width = 18,
            Height = 18,
            Padding = new Thickness(0),
            Tag = ownerTab,
            FontSize = 10
        };
        closeButton.Click += CloseTabButton_Click;

        _tabHeaders[ownerTab] = titleText;
        panel.Children.Add(titleText);
        panel.Children.Add(closeButton);
        return panel;
    }

    private void SetTabHeaderText(TabItem tab, string text)
    {
        if (_tabHeaders.TryGetValue(tab, out var headerText))
        {
            headerText.Text = text;
        }
    }

    private void CloseTabButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button closeButton || closeButton.Tag is not TabItem tabToClose)
        {
            return;
        }

        var closingIndex = BrowserTabs.Items.IndexOf(tabToClose);
        var wasSelected = ReferenceEquals(BrowserTabs.SelectedItem, tabToClose);

        if (tabToClose.Tag is WebView2 webView)
        {
            webView.NavigationCompleted -= MyWebView_NavigationCompleted;
            if (webView.CoreWebView2 is not null)
            {
                webView.CoreWebView2.NewWindowRequested -= CoreWebView2_NewWindowRequested;
            }

            _tabsByWebView.Remove(webView);
            _webViewInitTasks.Remove(webView);
        }

        _tabHeaders.Remove(tabToClose);
        BrowserTabs.Items.Remove(tabToClose);

        if (BrowserTabs.Items.Count == 0)
        {
            CreateBrowserTab(null, true);
            e.Handled = true;
            return;
        }

        if (wasSelected)
        {
            var nextIndex = closingIndex >= BrowserTabs.Items.Count ? BrowserTabs.Items.Count - 1 : closingIndex;
            BrowserTabs.SelectedIndex = nextIndex;
        }

        e.Handled = true;
    }

    private void DomainDeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string domain || string.IsNullOrWhiteSpace(domain))
        {
            return;
        }

        var result = MessageBox.Show(
            this,
            $"确认删除 {domain} 的全部历史记录吗？",
            "删除确认",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        var records = _db.Table<HistoryRecord>()
            .Where(x => x.RootDomain == domain)
            .ToList();

        foreach (var record in records)
        {
            _db.Delete<HistoryRecord>(record.Id);
        }

        if (string.Equals(_currentActiveDomain, domain, StringComparison.OrdinalIgnoreCase))
        {
            LoadDomainHistory();
        }

        DomainListView.SelectedItem = null;
        LoadHomeView();
        e.Handled = true;
    }

    private static string NormalizeUrl(string? rawUrl)
    {
        var url = rawUrl?.Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        if (url.Contains("://", StringComparison.Ordinal))
        {
            return url;
        }

        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            url = "https://" + url;
        }

        return url;
    }

    private void ToggleHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        _isHistoryCollapsed = !_isHistoryCollapsed;
        HistoryColumn.Width = _isHistoryCollapsed ? new GridLength(0) : new GridLength(280);
        ToggleHistoryButton.Content = _isHistoryCollapsed ? "展开历史" : "折叠历史";
    }

    private void PruneAllDomainHistories()
    {
        var domains = _db.Table<HistoryRecord>()
            .Select(x => x.RootDomain)
            .Distinct()
            .ToList();

        foreach (var domain in domains)
        {
            PruneDomainHistory(domain);
        }
    }

    private void NormalizeStoredDomains()
    {
        var records = _db.Table<HistoryRecord>().ToList();
        foreach (var record in records)
        {
            var normalized = ExtractRootDomain(record.Url);
            if (!string.Equals(record.RootDomain, normalized, StringComparison.OrdinalIgnoreCase))
            {
                record.RootDomain = normalized;
                _db.Update(record);
            }
        }
    }

    private void PruneDomainHistory(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            return;
        }

        var recordsToDelete = _db.Table<HistoryRecord>()
            .Where(x => x.RootDomain == domain)
            .OrderByDescending(x => x.VisitTime)
            .ThenByDescending(x => x.Id)
            .Skip(MaxHistoryPerDomain)
            .ToList();

        if (recordsToDelete.Count == 0)
        {
            return;
        }

        foreach (var record in recordsToDelete)
        {
            _db.Delete<HistoryRecord>(record.Id);
        }
    }
}
