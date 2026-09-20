using System.IO;
using System.Diagnostics;
using System.Windows.Input;
using System.Windows;
using System.Windows.Controls;
using UACToolBox.Contracts;
using UACToolBox.WindowsIntegration;
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
    string? suggestedName;
    readonly Queue<string[]> pendingActivations = new();
    public MainWindow()
    {
        InitializeComponent();
        Closing += (_, e) => { if (busy) { e.Cancel = true; Report("正在完成操作，请稍候再关闭。"); } };
        Loaded += (_, _) => Run(() =>
        {
            configuration = Store.Load(); loaded = true; RefreshList(); Fill(new()); Run(RefreshStatus); Run(MaybeOfferOnboarding);

        });
    }
    void Run(Action action)
    {
        try { action(); }
        catch (Exception ex) { Report(ex.Message); MessageBox.Show(this, ex.Message, "操作未完成", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    async Task RunAsync(Func<Task> action)
    {
        busy = true; Pages.IsEnabled = false;
        try { await action(); }
        catch (Exception ex) { Report(ex.Message); MessageBox.Show(this, ex.Message, "操作未完成", MessageBoxButton.OK, MessageBoxImage.Warning); }
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
    void Fill(LaunchEntry entry)
    {
        filling = true;
        EditorTabs.SelectedIndex = 0;
        try
        {
            bool existing = configuration.Entries.Any(e => e.Id == entry.Id);
            EditorTitle.Text = existing ? "编辑快捷方式" : "创建快捷方式";
            suggestedName = existing ? null : entry.ShortcutName;
            editing = entry; NameInput.Text = entry.ShortcutName; PathInput.Text = entry.ExecutablePath;
            ArgumentsInput.Text = entry.ArgumentsRaw; DirectoryInput.Text = entry.WorkingDirectory;
            IconInput.Text = entry.IconPath; IconIndexInput.Text = entry.IconIndex.ToString();
            EnabledInput.IsChecked = entry.Enabled; ShowInput.SelectedIndex = entry.ShowMode switch { 7 => 1, 3 => 2, _ => 0 };
            DestinationPathInput.Text = entry.ShortcutDirectory;
            DestinationInput.SelectedIndex = string.IsNullOrEmpty(entry.ShortcutDirectory) ? 0 : 1;
            CustomDestinationPanel.Visibility = DestinationInput.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
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
        var dialog = new OpenFolderDialog { Title = "选择快捷方式创建位置", Multiselect = false };
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
                entry = configuration.Entries.SingleOrDefault(e => e.Id == id) ?? throw new InvalidDataException("此快捷方式的授权项已不存在。");
            else throw new InvalidDataException("不能将启动器本身作为目标。");
        }
        if (!ConfirmReplace()) return;
        Fill(entry); Pages.SelectedItem = EditorPage; Report("已回填，请检查参数和工作目录后保存。");
    }
    async Task<LaunchEntry> SaveAsync()
    {
        if (!loaded) throw new InvalidOperationException("配置尚未加载。");
        string path = Path.GetFullPath(PathInput.Text.Trim());
        string directory = string.IsNullOrWhiteSpace(DirectoryInput.Text) ? Path.GetDirectoryName(path)! : Path.GetFullPath(DirectoryInput.Text.Trim());
        if (string.Equals(path, Store.LauncherPath, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("不能将启动器本身作为目标。");
        var entry = editing with { ShortcutName = ShortcutOutput.NormalizeName(NameInput.Text), ShortcutDirectory = DestinationInput.SelectedIndex == 1 ? DestinationPathInput.Text : "", ExecutablePath = path, ArgumentsRaw = ArgumentsInput.Text,
            WorkingDirectory = directory, ShowMode = ShowInput.SelectedIndex switch { 1 => 7, 2 => 3, _ => 1 },
            IconPath = IconInput.Text.Trim(), IconIndex = int.Parse(IconIndexInput.Text), Enabled = EnabledInput.IsChecked == true };
        if (entry.Enabled) Access.ValidateTarget(path, directory);
        var next = configuration with { Entries = configuration.Entries.Where(e => e.Id != entry.Id).Append(entry).ToList() };
        await ConfigurationClient.SaveAsync(next); configuration = Store.Load(); Fill(entry); RefreshList(); RefreshStatus(); Report("配置已保存。"); return entry;
    }
    void PageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loaded && ReferenceEquals(e.Source, Pages) && Pages.SelectedItem == SettingsPage) RefreshStatus();
    }
    void RefreshStatus()
    {
        bool taskOk = false, environmentOk = false, menusOk = false;
        try { var task = Scheduler.Inspect(); taskOk = task.State == InstallationState.Ready; SetStatus(InstallDot, InstallStatus, task.Message, taskOk); }
        catch (Exception ex) { SetStatus(InstallDot, InstallStatus, "检测失败：" + ex.Message, false); }
        try { bool saved = File.Exists(Store.FilePath); SetStatus(ConfigurationDot, ConfigurationStatus, (saved ? $"正常，{Store.Load().Entries.Count} 项" : "尚未保存") + "\n" + Store.FilePath, saved); }
        catch (Exception ex) { SetStatus(ConfigurationDot, ConfigurationStatus, "检测失败：" + ex.Message + "\n" + Store.FilePath, false); }
        try { var environment = DesktopIntegration.InspectEnvironment(); environmentOk = environment.Ready; SetStatus(EnvironmentDot, EnvironmentStatus, environment.Message, environmentOk); }
        catch (Exception ex) { SetStatus(EnvironmentDot, EnvironmentStatus, "检测失败：" + ex.Message, false); }
        try { var menus = DesktopIntegration.InspectMenus(); menusOk = menus.Ready; SetStatus(MenusDot, MenusStatus, menus.Message, menusOk); }
        catch (Exception ex) { SetStatus(MenusDot, MenusStatus, "检测失败：" + ex.Message, false); }
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
        "创建／编辑页已有填写或预填数据。是否覆盖？选择否将保留当前内容。", "覆盖当前填写内容",
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
    void ImportClick(object sender, RoutedEventArgs e) => Run(() => { var dialog = new OpenFileDialog { Filter = "程序或快捷方式|*.exe;*.lnk" }; if (dialog.ShowDialog(this) == true) Import(dialog.FileName); });
    async void SaveClick(object sender, RoutedEventArgs e) => await RunAsync(async () => { await SaveAsync(); });
    async void ShortcutClick(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        string output = ShortcutOutput.GetPath(NameInput.Text, DestinationInput.SelectedIndex == 1 ? DestinationPathInput.Text : "");
        if (File.Exists(output) && MessageBox.Show(this, $"此快捷方式已存在，是否覆盖？\n{output}", "覆盖快捷方式",
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        var entry = await SaveAsync();
        Shortcuts.Create(output, entry);
        Fill(new());
        Report("已创建快捷方式：" + output);
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
        if (!entry.Enabled) throw new InvalidOperationException("此项已停用，请先编辑并启用。");
        var start = new ProcessStartInfo(Store.LauncherPath) { UseShellExecute = false };
        start.ArgumentList.Add("launch"); start.ArgumentList.Add(entry.Id.ToString());
        using var process = Process.Start(start);
    });
    async void DeleteClick(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (EntriesList.SelectedItem is not LaunchEntry entry) return;
        if (MessageBox.Show(this, $"删除 {entry.ShortcutName}？已有快捷方式将失效。", "删除", MessageBoxButton.YesNo,
            MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        await ConfigurationClient.SaveAsync(configuration with { Entries = configuration.Entries.Where(x => x.Id != entry.Id).ToList() });
        configuration = Store.Load(); RefreshList();
        if (editing.Id == entry.Id) Fill(new());
        RefreshStatus(); Report("已删除。");
    });
    async void InstallClick(object sender, RoutedEventArgs e) => await RunAsync(async () => { await Maintenance.RunAsync("install"); RefreshStatus(); Report("计划任务已安装。环境变量及右键菜单请分别设置。"); });
    async void RegisterClick(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        await Maintenance.RunAsync("register"); RefreshStatus();
        Report("一键注册完成：计划任务、环境变量、右键菜单已设置。");
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
        if (MessageBox.Show(this, "尚未注册任何系统组件，快捷方式暂无法免 UAC 启动。\n\n现在前往「设置 → 系统」完成一键注册？", "欢迎使用 UACToolBox",
            MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
            Pages.SelectedItem = SettingsPage;
    }
    void RefreshClick(object sender, RoutedEventArgs e) => Run(RefreshStatus);
    async void UninstallClick(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (MessageBox.Show(this, "卸载计划任务？保留授权配置和快捷方式，已有快捷方式将无法启动。", "卸载任务", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        await Maintenance.RunAsync("uninstall"); RefreshStatus(); Report("计划任务已卸载。右键菜单可单独移除。");
    });
    async void EnvironmentClick(object sender, RoutedEventArgs e) => await RunAsync(async () => { await Maintenance.RunAsync("environment-add"); RefreshStatus(); Report("环境变量已更新。"); });
    async void RemoveEnvironmentClick(object sender, RoutedEventArgs e) => await RunAsync(async () => { await Maintenance.RunAsync("environment-remove"); RefreshStatus(); Report("已处理环境变量移除；其他安装的值保持不变。"); });
    async void MenusClick(object sender, RoutedEventArgs e) => await RunAsync(async () => { await Maintenance.RunAsync("menus-add"); RefreshStatus(); Report("右键菜单已注册。"); });
    async void RemoveMenusClick(object sender, RoutedEventArgs e) => await RunAsync(async () => { await Maintenance.RunAsync("menus-remove"); RefreshStatus(); Report("右键菜单已移除。"); });
    void SearchChanged(object sender, TextChangedEventArgs e) { if (loaded) RefreshList(); }

}
