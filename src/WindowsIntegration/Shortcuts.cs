using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using UACToolBox.Contracts;
namespace UACToolBox.WindowsIntegration;

public static class Shortcuts
{
    public static LaunchEntry Import(string path)
    {
        path = Path.GetFullPath(path);
        if (!File.Exists(path)) throw new FileNotFoundException(UACToolBox.Localization.Loc.T("err.fileMissing"), path);
        if (Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            return new() { ShortcutName = ShortcutOutput.DefaultName(path), ExecutablePath = path,
                WorkingDirectory = Path.GetDirectoryName(path)!, IconPath = path };
        if (!Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(UACToolBox.Localization.Loc.T("err.pickExeOrLnk"));
        var link = (IShellLinkW)new ShellLink();
        try
        {
            ((IPersistFile)link).Load(path, 0);
            var target = new StringBuilder(32768); var args = new StringBuilder(32768);
            var work = new StringBuilder(32768); var icon = new StringBuilder(32768);
            link.GetPath(target, target.Capacity, IntPtr.Zero, 4); // raw path; no Resolve/network access
            link.GetArguments(args, args.Capacity); link.GetWorkingDirectory(work, work.Capacity);
            link.GetIconLocation(icon, icon.Capacity, out int index); link.GetShowCmd(out int show);
            string exe = Environment.ExpandEnvironmentVariables(target.ToString());
            if (!Path.IsPathFullyQualified(exe) || !Path.GetExtension(exe).Equals(".exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(UACToolBox.Localization.Loc.T("err.lnkNotLocalExe"));
            return new() { ShortcutName = ShortcutOutput.DefaultName(path), ExecutablePath = exe,
                ArgumentsRaw = args.ToString(), WorkingDirectory = work.Length == 0 ? Path.GetDirectoryName(exe)! : Environment.ExpandEnvironmentVariables(work.ToString()),
                IconPath = icon.Length == 0 ? exe : Environment.ExpandEnvironmentVariables(icon.ToString()), IconIndex = index,
                ShowMode = show is 3 or 7 ? show : 1, SourceShortcut = path };
        }
        finally { Marshal.FinalReleaseComObject(link); }
    }
    public static void Create(string output, LaunchEntry entry, string? launcherPath = null)
    {
        launcherPath ??= Store.LauncherPath;
        var link = (IShellLinkW)new ShellLink();
        try
        {
            link.SetPath(launcherPath); link.SetArguments("launch " + entry.Id);
            link.SetDescription(entry.ShortcutName); link.SetWorkingDirectory(Path.GetDirectoryName(launcherPath)!);
            link.SetIconLocation(entry.IconPath, entry.IconIndex); link.SetShowCmd(1);
            var data = (IShellLinkDataList)link;
            // EXP_SZ_LINK: 8-byte header, MAX_PATH ANSI, MAX_PATH UTF-16.
            IntPtr block = LocalAlloc(0x40, 788);
            if (block == IntPtr.Zero) throw new OutOfMemoryException();
            try
            {
                Marshal.WriteInt32(block, 788); Marshal.WriteInt32(block, 4, unchecked((int)0xA0000001));
                var text = "%" + Store.Variable + "%";
                var ansi = Encoding.ASCII.GetBytes(text + '\0'); var wide = Encoding.Unicode.GetBytes(text + '\0');
                Marshal.Copy(ansi, 0, block + 8, ansi.Length); Marshal.Copy(wide, 0, block + 268, wide.Length);
                data.AddDataBlock(block); block = IntPtr.Zero; // COM owns LocalAlloc block after success.
                data.GetFlags(out uint flags); data.SetFlags((flags | 0x200) & ~0x02000000u); // Retain absolute fallback when the caller has a stale environment.
                ((IPersistFile)link).Save(output, true);
            }
            finally { if (block != IntPtr.Zero) LocalFree(block); }
        }
        finally { Marshal.FinalReleaseComObject(link); }
    }
    [DllImport("kernel32.dll")] static extern IntPtr LocalAlloc(uint flags, nuint bytes);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr memory);
}
[ComImport, Guid("00021401-0000-0000-C000-000000000046")] class ShellLink { }
[ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IShellLinkW
{
    void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int size, IntPtr findData, uint flags);
    void GetIDList(out IntPtr pidl); void SetIDList(IntPtr pidl);
    void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int size);
    void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string text);
    void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int size);
    void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string text);
    void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int size);
    void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string text);
    void GetHotkey(out short key); void SetHotkey(short key);
    void GetShowCmd(out int show); void SetShowCmd(int show);
    void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int size, out int index);
    void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
    void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
    void Resolve(IntPtr window, uint flags);
    void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
}
[ComImport, Guid("45E2B4AE-B1C3-11D0-B92F-00A0C90312E1"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IShellLinkDataList
{
    void AddDataBlock(IntPtr block); void CopyDataBlock(uint signature, out IntPtr block);
    void RemoveDataBlock(uint signature); void GetFlags(out uint flags); void SetFlags(uint flags);
}
