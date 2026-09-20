using System.Diagnostics;
using System.Windows;
using UACToolBox.WindowsIntegration;
namespace UACToolBox.Config;
public partial class App : Application
{
    Mutex? instance;
    bool owns;
    ManagerActivation? activation;
    readonly Queue<string[]> pending = new();
    bool processing;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        if ((e.Args.Length == 3 && e.Args[0] == "--admin-operation") || (e.Args.Length == 2 && e.Args[0] == "--admin-operation-self"))
        {
            // 安装器等已提权调用方使用 self 形式，以自身账户为授权对象。
            string owner = e.Args[0] == "--admin-operation-self" ? Access.Sid : e.Args[2];
            try { Maintenance.Perform(e.Args[1], owner); Shutdown(); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "系统设置未完成"); Shutdown(1); }
            return;
        }
        string name = $"LaunchManager-Config-{Access.Sid}-{Process.GetCurrentProcess().SessionId}";
        instance = new Mutex(false, @"Local\" + name);
        try
        {
            try { owns = instance.WaitOne(0); } catch (AbandonedMutexException) { owns = true; }
            if (!owns)
            {
                await ManagerActivation.ForwardAsync(name, e.Args);
                Shutdown(); return;
            }
            var window = new MainWindow(); MainWindow = window;
            window.Loaded += (_, _) => Enqueue(window, e.Args);
            activation = new ManagerActivation(name);
            _ = Listen(window);
            window.Show();
            ShutdownMode = ShutdownMode.OnMainWindowClose;
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "无法打开配置界面"); Shutdown(1); }
    }
    async Task Listen(MainWindow window)
    {
        try { await activation!.ListenAsync(args => Dispatcher.BeginInvoke(new Action(() => Enqueue(window, args)))); }
        catch (Exception ex) { if (!Dispatcher.HasShutdownStarted) MessageBox.Show(window, ex.Message, "窗口复用连接失败"); }
    }
    void Enqueue(MainWindow window, string[] args)
    {
        pending.Enqueue(args);
        if (processing) return;
        processing = true;
        try { while (pending.TryDequeue(out var next)) window.HandleActivation(next); }
        finally { processing = false; }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        activation?.Dispose();
        if (owns) instance?.ReleaseMutex();
        instance?.Dispose(); base.OnExit(e);
    }
}
