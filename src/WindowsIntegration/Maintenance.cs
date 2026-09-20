using System.Diagnostics;
namespace UACToolBox.WindowsIntegration;
public static class Maintenance
{
    public static bool IsSupported(string operation) => operation is "register" or "install" or "uninstall" or "environment-add" or "environment-remove" or "menus-add" or "menus-remove";
    public static void Perform(string operation, string owner)
    {
        Access.RequireAdmin();
        if (Access.Sid != owner) throw new UnauthorizedAccessException("请使用当前用户的管理员权限，不能使用其他账户凭据。");
        switch (operation)
        {
            case "register": Access.ProtectedPath(Store.LauncherPath); Access.ProtectedPath(AppContext.BaseDirectory, true); Scheduler.Install(); DesktopIntegration.SetEnvironment(true); DesktopIntegration.SetMenus(true); break;
            case "install": Scheduler.Install(); break;
            case "uninstall": Scheduler.Uninstall(); break;
            case "environment-add": Access.ProtectedPath(Store.LauncherPath); DesktopIntegration.SetEnvironment(true); break;
            case "environment-remove": DesktopIntegration.SetEnvironment(false); break;
            case "menus-add": Access.ProtectedPath(AppContext.BaseDirectory, true); DesktopIntegration.SetMenus(true); break;
            case "menus-remove": DesktopIntegration.SetMenus(false); break;
            default: throw new ArgumentException("不支持的系统设置操作。");
        }
    }
    public static async Task RunAsync(string operation)
    {
        if (!IsSupported(operation)) throw new ArgumentException("不支持的系统设置操作。");
        if (Access.IsAdmin) { Perform(operation, Access.Sid); return; }
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "Config.exe")) { UseShellExecute = true, Verb = "runas" };
        start.ArgumentList.Add("--admin-operation"); start.ArgumentList.Add(operation); start.ArgumentList.Add(Access.Sid);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("未能打开系统设置授权。");
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new InvalidOperationException("系统设置未完成，请查看管理员操作提示。");
    }
}
