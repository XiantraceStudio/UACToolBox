using Microsoft.Win32;
using System.Runtime.InteropServices;
namespace UACToolBox.WindowsIntegration;
public static class DesktopIntegration
{
    const string Marker = "LaunchManager.v1";
    static IEnumerable<string> Keys()
    {
        foreach (var type in new[] { "exefile", "lnkfile" })
            foreach (var verb in new[] { "LaunchManagerImport", "LaunchManagerRun" })
                yield return @"Software\Classes\" + type + @"\shell\" + verb;
    }
    public static void SetMenus(bool enable)
    {
        Access.RequireAdmin();
        foreach (var path in Keys())
        {
            using var existing = Registry.LocalMachine.OpenSubKey(path);
            if (existing is not null && (string?)existing.GetValue("Owner") != Marker)
                throw new InvalidOperationException("同名右键菜单不属于本工具，未修改。");
        }
        foreach (var path in Keys())
        {
            if (!enable) { Registry.LocalMachine.DeleteSubKeyTree(path, false); continue; }
            bool import = path.EndsWith("Import");
            using var key = Registry.LocalMachine.CreateSubKey(path);
            key.SetValue("Owner", Marker);
            key.SetValue("MUIVerb", import ? "添加到 UACToolBox…" : "通过 UACToolBox 运行");
            key.SetValue("Icon", "\"" + Store.LauncherPath + "\",0");
            using var cmd = key.CreateSubKey("command");
            string exe = import ? Path.Combine(AppContext.BaseDirectory, "Config.exe") : Store.LauncherPath;
            cmd.SetValue("", "\"" + exe + "\" " + (import ? "import" : "resolve") + " \"%1\"");
        }
    }
    public static (bool Ready, string Message) InspectEnvironment()
    {
        var machine = Environment.GetEnvironmentVariable(Store.Variable, EnvironmentVariableTarget.Machine);
        var user = Environment.GetEnvironmentVariable(Store.Variable, EnvironmentVariableTarget.User);
        var effective = string.IsNullOrEmpty(user) ? machine : user;
        bool ready = string.Equals(effective, Store.LauncherPath, StringComparison.OrdinalIgnoreCase);
        return (ready, string.IsNullOrEmpty(effective) ? "未配置" : (ready ? "正常" : "指向其他位置") + "\n" + effective);
    }
    public static (bool Ready, string Message) InspectMenus()
    {
        int present = 0;
        foreach (var path in Keys())
        {
            using var key = Registry.LocalMachine.OpenSubKey(path);
            if (key is null) continue;
            if ((string?)key.GetValue("Owner") != Marker) return (false, "存在同名冲突");
            using var command = key.OpenSubKey("command");
            bool import = path.EndsWith("Import");
            string exe = import ? Path.Combine(AppContext.BaseDirectory, "Config.exe") : Store.LauncherPath;
            string expected = "\"" + exe + "\" " + (import ? "import" : "resolve") + " \"%1\"";
            if (!string.Equals((string?)command?.GetValue(""), expected, StringComparison.OrdinalIgnoreCase)) return (false, "需要修复");
            present++;
        }
        return present == 4 ? (true, "正常") : present == 0 ? (false, "未注册") : (false, "注册不完整");
    }
    public static void SetEnvironment(bool enable)
    {
        Access.RequireAdmin();
        Environment.SetEnvironmentVariable(Store.Variable, enable ? Store.LauncherPath : null, EnvironmentVariableTarget.Machine);
        Environment.SetEnvironmentVariable(Store.Variable, enable ? Store.LauncherPath : null);
        SendMessageTimeout((IntPtr)0xffff, 0x1A, IntPtr.Zero, "Environment", 2, 3000, out _);
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, IntPtr wParam, string text, uint flags, uint timeout, out IntPtr result);
}
