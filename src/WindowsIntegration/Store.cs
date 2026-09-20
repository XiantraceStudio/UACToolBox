using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using UACToolBox.Contracts;
namespace UACToolBox.WindowsIntegration;
public static class Store
{
    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "XianTrace", "UACToolBox");
    public static string FilePath => Path.Combine(Root, Access.Sid + ".json");
    public static string LauncherPath => Path.Combine(AppContext.BaseDirectory, "Launcher.exe");
    public static string Variable => "XianTrace_UAC_ToolBox";
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
        if (new FileInfo(FilePath).Length > Configuration.MaxStorageBytes) throw new InvalidDataException("配置过大。");
        var result = JsonSerializer.Deserialize<Configuration>(File.ReadAllText(FilePath), JsonDefaults.Options) ?? throw new InvalidDataException("配置为空。");
        result.Validate(Access.Sid);
        return result;
    }
    public static void Save(Configuration config)
    {
        Ensure(); config.Validate(Access.Sid);
        using var writeLock = new FileStream(Path.Combine(Root, Access.Sid + ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (Load().Revision != config.Revision)
            throw new InvalidOperationException("配置已被其他窗口修改，请重新打开配置界面后再保存。");
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
