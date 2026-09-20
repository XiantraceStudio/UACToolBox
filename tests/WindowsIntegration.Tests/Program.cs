using System.IO;
using System.Runtime.InteropServices;
using UACToolBox.Contracts;
using UACToolBox.WindowsIntegration;
namespace UACToolBox.IntegrationTests;
internal static class Program
{
    static int count;
    static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        Console.WriteLine("PASS " + name); count++;
    }
    [STAThread]
    static int Main(string[] args)
    {
        if (args is ["launch", var probeId])
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "probe-result.txt"), probeId);
            return 0;
        }
        string folder = Path.Combine(Path.GetTempPath(), "LaunchManager-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        string? previous = Environment.GetEnvironmentVariable(Store.Variable);
        try
        {
            string writableExe = Path.Combine(folder, "普通目录程序.exe");
            File.WriteAllBytes(writableExe, [0]); // Validation does not execute this test file.
            Access.ValidateTarget(writableExe, folder);
            Check(true, "Accept user-writable executable and working directory");
            File.WriteAllBytes(writableExe, [1, 2]);
            Access.ValidateTarget(writableExe, folder);
            Check(true, "Accept updated target without reauthorization");
            File.Delete(writableExe);
            try { Access.ValidateTarget(writableExe, folder); throw new Exception("Missing target accepted"); }
            catch (FileNotFoundException) { Check(true, "Reject missing target"); }
            try { Access.ValidateTarget(Path.Combine(folder, "test.cmd"), folder); throw new Exception("Script accepted"); }
            catch (InvalidDataException) { Check(true, "Retain EXE target restriction"); }
            string executable = Path.Combine(Environment.SystemDirectory, "notepad.exe");
            string raw = "--name \"中文 空格\" --empty \"\" --path \"C:\\tail\\\\\"";
            string file = Path.Combine(folder, "原始 快捷方式.lnk");
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
            try
            {
                dynamic shortcut = shell.CreateShortcut(file);
                try
                {
                    shortcut.TargetPath = executable; shortcut.Arguments = raw;
                    shortcut.WorkingDirectory = Environment.SystemDirectory;
                    shortcut.IconLocation = executable + ",2"; shortcut.WindowStyle = 3;
                    shortcut.Save();
                }
                finally { Marshal.FinalReleaseComObject(shortcut); }
            }
            finally { Marshal.FinalReleaseComObject(shell); }
            var imported = Shortcuts.Import(file);
            Check(imported.ExecutablePath.Equals(executable, StringComparison.OrdinalIgnoreCase), "Import executable path");
            Check(imported.ArgumentsRaw == raw, "Import exact raw arguments");
            Check(imported.WorkingDirectory.Equals(Environment.SystemDirectory, StringComparison.OrdinalIgnoreCase), "Import working directory");
            Check(imported.IconIndex == 2 && imported.IconPath.Equals(executable, StringComparison.OrdinalIgnoreCase), "Import icon and index");
            Check(imported.ShowMode == 3, "Import window state");
            Check(imported.ShortcutName == "原始 快捷方式_UACTB", "Import default Unicode shortcut name");
            string generated = Path.Combine(folder, "生成.lnk");
            Environment.SetEnvironmentVariable(Store.Variable, executable); // Process only; no persistent settings.
            Shortcuts.Create(generated, imported);
            var generatedImport = Shortcuts.Import(generated);
            Check(generatedImport.ArgumentsRaw == "launch " + imported.Id, "Generated shortcut retains entry ID");
            Check(generatedImport.ExecutablePath.Equals(executable, StringComparison.OrdinalIgnoreCase), "Environment target resolves");
            Check(generatedImport.IconIndex == imported.IconIndex, "Generated icon index");
            Environment.SetEnvironmentVariable(Store.Variable, Path.Combine(Environment.SystemDirectory, "cmd.exe"));
            Check(Shortcuts.Import(generated).ExecutablePath.EndsWith(@"\cmd.exe", StringComparison.OrdinalIgnoreCase), "Existing shortcut follows changed process environment");
            var bytes = File.ReadAllBytes(generated);
            Check((BitConverter.ToUInt32(bytes, 20) & 0x02000200) == 0x200, "Environment flags persisted");
            byte[] signature = [0x14, 3, 0, 0, 1, 0, 0, 0xA0];
            int block = bytes.AsSpan().IndexOf(signature);
            Check(block >= 0 && System.Text.Encoding.Unicode.GetString(bytes, block + 268, 520).TrimEnd('\0') == "%" + Store.Variable + "%", "Environment data block persisted");
            TestShortcutOutput(folder, imported);
            TestShellLaunch(folder, imported);
            TestDelivery().GetAwaiter().GetResult();
            TestActivation().GetAwaiter().GetResult();
            TestTaskPolicy();
            TestConfigurationValidation(executable);
            TestConfigurationCaller().GetAwaiter().GetResult();
            Check(Maintenance.IsSupported("register") && Maintenance.IsSupported("install") && Maintenance.IsSupported("menus-remove") && !Maintenance.IsSupported("save") && !Maintenance.IsSupported("cmd.exe"), "Maintenance accepts only fixed system operations");
            Check(Store.Root.EndsWith(Path.Combine("XianTrace", "UACToolBox"), StringComparison.OrdinalIgnoreCase) && !Store.Root.Contains("LaunchManager"), "Config directory is XianTrace/UACToolBox without legacy fallback");
            Check(Access.BuildProtectedAcl(false) is System.Security.AccessControl.FileSecurity && Access.BuildProtectedAcl(true) is System.Security.AccessControl.DirectorySecurity,
                "Protected ACL templates build for files and directories without inheritance on files");
            Check(Scheduler.Inspect().Message.Length > 0, "Read actual task status without modifying tasks");
            var app = new System.Windows.Application();
            app.Resources = new System.Windows.ResourceDictionary { Source = new Uri("/Config;component/Theme.xaml", UriKind.Relative) };
            var window = new UACToolBox.Config.MainWindow();
            Check(window.FindName("EntriesList") is not null && window.FindName("NameInput") is not null, "WPF window and resources load without displaying or installing");
            Check(window.FindName("ListPage") != window.FindName("EditorPage") && window.FindName("EditorPage") is not null, "Separate list and editor pages load");
            var nameInput = (System.Windows.Controls.TextBox)window.FindName("NameInput");
            var pathInput = (System.Windows.Controls.TextBox)window.FindName("PathInput");
            pathInput.Text = @"C:\Games\Game.exe";
            Check(nameInput.Text == "Game_UACTB", "Typing executable supplies default shortcut name");
            nameInput.Text = "我的快捷方式"; pathInput.Text = @"C:\Games\Other.exe";
            Check(nameInput.Text == "我的快捷方式", "Changing target preserves custom shortcut name");
            if (args is ["--render-ui", var renderDirectory])
            {
                Directory.CreateDirectory(renderDirectory);
                var tabs = (System.Windows.Controls.TabControl)window.FindName("Pages");
                var root = (System.Windows.Controls.Grid)window.Content;
                root.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(245, 247, 251));
                window.Content = null;
                var preview = new System.Windows.Window { Content = root, Width = 1180, Height = 820, ShowInTaskbar = false };
                preview.Show();
                foreach (int index in new[] { 0, 1, 2 })
                {
                    tabs.SelectedIndex = index;
                    if (index == 2)
                    {
                        ((System.Windows.Controls.TextBlock)window.FindName("InstallStatus")).Text = "计划任务身份、执行动作及访问权限检查通过。";
                        ((System.Windows.Controls.TextBlock)window.FindName("ConfigurationStatus")).Text = "正常，3 项\nC:\\ProgramData\\XianTrace\\UACToolBox\\S-1-5-21-1004336348-1177238915-682003330-500.json";
                        ((System.Windows.Controls.TextBlock)window.FindName("EnvironmentStatus")).Text = "正常\nC:\\Program Files\\win-x64\\Launcher.exe";
                        ((System.Windows.Controls.TextBlock)window.FindName("MenusStatus")).Text = "正常";
                        var ready = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x25, 0x80, 0x5D));
                        foreach (var dot in new[] { "InstallDot", "ConfigurationDot", "EnvironmentDot", "MenusDot" })
                            ((System.Windows.Shapes.Ellipse)window.FindName(dot)).Fill = ready;
                    }
                    window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    root.Measure(new System.Windows.Size(1180, 820));
                    root.Arrange(new System.Windows.Rect(0, 0, 1180, 820)); root.UpdateLayout();
                    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(1180, 820, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                    bitmap.Render(root);
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                    using var image = File.Create(Path.Combine(renderDirectory, index == 0 ? "list.png" : index == 1 ? "editor.png" : "settings.png")); encoder.Save(image);
                }
                preview.Close();
            }
            Check(((System.Windows.Controls.TabControl)window.FindName("EditorTabs")).Items.Count == 3 &&
                ((System.Windows.Controls.TabControl)window.FindName("SettingsTabs")).Items.Count == 2, "Editor and settings expose requested subtabs");
            var menu = ((System.Windows.Controls.ListBox)window.FindName("EntriesList")).ContextMenu;
            Check(menu.Items.OfType<System.Windows.Controls.MenuItem>().Select(x => x.Header.ToString()).SequenceEqual(new[] { "打开", "编辑", "删除" }), "List context menu exposes open edit delete");
            Check(Store.Variable == "XianTrace_UAC_ToolBox" && DesktopIntegration.InspectEnvironment().Message.Length > 0 && DesktopIntegration.InspectMenus().Message.Length > 0,
                "New environment key and read-only system status detection");
            window.Close(); app.Shutdown();
            Console.WriteLine($"{count} Windows checks passed");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
            Environment.SetEnvironmentVariable(Store.Variable, previous);
            Directory.Delete(folder, true);
        }
    }
    static void TestConfigurationValidation(string executable)
    {
        var entry = new LaunchEntry { ShortcutName = "test", ExecutablePath = executable, WorkingDirectory = Environment.SystemDirectory };
        var config = new Configuration { OwnerSid = Access.Sid, Entries = [entry] };
        ConfigurationWriter.Validate(config);
        Check(true, "Validate configuration without elevation or writing");
        try { ConfigurationWriter.Validate(config with { OwnerSid = "other" }); throw new Exception("Wrong owner accepted"); }
        catch (InvalidDataException) { Check(true, "Configuration writer rejects wrong owner"); }
        try { ConfigurationWriter.Validate(config with { Entries = [entry with { ExecutablePath = Store.LauncherPath }] }); throw new Exception("Self target accepted"); }
        catch (InvalidDataException) { Check(true, "Configuration writer rejects launcher as target"); }
    }
    static async Task TestConfigurationCaller()
    {
        string name = "LaunchManager-config-caller-test-" + Guid.NewGuid();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var server = new System.IO.Pipes.NamedPipeServerStream(name, System.IO.Pipes.PipeDirection.InOut, 1,
            System.IO.Pipes.PipeTransmissionMode.Byte, System.IO.Pipes.PipeOptions.Asynchronous);
        using var client = new System.IO.Pipes.NamedPipeClientStream(".", name, System.IO.Pipes.PipeDirection.InOut,
            System.IO.Pipes.PipeOptions.Asynchronous, System.Security.Principal.TokenImpersonationLevel.Identification);
        var accepting = server.WaitForConnectionAsync(deadline.Token);
        await client.ConnectAsync(deadline.Token); await accepting;
        var reading = Wire.ReadAsync<LaunchRequest>(server, deadline.Token);
        await Wire.WriteAsync(client, new LaunchRequest(2, Guid.NewGuid(), Guid.Empty, "save-configuration"), deadline.Token);
        await reading;
        try { PipeSecurity.VerifyConfigurationClient(server); throw new Exception("Unrelated test executable accepted as configuration UI"); }
        catch (UnauthorizedAccessException ex)
        {
            Check(ex.Message.Contains("本安装的配置界面"), "Reject save caller outside installed configuration executable");
        }
    }
    static void TestTaskPolicy()
    {
        string sid = "S-1-5-21-100-200-300-1001";
        string secure = $"O:BAG:BAD:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GRGX;;;{sid})";
        string xml = $"""
        <Task xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <RegistrationInfo><Description>{TaskPolicy.Marker}</Description></RegistrationInfo>
          <Principals><Principal><UserId>{sid}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>
          <Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><Enabled>true</Enabled><AllowStartOnDemand>true</AllowStartOnDemand></Settings>
          <Actions><Exec><Command>C:\Tools\Launcher.exe</Command><Arguments>broker</Arguments><WorkingDirectory>C:\Tools</WorkingDirectory></Exec></Actions>
        </Task>
        """;
        InstallationState Inspect(string text, string? acl = null) => TaskPolicy.Inspect(text, acl ?? secure, sid, @"C:\Tools\Launcher.exe", @"C:\Tools").State;
        Check(Inspect(xml) == InstallationState.Ready, "Accept expected task policy");
        Check(Inspect(xml.Replace("HighestAvailable", "LeastPrivilege")) == InstallationState.NeedsRepair, "Reject wrong task elevation");
        Check(Inspect(xml.Replace(sid, "S-1-5-18")) == InstallationState.NeedsRepair, "Reject wrong task user");
        Check(Inspect(xml.Replace("Launcher.exe", "Other.exe")) == InstallationState.NeedsRepair, "Reject changed task executable");
        Check(Inspect(xml.Replace("<Arguments>broker", "<Arguments>launch")) == InstallationState.NeedsRepair, "Reject changed task arguments");
        Check(Inspect(xml.Replace("<Enabled>true", "<Enabled>false")) == InstallationState.NeedsRepair, "Reject disabled task");
        Check(Inspect(xml.Replace("<Settings>", "<Triggers><LogonTrigger/></Triggers><Settings>")) == InstallationState.NeedsRepair, "Reject automatic triggers");
        Check(Inspect(xml.Replace(TaskPolicy.Marker, "Other task")) == InstallationState.Conflict, "Detect task ownership conflict");
        Check(Inspect(xml, secure.Replace("GRGX", "GA")) == InstallationState.NeedsRepair, "Reject writable task ACL");
        Check(Inspect(xml, secure + "(A;;GRGX;;;WD)") == InstallationState.NeedsRepair, "Reject unrelated task readers");
        Check(Inspect(xml, secure.Replace("O:BA", "O:" + sid)) == InstallationState.NeedsRepair, "Reject user-owned task descriptor");
        Check(Inspect(xml, secure.Replace("GRGX", "0x1200a9")) == InstallationState.Ready, "Accept expanded read and execute rights");
    }

    static void TestShortcutOutput(string folder, LaunchEntry entry)
    {
        Check(ShortcutOutput.DefaultName(@"C:\Games\示例.exe") == "示例_UACTB" &&
            ShortcutOutput.DefaultName(@"C:\Links\示例_UACTB.lnk") == "示例_UACTB", "Default shortcut suffix is appended only once");
        Check(ShortcutOutput.NormalizeName("示例.lnk") == "示例", "Optional link extension is not duplicated");
        foreach (string invalid in new[] { "", "../outside", @"C:\other", "CON", "con.txt", "LPT1", "COM¹", "尾部.", "bad:name", new string('x', 252) })
        {
            try { ShortcutOutput.NormalizeName(invalid); throw new Exception("Invalid filename accepted: " + invalid); }
            catch (InvalidDataException) { }
        }
        Check(true, "Reject traversal, reserved names and invalid shortcut filenames");
        Check(ShortcutOutput.GetPath(entry.ShortcutName, "") == Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), entry.ShortcutName + ".lnk"),
            "Desktop destination uses current user desktop");
        string custom = Path.Combine(folder, "自定义 位置"); Directory.CreateDirectory(custom);
        string output = ShortcutOutput.GetPath(entry.ShortcutName, custom);
        Shortcuts.Create(output, entry);
        Check(File.Exists(output) && Shortcuts.Import(output).ArgumentsRaw == "launch " + entry.Id,
            "Create named shortcut in custom Unicode folder with original entry ID");
        try { ShortcutOutput.GetPath(entry.ShortcutName, Path.Combine(folder, "missing")); throw new Exception("Missing folder accepted"); }
        catch (DirectoryNotFoundException) { Check(true, "Reject unavailable shortcut destination"); }
    }
    static void TestShellLaunch(string folder, LaunchEntry entry)
    {
        var probeFolder = Path.Combine(folder, "路径 带空格"); Directory.CreateDirectory(probeFolder);
        foreach (var file in Directory.GetFiles(AppContext.BaseDirectory))
            File.Copy(file, Path.Combine(probeFolder, Path.GetFileName(file)));
        string probe = Path.Combine(probeFolder, "WindowsIntegration.Tests.exe");
        string link = Path.Combine(folder, "空格启动.lnk");
        string result = Path.Combine(probeFolder, "probe-result.txt");
        string? old = Environment.GetEnvironmentVariable(Store.Variable);
        try
        {
            Environment.SetEnvironmentVariable(Store.Variable, probe);
            Shortcuts.Create(link, entry, probe);
            foreach (bool unset in new[] { false, true })
            {
                if (unset) Environment.SetEnvironmentVariable(Store.Variable, null);
                File.Delete(result);
                using var child = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(link) { UseShellExecute = true, WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden });
                if (child is not null && !child.WaitForExit(10000)) throw new Exception("Shortcut probe timed out");
                Check(SpinWait.SpinUntil(() => File.Exists(result), 5000) && File.ReadAllText(result) == entry.Id.ToString(),
                    unset ? "Shell shortcut starts with missing inherited variable" : "Shell shortcut starts spaced Unicode path with exact ID");
            }
            string moved = Path.Combine(folder, "迁移 后位置");
            Directory.Move(probeFolder, moved);
            Environment.SetEnvironmentVariable(Store.Variable, Path.Combine(moved, "WindowsIntegration.Tests.exe"));
            result = Path.Combine(moved, "probe-result.txt"); File.Delete(result);
            using var movedChild = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(link) { UseShellExecute = true, WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden });
            if (movedChild is not null && !movedChild.WaitForExit(10000)) throw new Exception("Moved shortcut timed out");
            Check(SpinWait.SpinUntil(() => File.Exists(result), 5000) && File.ReadAllText(result) == entry.Id.ToString(), "Shell shortcut follows environment after original directory moved");
        }
        finally { Environment.SetEnvironmentVariable(Store.Variable, old); }
    }
    static async Task TestDelivery()
    {
        string name = "LaunchManager-delivery-test-" + Guid.NewGuid();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var server = new System.IO.Pipes.NamedPipeServerStream(name, System.IO.Pipes.PipeDirection.InOut, 1,
            System.IO.Pipes.PipeTransmissionMode.Byte, System.IO.Pipes.PipeOptions.Asynchronous);
        using var client = new System.IO.Pipes.NamedPipeClientStream(".", name, System.IO.Pipes.PipeDirection.InOut, System.IO.Pipes.PipeOptions.Asynchronous);
        var accepting = server.WaitForConnectionAsync(deadline.Token);
        await client.ConnectAsync(deadline.Token); await accepting;
        for (int i = 0; i < 20; i++)
        {
            var expected = new LaunchResponse(Guid.NewGuid(), ResultCode.Success, "started", 123);
            var send = ResponseDelivery.SendAsync(server, expected, deadline.Token);
            await Task.Delay(20, deadline.Token); // Deliberately delay consuming buffered response.
            if (send.IsCompleted) throw new Exception("Server returned before receipt");
            var actual = await ResponseDelivery.ReceiveAsync(client, expected.RequestId, deadline.Token);
            await send;
            if (actual != expected) throw new Exception("Response was lost");
        }
        server.Disconnect();
        Check(true, "20 delayed pipe responses acknowledged before disconnect");
    }
    static async Task TestActivation()
    {
        string name = "LaunchManager-activation-test-" + Guid.NewGuid();
        using var server = new ManagerActivation(name);
        var received = new System.Collections.Concurrent.ConcurrentQueue<string[]>();
        var listening = server.ListenAsync(received.Enqueue);
        await ManagerActivation.ForwardAsync(name, []);
        await ManagerActivation.ForwardAsync(name, ["import", @"C:\路径 空格\item.lnk"]);
        Check(received.Count == 2 && received.Last().SequenceEqual(new[] { "import", @"C:\路径 空格\item.lnk" }), "Manager activation forwards open and Unicode import requests");
        server.Dispose(); await listening;
    }

}

