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
                throw new InvalidDataException(UACToolBox.Localization.Loc.T("err.selfTarget"));
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
