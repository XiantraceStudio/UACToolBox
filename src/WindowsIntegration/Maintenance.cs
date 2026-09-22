using System.Diagnostics;
using UACToolBox.Localization;
namespace UACToolBox.WindowsIntegration;
public static class Maintenance
{
    public static bool IsSupported(string operation) => operation is "register" or "install" or "uninstall" or "environment-add" or "environment-remove" or "menus-add" or "menus-remove";
    public static void Perform(string operation, string owner)
    {
        Access.RequireAdmin();
        if (Access.Sid != owner) throw new UnauthorizedAccessException(Loc.T("err.ownerMismatch"));
        switch (operation)
        {
            case "register": Scheduler.Install(); DesktopIntegration.SetEnvironment(true); DesktopIntegration.SetMenus(true); break;
            case "install": Scheduler.Install(); break;
            case "uninstall": Scheduler.Uninstall(); Store.ClearRegistration(); break;
            case "environment-add": EnsureProtectedRuntime(); DesktopIntegration.SetEnvironment(true); break;
            case "environment-remove": DesktopIntegration.SetEnvironment(false); break;
            case "menus-add": EnsureProtectedRuntime(); DesktopIntegration.SetMenus(true); break;
            case "menus-remove": DesktopIntegration.SetMenus(false); break;
            default: throw new ArgumentException(Loc.T("err.opUnsupported"));
        }
    }
    /// <summary>
    /// 提权执行端固定部署在受保护的系统位置（ProgramData 配置根目录），与安装目录无关，
    /// 安装目录可任意选择（含用户可写位置）。保存配置的调用方核验改为注册表记录的
    /// 配置界面路径 + SHA-256：界面文件被替换即拒绝，无需安装目录受保护。
    /// </summary>
    static void EnsureProtectedRuntime()
    {
        if (!File.Exists(Store.LauncherPath)) throw new FileNotFoundException(Loc.T("err.brokerNotDeployed"), Store.LauncherPath);
        Access.ProtectedPath(Store.LauncherPath);
        Access.ProtectedPath(Store.Root, true);
    }
    public static async Task RunAsync(string operation)
    {
        if (!IsSupported(operation)) throw new ArgumentException(Loc.T("err.opUnsupported"));
        if (Access.IsAdmin) { Perform(operation, Access.Sid); return; }
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "Config.exe")) { UseShellExecute = true, Verb = "runas" };
        start.ArgumentList.Add("--admin-operation"); start.ArgumentList.Add(operation); start.ArgumentList.Add(Access.Sid);
        using var process = Process.Start(start) ?? throw new InvalidOperationException(Loc.T("err.cantElevate"));
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new InvalidOperationException(Loc.T("err.opAborted"));
    }
}
