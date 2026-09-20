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
            case "register": EnsureProtectedInstall(); Scheduler.Install(); DesktopIntegration.SetEnvironment(true); DesktopIntegration.SetMenus(true); break;
            case "install": EnsureProtectedInstall(); Scheduler.Install(); break;
            case "uninstall": Scheduler.Uninstall(); break;
            case "environment-add": EnsureProtectedInstall(); DesktopIntegration.SetEnvironment(true); break;
            case "environment-remove": DesktopIntegration.SetEnvironment(false); break;
            case "menus-add": EnsureProtectedInstall(); DesktopIntegration.SetMenus(true); break;
            case "menus-remove": DesktopIntegration.SetMenus(false); break;
            default: throw new ArgumentException("不支持的系统设置操作。");
        }
    }
    /// <summary>
    /// 校验安装位置是否受保护；不达标时先自动加固：安装目录本身完全加固（所有者改管理员、
    /// 切断继承、普通用户只读），父目录链（含盘根）仅移除对文件夹本身生效的危险权限、
    /// 修正所有者——可继承授权保持不变，盘上其他内容行为不受影响。仍不达标时抛出的异常指出确切路径。
    /// </summary>
    static void EnsureProtectedInstall()
    {
        try { Access.ProtectedPath(Store.LauncherPath); Access.ProtectedPath(AppContext.BaseDirectory, true); }
        catch (UnauthorizedAccessException)
        {
            Access.HardenPath(AppContext.BaseDirectory, true);
            Access.HardenAncestors(AppContext.BaseDirectory);
            Access.ProtectedPath(Store.LauncherPath);
            Access.ProtectedPath(AppContext.BaseDirectory, true);
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
