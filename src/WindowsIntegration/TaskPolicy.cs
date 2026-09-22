using System.Security.AccessControl;
using System.Xml;
using System.Xml.Linq;
using UACToolBox.Localization;
namespace UACToolBox.WindowsIntegration;
public enum InstallationState { Missing, Ready, NeedsRepair, Conflict }
public sealed record InstallationStatus(InstallationState State, string Message);
public static class TaskPolicy
{
    public const string Marker = "LaunchManager managed broker v1";
    public static InstallationStatus Inspect(string xml, string security, string sid, string launcher, string directory)
    {
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        var root = XElement.Load(reader); XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        string Value(XElement? parent, string name) => parent?.Element(ns + name)?.Value ?? "";
        if (root.Name != ns + "Task" || Value(root.Element(ns + "RegistrationInfo"), "Description") != Marker)
            return new(InstallationState.Conflict, Loc.T("status.task.conflict"));
        var principals = root.Element(ns + "Principals")?.Elements(ns + "Principal").ToArray() ?? [];
        var actions = root.Element(ns + "Actions")?.Elements().ToArray() ?? [];
        var settings = root.Element(ns + "Settings");
        bool PathEquals(string actual, string expected)
        {
            try { return Path.IsPathFullyQualified(actual) && string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(actual)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(expected)), StringComparison.OrdinalIgnoreCase); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
        }
        bool valid = principals.Length == 1 && Value(principals[0], "UserId") == sid &&
            Value(principals[0], "LogonType") == "InteractiveToken" && Value(principals[0], "RunLevel") == "HighestAvailable" &&
            actions.Length == 1 && actions[0].Name == ns + "Exec" &&
            PathEquals(Value(actions[0], "Command"), launcher) && Value(actions[0], "Arguments") == "broker" &&
            PathEquals(Value(actions[0], "WorkingDirectory"), directory) &&
            !(root.Element(ns + "Triggers")?.Elements().Any() ?? false) &&
            Value(settings, "MultipleInstancesPolicy") == "IgnoreNew" && Value(settings, "Enabled") != "false" &&
            Value(settings, "AllowStartOnDemand") != "false";
        if (!valid) return new(InstallationState.NeedsRepair, Loc.T("status.task.repair"));
        if (!SecureDescriptor(security, sid)) return new(InstallationState.NeedsRepair, Loc.T("status.task.acl"));
        return new(InstallationState.Ready, Loc.T("status.task.ready"));
    }
    public static bool SecureDescriptor(string sddl, string sid)
    {
        var descriptor = new RawSecurityDescriptor(sddl);
        if (descriptor.Owner?.Value is not ("S-1-5-18" or "S-1-5-32-544") || descriptor.DiscretionaryAcl is null) return false;
        bool userCanRun = false;
        foreach (GenericAce ace in descriptor.DiscretionaryAcl)
        {
            if (ace is not CommonAce rule || rule.IsCallback || rule.AceFlags != AceFlags.None) return false;
            if (rule.AceQualifier != AceQualifier.AccessAllowed) return false;
            string identity = rule.SecurityIdentifier.Value;
            if (identity is "S-1-5-18" or "S-1-5-32-544") continue;
            if (identity != sid) return false;
            // Generic read/execute, or their expanded file-style task rights. No write or ownership rights.
            uint mask = unchecked((uint)rule.AccessMask);
            if ((mask & ~0xA01200A9u) != 0) return false;
            userCanRun |= (mask & 0x20000020u) != 0 && (mask & 0x80000001u) != 0;
        }
        return userCanRun;
    }
}
