using System.IO;
using System.Diagnostics;
using System.Windows.Input;
using System.Windows;
using System.Windows.Controls;
using UACToolBox.Contracts;
using UACToolBox.WindowsIntegration;
using UACToolBox.Localization;
using Microsoft.Win32;
namespace UACToolBox.Config;
public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    Configuration configuration = new();
    LaunchEntry editing = new();
    bool loaded;
    bool busy;
    bool activating;
    bool filling;
    bool selectingLanguage;
    string? suggestedName;
    readonly Queue<string[]> pendingActivations = new();
    public MainWindow()
    {
        InitializeComponent();
        Closing += (_, e) => { if (busy) { e.Cancel = true; Report(Loc.T("msg.busyClose")); } };
        Loaded += (_, _) => Run(() =>
        {
            configuration = Store.Load(); loaded = true; RefreshList(); Fill(new()); Run(RefreshStatus); Run(MaybeOfferOnboarding); LoadLanguages();
        });
        Loc.CultureChanged += () => Dispatcher.BeginInvoke(() => { if (!loaded) return; UpdateEditorTitle(); Run(RefreshStatus); });
    }
    void Run(Action action)
    {
        try { action(); }
        catch (Exception ex) { Report(ex.Message); MessageBox.Show(this, ex.Message, Loc.T("msg.opFailed"), MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    async Task RunAsync(Func<Task> action)
    {
        busy = true; Pages.IsEnabled = false;
        try { await action(); }
        catch (Exception ex) { Report(ex.Message); MessageBox.Show(this, ex.Message, Loc.T("msg.opFailed"), MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally
        {
            busy = false; Pages.IsEnabled = true;
            DrainActivations();
        }
    }
    void Report(string text) { StatusText.Text = text; Diagnostics.AppendText($"{DateTime.Now:HH:mm:ss} {text}{Environment.NewLine}"); }
    void RefreshList()
    {
        string text = SearchBox.Text.Trim();
        EntriesList.ItemsSource = configuration.Entries.Where(e => e.ShortcutName.Contains(text, StringComparison.OrdinalIgnoreCase) || e.ExecutablePath.Contains(text, StringComparison.OrdinalIgnoreCase)).ToArray();
    }
    void UpdateEditorTitle()
    {
        bool existing = configuration.Entries.Any(e => e.Id == editing.Id);
        EditorTitle.Text = Loc.T(existing ? "editor.edit.title" : "editor.create.title");
    }
    void Fill(LaunchEntry entry)
    {
        filling = true;
        EditorTabs.SelectedIndex = 0;
        try
        {
            bool existing = configuration.Entries.Any(e => e.Id == entry.Id);
            suggestedName = existing ? null : entry.ShortcutName;
            editing = entry; NameInput.Text = entry.ShortcutName; PathInput.Text = entry.ExecutablePath;
            ArgumentsInput.Text = entry.ArgumentsRaw; DirectoryInput.Text = entry.WorkingDirectory;
            IconInput.Text = entry.IconPath; IconIndexInput.Text = entry.IconIndex.ToString();
            EnabledInput.IsChecked = entry.Enabled; ShowInput.SelectedIndex = entry.ShowMode switch { 7 => 1, 3 => 2, _ => 0 };
            DestinationPathInput.Text = entry.ShortcutDirectory;
            DestinationInput.SelectedIndex = string.IsNullOrEmpty(entry.ShortcutDirectory) ? 0 : 1;
            CustomDestinationPanel.Visibility = DestinationInput.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
            UpdateEditorTitle();
        }
        finally { filling = false; }
    }
    void TargetPathChanged(object sender, TextChangedEventArgs e)
    {
        if (filling || NameInput is null) return;
        if (!string.IsNullOrWhiteSpace(NameInput.Text) && NameInput.Text != suggestedName) return;
        try { suggestedName = ShortcutOutput.DefaultName(PathInput.Text); NameInput.Text = suggestedName; }
        catch (ArgumentException) { } // Allow partially typed paths until save validation.
    }
    bool ChooseDestination()
    {
        var dialog = new OpenFolderDialog { Title = Loc.T("editor.dest.pick.title"), Multiselect = false };
        if (Directory.Exists(DestinationPathInput.Text)) dialog.InitialDirectory = DestinationPathInput.Text;
        if (dialog.ShowDialog(this) != true) return false;
        DestinationPathInput.Text = dialog.FolderName;
        return true;
    }
    void DestinationChanged(object sender, SelectionChangedEventArgs e)
    {
        if (filling || CustomDestinationPanel is null) return;
        CustomDestinationPanel.Visibility = DestinationInput.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        if (DestinationInput.SelectedIndex == 1)
            Run(() => { if (!ChooseDestination() && string.IsNullOrEmpty(DestinationPathInput.Text)) DestinationInput.SelectedIndex = 0; });
    }
    void ChooseDestinationClick(object sender, RoutedEventArgs e) => Run(() => { ChooseDestination(); });
    void Import(string path)
    {
        var entry = Shortcuts.Import(path);
        if (string.Equals(entry.ExecutablePath, Store.LauncherPath, StringComparison.OrdinalIgnoreCase))
        {
            if (entry.ArgumentsRaw.StartsWith("launch ") && Guid.TryParse(entry.ArgumentsRaw[7..], out var id))
                entry = configuration.Entries.SingleOrDefault(e => e.Id == id) ?? throw new InvalidDataException(Loc.T("err.legacyEntryGone"));
            else throw new InvalidDataException(Loc.T("err.selfTarget"));
        }
        if (!ConfirmReplace()) return;
        Fill(entry); Pages.SelectedItem = EditorPage; Report(Loc.T("msg.imported"));
    }
    async Task<LaunchEntry> SaveAsync()
    {
        if (!loaded) throw new InvalidOperationException(Loc.T("err.configNotLoaded"));
        string path = Path.GetFullPath(PathInput.Text.Trim());
        string directory = string.IsNullOrWhiteSpace(DirectoryInput.Text) ? Path.GetDirectoryName(path)! : Path.GetFullPath(DirectoryInput.Text.Trim());
        if (string.Equals(path, Store.LauncherPath, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(Loc.T("err.selfTarget"));
        var entry = editing with { ShortcutName = ShortcutOutput.NormalizeName(NameInput.Text), ShortcutDirectory = DestinationInput.SelectedIndex == 1 ? DestinationPathInput.Text : "", ExecutablePath = path, ArgumentsRaw = ArgumentsInput.Text,
            WorkingDirectory = directory, ShowMode = ShowInput.SelectedIndex switch { 1 => 7, 2 => 3, _ => 1 },
            IconPath = IconInput.Text.Trim(), IconIndex = int.Parse(IconIndexInput.Text), Enabled = EnabledInput.IsChecked == true };
        if (entry.Enabled) Access.ValidateTarget(path, directory);
        var next = configuration with { Entries = configuration.Entries.Where(e => e.Id != entry.Id).Append(entry).ToList() };
        await ConfigurationClient.SaveAsync(next); configuration = Store.Load(); Fill(entry); RefreshList(); RefreshStatus(); Report(Loc.T("msg.saved")); return entry;
    }
    void PageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loaded && ReferenceEquals(e.Source, Pages) && Pages.SelectedItem == SettingsPage) RefreshStatus();
    }
    void RefreshStatus()
    {
        bool taskOk = false, environmentOk = false, menusOk = false;
        try { var task = Scheduler.Inspect(); taskOk = task.State == InstallationState.Ready; SetStatus(InstallDot, InstallStatus, task.Message, taskOk); }
        catch (Exception ex) { SetStatus(InstallDot, InstallStatus, Loc.T("status.detectFailed", ex.Message), false); }
        try { bool saved = File.Exists(Store.FilePath); SetStatus(ConfigurationDot, ConfigurationStatus, (saved ? Loc.T("status.config.ok", Store.Load().Entries.Count) : Loc.T("status.config.none")) + "\n" + Store.FilePath, saved); }
        catch (Exception ex) { SetStatus(ConfigurationDot, ConfigurationStatus, Loc.T("status.detectFailed", ex.Message) + "\n" + Store.FilePath, false); }
        try { var environment = DesktopIntegration.InspectEnvironment(); environmentOk = environment.Ready; SetStatus(EnvironmentDot, EnvironmentStatus, environment.Message, environmentOk); }
        catch (Exception ex) { SetStatus(EnvironmentDot, EnvironmentStatus, Loc.T("status.detectFailed", ex.Message), false); }
        try { var menus = DesktopIntegration.InspectMenus(); menusOk = menus.Ready; SetStatus(MenusDot, MenusStatus, menus.Message, menusOk); }
        catch (Exception ex) { SetStatus(MenusDot, MenusStatus, Loc.T("status.detectFailed", ex.Message), false); }
        // 一键注册覆盖任务、环境变量、右键菜单；配置文件不属于注册项。
        RegisterButton.IsEnabled = !(taskOk && environmentOk && menusOk);
    }
    static readonly System.Windows.Media.SolidColorBrush ReadyBrush = new(System.Windows.Media.Color.FromRgb(0x25, 0x80, 0x5D));
    static readonly System.Windows.Media.SolidColorBrush ProblemBrush = new(System.Windows.Media.Color.FromRgb(0xC4, 0x2B, 0x1C));
    void SetStatus(System.Windows.Shapes.Ellipse dot, TextBlock target, string text, bool ready)
    {
        target.Text = text; dot.Fill = ready ? ReadyBrush : ProblemBrush;
    }
    bool HasEditorData => new[] { NameInput.Text, PathInput.Text, ArgumentsInput.Text, DirectoryInput.Text, IconInput.Text }.Any(x => !string.IsNullOrWhiteSpace(x)) || IconIndexInput.Text != "0" || ShowInput.SelectedIndex != 0 || EnabledInput.IsChecked != true || DestinationInput.SelectedIndex != 0;
    bool ConfirmReplace() => !HasEditorData || MessageBox.Show(this,
        Loc.T("msg.confirmOverwrite"), Loc.T("msg.overwriteTitle"),
        MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
    public void HandleActivation(string[] args)
    {
        pendingActivations.Enqueue(args);
        DrainActivations();
    }
    void DrainActivations()
    {
        if (busy || activating) return;
        activating = true;
        try
        {
            while (pendingActivations.TryDequeue(out var args))
            {
                if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
                Show(); Activate();
                Run(() => { if (args is ["import", var file]) Import(file); });
            }
        }
        finally { activating = false; }
    }
    void NewClick(object sender, RoutedEventArgs e)
    {
        if (!ConfirmReplace()) return;
        Fill(new()); Pages.SelectedItem = EditorPage;
    }
    void BackClick(object sender, RoutedEventArgs e) => Pages.SelectedItem = ListPage;
    void EditClick(object sender, RoutedEventArgs e)
    {
        if (EntriesList.SelectedItem is not LaunchEntry entry || !ConfirmReplace()) return;
        Fill(entry); Pages.SelectedItem = EditorPage;
    }
    void ImportClick(object sender, RoutedEventArgs e) => Run(() => { var dialog = new OpenFileDialog { Filter = "EXE / LNK|*.exe;*.lnk" }; if (dialog.ShowDialog(this) == true) Import(dialog.FileName); });
    async void SaveClick(object sender, RoutedEventArgs e) => await RunAsync(async () => { await SaveAsync(); });
    async void ShortcutClick(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        string output = ShortcutOutput.GetPath(NameInput.Text, DestinationInput.SelectedIndex == 1 ? DestinationPathInput.Text : "");
        if (File.Exists(output) && MessageBox.Show(this, string.Format(Loc.T("msg.confirmShortcut"), output), Loc.T("msg.shortcutOverTitle"),
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        var entry = await SaveAsync();
        Shortcuts.Create(output, entry);
        Fill(new());
        Report(string.Format(System.Globalization.CultureInfo.CurrentCulture, Loc.T("msg.shortcutCreated"), output));
    });
    void SelectContextItem(object sender, MouseButtonEventArgs e)
    {
        var item = ItemsControl.ContainerFromElement(EntriesList, e.OriginalSource as DependencyObject) as ListBoxItem;
        if (item is null) EntriesList.SelectedItem = null;
        else { item.IsSelected = true; item.Focus(); }
    }
    void ListMenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu menu)
            foreach (var item in menu.Items.OfType<MenuItem>()) item.IsEnabled = EntriesList.SelectedItem is LaunchEntry;
    }
    void OpenClick(object sender, RoutedEventArgs e) => Run(() =>
    {
        if (e is MouseButtonEventArgs mouse && ItemsControl.ContainerFromElement(EntriesList, mouse.OriginalSource as DependencyObject) is not ListBoxItem) return;
        if (EntriesList.SelectedItem is not LaunchEntry entry) return;
        if (!entry.Enabled) throw new InvalidOperationException(Loc.T("err.disabled"));
        var start = new ProcessStartInfo(Store.LauncherPath) { UseShellExecute = false };
        start.ArgumentList.Add("launch"); start.ArgumentList.Add(entry.Id.ToString());
        using var process = Process.Start(start);
    });
    async void DeleteClick(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (EntriesList.SelectedItem is not LaunchEntry entry) return;
        if (MessageBox.Show(this, string.Format(Loc.T("msg.confirmDelete"), entry.ShortcutName), Loc.T("msg.deleteTitle"), MessageBoxButton.YesNo,
            MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        await ConfigurationClient.SaveAsync(configuration with { Entries = configuration.Entries.Where(x => x.Id != entry.Id).ToList() });
        configuration = Store.Load(); RefreshList();
        if (editing.Id == entry.Id) Fill(new());
        RefreshStatus(); Report(Loc.T("msg.deleted"));
    });
    async void InstallClick(object sender, RoutedEventArgs e) => await RunAsync(async () => { await Maintenance.RunAsync("install"); RefreshStatus(); Report(Loc.T("msg.taskInstalled")); });
    void RefreshClick(object sender, RoutedEventArgs e) => Run(RefreshStatus);
    async void RegisterClick(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        await Maintenance.RunAsync("register"); RefreshStatus();
        Report(Loc.T("msg.registerDone"));
    });
    void MaybeOfferOnboarding()
    {
        try
        {
            if (File.Exists(Store.FilePath)) return;
            if (Scheduler.Inspect().State == InstallationState.Ready) return;
            if (DesktopIntegration.InspectEnvironment().Ready) return;
            if (DesktopIntegration.InspectMenus().Ready) return;
        }
        catch { return; }
        if (MessageBox.Show(this, Loc.T("msg.onboarding"), Loc.T("msg.welcomeTitle"),
            MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
            Pages.SelectedItem = SettingsPage;
    }
    async void UninstallClick(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (MessageBox.Show(this, Loc.T("msg.confirmUninstall"), Loc.T("msg.uninstallTitle"), MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        await Maintenance.RunAsync("uninstall"); RefreshStatus(); Report(Loc.T("msg.taskUninstalled"));
    });
    async void EnvironmentClick(object sender, RoutedEventArgs e) => await RunAsync(async () => { await Maintenance.RunAsync("environment-add"); RefreshStatus(); Report(Loc.T("msg.envUpdated")); });
    async void RemoveEnvironmentClick(object sender, RoutedEventArgs e) => await RunAsync(async () => { await Maintenance.RunAsync("environment-remove"); RefreshStatus(); Report(Loc.T("msg.envRemoved")); });
    async void MenusClick(object sender, RoutedEventArgs e) => await RunAsync(async () => { await Maintenance.RunAsync("menus-add"); RefreshStatus(); Report(Loc.T("msg.menusAdded")); });
    async void RemoveMenusClick(object sender, RoutedEventArgs e) => await RunAsync(async () => { await Maintenance.RunAsync("menus-remove"); RefreshStatus(); Report(Loc.T("msg.menusRemoved")); });
    void SearchChanged(object sender, TextChangedEventArgs e) { if (loaded) RefreshList(); }
    void LoadLanguages()
    {
        selectingLanguage = true;
        try
        {
            LanguageBox.ItemsSource = Loc.AvailableCultures
                .Select(name => { try { return System.Globalization.CultureInfo.GetCultureInfo(name); } catch (System.Globalization.CultureNotFoundException) { return null; } })
                .Where(culture => culture is not null)
                .Select(culture => culture!.NativeName)
                .ToArray();
            var current = System.Globalization.CultureInfo.GetCultureInfo(Loc.Current).NativeName;
            var index = Array.IndexOf((string[])LanguageBox.ItemsSource, current);
            LanguageBox.SelectedIndex = index < 0 ? 0 : index;
        }
        finally { selectingLanguage = false; }
    }
    void LanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (selectingLanguage || LanguageBox.SelectedItem is not string name) return;
        var match = Loc.AvailableCultures.FirstOrDefault(culture =>
            string.Equals(System.Globalization.CultureInfo.GetCultureInfo(culture).NativeName, name, StringComparison.OrdinalIgnoreCase));
        if (match is null) return;
        Loc.SetCulture(match);
        LoadLanguages();
    }
}
