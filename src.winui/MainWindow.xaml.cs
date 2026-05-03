using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OmegaAssetStudio.WinUI.Modules.Settings;
using OmegaAssetStudio.WinUI.Modules.MaterialEditor;
using OmegaAssetStudio.WinUI.Modules.MFL;
using OmegaAssetStudio.WinUI.Modules.UpkMigration;
using OmegaAssetStudio.WinUI.Pages;
using OmegaAssetStudio.WinUI.Services;
using System.Linq;
using System.IO;
using WinRT.Interop;

namespace OmegaAssetStudio.WinUI;

public sealed partial class MainWindow : Window
{
    private string currentTag = string.Empty;
    private object? currentPage;
    private object? lastWorkspacePage;
    private MeshPage? lastMeshPage;

    public string CurrentTag => currentTag;
    public object? CurrentPage => currentPage;
    public object? LastWorkspacePage => lastWorkspacePage;
    public MeshPage? LastMeshPage => lastMeshPage;

    public MainWindow()
    {
        InitializeComponent();
        ApplyWindowIcon();
        ThemeService.ThemeChanged += ThemeService_ThemeChanged;
        ApplyTheme(ThemeService.CurrentTheme);
        Closed += MainWindow_Closed;
        RootNavigation.SelectedItem = RootNavigation.MenuItems[0];
        NavigateToTag("home");
    }

    private void ApplyWindowIcon()
    {
        try
        {
            string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "OmegaAssetStudio.ico");
            if (!File.Exists(iconPath))
                return;

            nint hwnd = WindowNative.GetWindowHandle(this);
            Microsoft.UI.WindowId windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            AppWindow appWindow = AppWindow.GetFromWindowId(windowId);
            appWindow.SetIcon(iconPath);
        }
        catch
        {
        }
    }

    private void RootNavigation_OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer?.Tag is string tag)
            NavigateToTag(tag);
    }

    public void NavigateToTag(string tag, object? parameter = null)
    {
        if (string.Equals(currentTag, tag, System.StringComparison.Ordinal) && parameter is null && tag != "mesh")
        {
            return;
        }

        if (parameter is null)
        {
            if (string.Equals(tag, "mesh", System.StringComparison.OrdinalIgnoreCase))
            {
                parameter = "Preview";
            }
        }

        try
        {
            App.WriteDiagnosticsLog("MainWindow.NavigateToTag", $"enter tag={tag}, parameter={parameter?.GetType().Name ?? "(null)"}");
            switch (tag)
            {
                case "objects":
                    RootFrame.Navigate(typeof(ObjectsPage), parameter);
                    break;
                case "recent":
                    RootFrame.Navigate(typeof(RecentUpksPage), parameter);
                    break;
                case "mesh":
                    RootFrame.Navigate(typeof(MeshPage), parameter);
                    break;
                case "mfl":
                    RootFrame.Navigate(typeof(MFLView), parameter);
                    break;
                case "materialeditor":
                    RootFrame.Navigate(typeof(MaterialEditorView), parameter);
                    break;
                case "upkmigration":
                    RootFrame.Navigate(typeof(UpkMigrationView), parameter);
                    break;
                case "textures":
                    RootFrame.Navigate(typeof(TexturesPage), parameter);
                    break;
                case "omegaintel":
                    RootFrame.Navigate(typeof(OmegaIntelPage), parameter);
                    break;
                case "diagnostics":
                    RootFrame.Navigate(typeof(DiagnosticsPage), parameter);
                    break;
                case "backup":
                    RootFrame.Navigate(typeof(BackupPage), parameter);
                    break;
                case "retarget":
                    RootFrame.Navigate(typeof(RetargetPage), parameter);
                    break;
                case "settings":
                    RootFrame.Navigate(typeof(SettingsView), parameter);
                    break;
                default:
                    RootFrame.Navigate(typeof(HomePage), parameter);
                    break;
            }
        }
        catch (Exception ex)
        {
            App.WriteDiagnosticsLog("MainWindow.NavigateToTag", $"tag={tag}, parameter={parameter?.GetType().Name ?? "(null)"}\n{ex}");
            RootFrame.Navigate(typeof(HomePage), parameter);
        }

        currentPage = RootFrame.Content;
        if (currentPage is MeshPage meshPage)
            lastMeshPage = meshPage;
        if (tag != "diagnostics")
            lastWorkspacePage = currentPage;

        currentTag = tag;
        SelectNavigationItem(tag);
        WorkspaceSessionStore.RememberLastWorkspace(tag);
    }

    private void ApplyTheme(AppThemeMode theme)
    {
        ElementTheme elementTheme = theme == AppThemeMode.Dark ? ElementTheme.Dark : ElementTheme.Light;
        RootNavigation.RequestedTheme = elementTheme;
        RootFrame.RequestedTheme = elementTheme;
    }

    private void ThemeService_ThemeChanged(object? sender, ThemeChangedEventArgs e)
    {
        ApplyTheme(e.Theme);
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        ThemeService.ThemeChanged -= ThemeService_ThemeChanged;
        Closed -= MainWindow_Closed;
    }

    private void SelectNavigationItem(string tag)
    {
        NavigationViewItem? menuItem = FindNavigationItem(tag);
        if (menuItem is not null)
        {
            RootNavigation.SelectedItem = menuItem;
        }
    }

    private NavigationViewItem? FindNavigationItem(string tag)
    {
        foreach (var item in RootNavigation.MenuItems.OfType<NavigationViewItem>())
        {
            NavigationViewItem? match = FindNavigationItem(item, tag);
            if (match is not null)
                return match;
        }

        return null;
    }

    private static NavigationViewItem? FindNavigationItem(NavigationViewItem parent, string tag)
    {
        if (string.Equals(parent.Tag as string, tag, System.StringComparison.Ordinal))
            return parent;

        foreach (object child in parent.MenuItems)
        {
            if (child is NavigationViewItem childItem)
            {
                NavigationViewItem? match = FindNavigationItem(childItem, tag);
                if (match is not null)
                    return match;
            }
        }

        return null;
    }
}
