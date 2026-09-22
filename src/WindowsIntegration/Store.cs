using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using UACToolBox.Contracts;
using UACToolBox.Localization;
namespace UACToolBox.WindowsIntegration;
public static class Store
{
    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "XianTrace", "UACToolBox");
    public static string FilePath => Path.Combine(Root, Access.Sid + ".json");
    /// <summary>运行时执行端固定部署在受保护的配置根目录，与安装位置无关。</summary>
    public static string LauncherPath => Path.Combine(Root, "Launcher.exe");
    /// <summary>安装目录中的执行端来源副本，注册时复制到 LauncherPath。</summary>
    public static string LauncherSource => Path.Combine(AppContext.BaseDirectory, "Launcher.exe");
    public static string Variable => "XianTrace_UAC_ToolBox";
    const string RegistrationKeyName = @"SOFTWARE\XianTrace\UACToolBox";
    /// <summary>注册时记录的配置界面路径与 SHA-256（仅管理员可写），供执行端核验保存请求调用方。</summary>
    public static (string? Path, string? Hash) RegisteredConfigExe()
    {
        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(RegistrationKeyName);
        return ((string?)key?.GetValue("ConfigPath"), (string?)key?.GetValue("ConfigHash"));
    }
    public static void RecordConfigExe(string path)
    {
        Access.RequireAdmin();
        using var key = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(RegistrationKeyName);
        using var sha = System.Security.Cryptography.SHA256.Create();
        key.SetValue("ConfigPath", Path.GetFullPath(path));
        key.SetValue("ConfigHash", Convert.ToHexString(sha.ComputeHash(File.ReadAllBytes(path))));
    }
    public static void ClearRegistration()
    {
        Access.RequireAdmin();
        Microsoft.Win32.Registry.LocalMachine.DeleteSubKey(RegistrationKeyName, false);
    }
    public static void Ensure()
    {
        Access.RequireAdmin();
        if (!Directory.Exists(Root))
        {
            var acl = new DirectorySecurity();
            acl.SetAccessRuleProtection(true, false);
            acl.SetOwner(new SecurityIdentifier("S-1-5-32-544"));
            foreach (var sid in new[] { "S-1-5-18", "S-1-5-32-544" })
                acl.AddAccessRule(new(new SecurityIdentifier(sid), FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            acl.AddAccessRule(new(new SecurityIdentifier("S-1-5-32-545"), FileSystemRights.ReadAndExecute, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(Root).Create(acl);
        }
        Access.ProtectedPath(Root, true);
    }
    public static Configuration Load()
    {
        if (!File.Exists(FilePath)) return new() { OwnerSid = Access.Sid };
        Access.ProtectedPath(FilePath);
        if (new FileInfo(FilePath).Length > Configuration.MaxStorageBytes) throw new InvalidDataException(Loc.T("err.configTooLarge"));
        var result = JsonSerializer.Deserialize<Configuration>(File.ReadAllText(FilePath), JsonDefaults.Options) ?? throw new InvalidDataException(Loc.T("err.configEmpty"));
        result.Validate(Access.Sid);
        return result;
    }
    public static void Save(Configuration config)
    {
        Ensure(); config.Validate(Access.Sid);
        using var writeLock = new FileStream(Path.Combine(Root, Access.Sid + ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (Load().Revision != config.Revision)
            throw new InvalidOperationException(Loc.T("err.revisionConflict"));
        var bytes = (config with { Revision = checked(config.Revision + 1) }).SerializeForStorage(Access.Sid);
        var temp = Path.Combine(Root, Guid.NewGuid() + ".tmp");
        try
        {
            File.WriteAllBytes(temp, bytes);
            // Explicit owner avoids granting the unelevated user implicit WRITE_DAC.
            var info = new FileInfo(temp); var acl = info.GetAccessControl();
            acl.SetOwner(new SecurityIdentifier("S-1-5-32-544")); info.SetAccessControl(acl);
            File.Move(temp, FilePath, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
