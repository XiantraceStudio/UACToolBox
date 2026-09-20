using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
namespace UACToolBox.WindowsIntegration;
public static class Access
{
    public static string Sid => WindowsIdentity.GetCurrent().User!.Value;
    public static bool IsAdmin => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
    static bool Trusted(SecurityIdentifier sid) => sid.Value is "S-1-5-18" or "S-1-5-32-544" or "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464";
    public static void RequireAdmin()
    {
        if (!IsAdmin) throw new UnauthorizedAccessException("此操作需要管理员权限。");
        if (Process.GetCurrentProcess().SessionId == 0) throw new UnauthorizedAccessException("仅支持交互式用户会话。");
    }
    public static void ProtectedPath(string path, bool directory = false)
    {
        path = Path.GetFullPath(path);
        if (path.StartsWith(@"\") || path.Length < 3 || path[1] != ':' || path[2] != '\\' || path[3..].Contains(':'))
            throw new InvalidDataException("仅支持本地磁盘路径。");
        FileSystemInfo? item = directory ? new DirectoryInfo(path) : new FileInfo(path);
        bool first = true;
        while (item is not null)
        {
            if (!item.Exists || (item.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException("路径不存在或包含重解析点：" + item.FullName);
            FileSystemSecurity acl = item is DirectoryInfo d ? d.GetAccessControl() : ((FileInfo)item).GetAccessControl();
            if (acl.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner || !Trusted(owner))
                throw new UnauthorizedAccessException("路径所有者必须为管理员或系统：" + item.FullName);
            var dangerous = FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership | FileSystemRights.Delete;
            if (item is DirectoryInfo) dangerous |= FileSystemRights.DeleteSubdirectoriesAndFiles;
            if (first) dangerous |= FileSystemRights.WriteData | FileSystemRights.AppendData;
            foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, true, typeof(SecurityIdentifier)))
                if (rule.AccessControlType == AccessControlType.Allow &&
                    (rule.PropagationFlags & PropagationFlags.InheritOnly) == 0 &&
                    (rule.FileSystemRights & dangerous) != 0 && !Trusted((SecurityIdentifier)rule.IdentityReference))
                    throw new UnauthorizedAccessException("普通权限可修改此路径，请使用受保护的安装位置：" + item.FullName);
            first = false;
            item = item is DirectoryInfo dir ? dir.Parent : ((FileInfo)item).Directory;
        }
    }
    public static void ValidateTarget(string exe, string working)
    {
        if (!string.Equals(Path.GetExtension(exe), ".exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("第一版仅支持本机 EXE。");
        ValidateLocalTargetPath(exe, false);
        ValidateLocalTargetPath(working, true);
    }
    /// <summary>
    /// 将目录或文件加固为受保护位置：所有者改为管理员、切断 ACL 继承、
    /// 仅 SYSTEM 与管理员完全控制、普通用户只读。需要管理员权限。
    /// 只处理传入的路径本身（及其直接内容），不改动其父目录链。
    /// </summary>
    public static void HardenPath(string path, bool directory)
    {
        RequireAdmin();
        path = Path.GetFullPath(path);
        FileSystemSecurity acl = BuildProtectedAcl(directory);
        if (directory)
        {
            new DirectoryInfo(path).SetAccessControl((DirectorySecurity)acl);
            foreach (var child in Directory.EnumerateDirectories(path)) HardenPath(child, true);
            foreach (var file in Directory.EnumerateFiles(path)) HardenPath(file, false);
        }
        else new FileInfo(path).SetAccessControl((FileSecurity)acl);
    }
    /// <summary>构造受保护位置的 ACL 模板；文件不允许携带继承标志，仅目录可继承到内容。</summary>
    public static FileSystemSecurity BuildProtectedAcl(bool directory)
    {
        FileSystemSecurity acl = directory ? new DirectorySecurity() : new FileSecurity();
        acl.SetAccessRuleProtection(true, false);
        acl.SetOwner(new SecurityIdentifier("S-1-5-32-544"));
        InheritanceFlags inheritance = directory ? InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit : InheritanceFlags.None;
        foreach (var sid in new[] { "S-1-5-18", "S-1-5-32-544" })
            acl.AddAccessRule(new(new SecurityIdentifier(sid), FileSystemRights.FullControl, inheritance, PropagationFlags.None, AccessControlType.Allow));
        acl.AddAccessRule(new(new SecurityIdentifier("S-1-5-32-545"), FileSystemRights.ReadAndExecute, inheritance, PropagationFlags.None, AccessControlType.Allow));
        return acl;
    }
    /// <summary>
    /// 收紧指定目录的全部父目录（含盘根）：所有者不为受信任主体时改为管理员；
    /// 对文件夹本身生效的普通用户授权移除删除 / 改写 ACL 等危险权限。
    /// 可继承到其他子目录与既有内容的授权保持不变——除文件夹本身的改名 / 删除需提权外，
    /// 盘上其他内容行为不变。需要管理员权限。
    /// </summary>
    public static void HardenAncestors(string directoryPath)
    {
        RequireAdmin();
        for (var directory = new DirectoryInfo(Path.GetFullPath(directoryPath)).Parent; directory is not null; directory = directory.Parent)
            TrimDangerousRights(directory);
    }
    static void TrimDangerousRights(DirectoryInfo directory)
    {
        DirectorySecurity acl = directory.GetAccessControl();
        // 断开继承并复制现有规则，使修改作用于本文件夹的显式副本；可继承规则原样保留。
        acl.SetAccessRuleProtection(true, true);
        if (acl.GetOwner(typeof(SecurityIdentifier)) is SecurityIdentifier owner && !Trusted(owner))
            acl.SetOwner(new SecurityIdentifier("S-1-5-32-544"));
        foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType != AccessControlType.Allow || (rule.PropagationFlags & PropagationFlags.InheritOnly) != 0 || Trusted((SecurityIdentifier)rule.IdentityReference)) continue;
            var dangerous = FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership | FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles;
            if ((rule.FileSystemRights & dangerous) == 0) continue;
            acl.RemoveAccessRuleSpecific(rule);
            var reduced = rule.FileSystemRights & ~dangerous;
            if (reduced != 0)
                acl.AddAccessRule(new FileSystemAccessRule(rule.IdentityReference, reduced, rule.InheritanceFlags, rule.PropagationFlags, AccessControlType.Allow));
        }
        directory.SetAccessControl(acl);
    }
    static void ValidateLocalTargetPath(string path, bool directory)
    {
        if (!Path.IsPathFullyQualified(path)) throw new InvalidDataException("目标和工作目录必须使用绝对路径。");
        path = Path.GetFullPath(path);
        if (path.Length < 3 || !char.IsAsciiLetter(path[0]) || path[1] != ':' || path[2] != Path.DirectorySeparatorChar || path[3..].Contains(':'))
            throw new InvalidDataException("仅支持本地磁盘路径。");
        FileSystemInfo? item = directory ? new DirectoryInfo(path) : new FileInfo(path);
        while (item is not null)
        {
            if (!item.Exists) throw new FileNotFoundException("目标或工作目录不存在。", item.FullName);
            if ((item.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("暂不支持包含重解析点的目标路径：" + item.FullName);
            item = item is DirectoryInfo dir ? dir.Parent : ((FileInfo)item).Directory;
        }
    }
}
