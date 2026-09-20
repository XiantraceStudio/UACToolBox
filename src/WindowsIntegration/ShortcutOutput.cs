namespace UACToolBox.WindowsIntegration;
public static class ShortcutOutput
{
    public static string DefaultName(string source)
    {
        string name = Path.GetFileNameWithoutExtension(source).Trim().TrimEnd('.');
        if (string.IsNullOrEmpty(name)) return "";
        return name.EndsWith("_UACTB", StringComparison.OrdinalIgnoreCase) ? name : name + "_UACTB";
    }
    public static string NormalizeName(string name)
    {
        name = name.Trim();
        if (name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        string device = name.Split('.')[0].TrimEnd().ToUpperInvariant();
        bool reserved = device is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" ||
            device.Length == 4 && (device.StartsWith("COM") || device.StartsWith("LPT")) && "123456789¹²³".Contains(device[3]);
        if (string.IsNullOrWhiteSpace(name) || name.Length > 251 || name.EndsWith('.') || name.EndsWith(' ') ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || reserved)
            throw new InvalidDataException("快捷方式名称无效：请勿填写路径、特殊字符、系统保留名或末尾空格、句点。");
        return name;
    }
    public static string GetPath(string name, string? customDirectory)
    {
        string directory = string.IsNullOrEmpty(customDirectory)
            ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) : customDirectory;
        if (!Path.IsPathFullyQualified(directory) || !Directory.Exists(directory))
            throw new DirectoryNotFoundException("快捷方式创建位置不存在，请重新选择文件夹。");
        return Path.Combine(directory, NormalizeName(name) + ".lnk");
    }
}
