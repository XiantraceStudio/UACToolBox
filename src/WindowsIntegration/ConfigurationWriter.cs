using UACToolBox.Contracts;
namespace UACToolBox.WindowsIntegration;
public static class ConfigurationWriter
{
    public static void Validate(Configuration config)
    {
        config.SerializeForStorage(Access.Sid);
        foreach (var entry in config.Entries)
        {
            if (string.Equals(Path.GetFullPath(entry.ExecutablePath), Store.LauncherPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("不能将启动器本身作为目标。");
            if (entry.Enabled) Access.ValidateTarget(entry.ExecutablePath, entry.WorkingDirectory);
        }
    }
    public static void Save(Configuration config)
    {
        Access.RequireAdmin();
        Validate(config);
        Store.Save(config);
    }
}
