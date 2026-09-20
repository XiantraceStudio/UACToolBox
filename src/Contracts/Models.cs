using System.Text.Json;
using System.Text.Json.Serialization;
namespace UACToolBox.Contracts;
public sealed record LaunchEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();
    // Preserve the stored field name so existing configuration and older readers remain compatible.
    [JsonPropertyName("displayName")]
    public string ShortcutName { get; init; } = "";
    public string ShortcutDirectory { get; init; } = ""; // Empty means the current user desktop.
    public string ExecutablePath { get; init; } = "";
    public string ArgumentsRaw { get; init; } = "";
    public string WorkingDirectory { get; init; } = "";
    public int ShowMode { get; init; } = 1;
    public string IconPath { get; init; } = "";
    public int IconIndex { get; init; }
    public bool Enabled { get; init; } = true;
    public string? SourceShortcut { get; init; }
}
public sealed record Configuration
{
    public const int MaxStorageBytes = 2_000_000;
    public byte[] SerializeForStorage(string owner)
    {
        Validate(owner);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(this, JsonDefaults.Options);
        if (bytes.Length > MaxStorageBytes) throw new InvalidDataException("配置总大小超过限制，请减少条目或参数长度。");
        return bytes;
    }
    public int SchemaVersion { get; init; } = 1;
    public long Revision { get; init; }
    public string OwnerSid { get; init; } = "";
    public List<LaunchEntry> Entries { get; init; } = [];
    public void Validate(string owner)
    {
        if (SchemaVersion != 1 || Revision < 0 || Revision == long.MaxValue || OwnerSid != owner)
            throw new InvalidDataException("配置版本或所属账户不匹配。");
        if (Entries is null || Entries.Count > 500 || Entries.Any(x => x is null))
            throw new InvalidDataException("配置条目无效。");
        if (Entries.Select(x => x.Id).Distinct().Count() != Entries.Count)
            throw new InvalidDataException("配置含重复 ID。");
        foreach (var e in Entries)
            if (e.Id == Guid.Empty || string.IsNullOrWhiteSpace(e.ShortcutName) ||
                string.IsNullOrWhiteSpace(e.ExecutablePath) || string.IsNullOrWhiteSpace(e.WorkingDirectory) ||
                e.ArgumentsRaw is null || e.ArgumentsRaw.Length > 24000 ||
                e.ArgumentsRaw.Contains((char)0) || e.ShowMode is not (1 or 3 or 7))
                throw new InvalidDataException("配置中存在无效名称、路径、参数或窗口状态。");
    }
}
public enum ResultCode { Success, InvalidInput, NotInstalled, NotAuthorized, StartFailed, Busy, Unknown }
public sealed record LaunchRequest(int Version, Guid RequestId, Guid EntryId, string Operation = "launch");
public sealed record LaunchResponse(Guid RequestId, ResultCode Code, string Message, int? ProcessId = null);
public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };
}
