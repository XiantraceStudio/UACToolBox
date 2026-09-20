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
            case "register": Scheduler.Install(); DesktopIntegration.SetEnvironment(true); DesktopIntegration.SetMenus(true); break;
            case "install": Scheduler.Install(); break;
            case "uninstall": Scheduler.Uninstall(); Store.ClearRegistration(); break;
            case "environment-add": EnsureProtectedRuntime(); DesktopIntegration.SetEnvironment(true); break;
            case "environment-remove": DesktopIntegration.SetEnvironment(false); break;
            case "menus-add": EnsureProtectedRuntime(); DesktopIntegration.SetMenus(true); break;
            case "menus-remove": DesktopIntegration.SetMenus(false); break;
            default: throw new ArgumentException("不支持的系统设置操作。");
        }
    }
    /// <summary>
    /// 提权执行端固定部署在受保护的系统位置（ProgramData 配置根目录），与安装目录无关，
    /// 安装目录可任意选择（含用户可写位置）。保存配置的调用方核验改为注册表记录的
    /// 配置界面路径 + SHA-256：界面文件被替换即拒绝，无需安装目录受保护。
    /// </summary>
    static void EnsureProtectedRuntime()
    {
        if (!File.Exists(Store.LauncherPath)) throw new FileNotFoundException("执行端尚未部署，请先安装计划任务。", Store.LauncherPath);
        Access.ProtectedPath(Store.LauncherPath);
        Access.ProtectedPath(Store.Root, true);
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
