using System.Buffers.Binary;
using System.Text.Json;
using UACToolBox.Contracts;
int passed = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("PASS " + name); passed++; }
void Reject(Action action, string name)
{
    try { action(); } catch (InvalidDataException) { Check(true, name); return; }
    throw new Exception("Expected rejection: " + name);
}
async Task RejectFrame(byte[] bytes, string name)
{
    try { await Wire.ReadAsync<LaunchRequest>(new MemoryStream(bytes), default); }
    catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or JsonException) { Check(true, name); return; }
    throw new Exception("Expected rejection: " + name);
}
var entry = new LaunchEntry { ShortcutName = "示例", ExecutablePath = @"C:\Program Files\Example\app.exe",
    WorkingDirectory = @"C:\Program Files\Example", ArgumentsRaw = "--name \"中文 空格\" --empty \"\" --path \"C:\\tail\\\\\"" };
var config = new Configuration { OwnerSid = "owner", Entries = [entry] };
config.Validate("owner");
var restored = JsonSerializer.Deserialize<Configuration>(JsonSerializer.Serialize(config, JsonDefaults.Options), JsonDefaults.Options)!;
Check(restored.Entries[0].ArgumentsRaw == entry.ArgumentsRaw, "Raw parameters survive storage including quotes, Unicode and slashes");
Reject(() => config.Validate("other"), "Reject wrong owner");
Reject(() => (config with { SchemaVersion = 2 }).Validate("owner"), "Reject unsupported schema");
Reject(() => (config with { Entries = [entry, entry] }).Validate("owner"), "Reject duplicate IDs");
Reject(() => (config with { Entries = [entry with { ArgumentsRaw = "bad\0value" }] }).Validate("owner"), "Reject NUL parameter");
Reject(() => (config with { Entries = [entry with { ShowMode = 99 }] }).Validate("owner"), "Reject invalid show mode");
Reject(() => (config with { Revision = long.MaxValue }).Validate("owner"), "Reject revision overflow");
Reject(() => (config with { Entries = Enumerable.Range(0, 100).Select(_ => entry with { Id = Guid.NewGuid(), ArgumentsRaw = new string('x', 24000) }).ToList() }).SerializeForStorage("owner"), "Reject oversized storage before writing");
Check(config.SerializeForStorage("owner").Length < Configuration.MaxStorageBytes, "Accept bounded storage");
var request = new LaunchRequest(1, Guid.NewGuid(), entry.Id);
using var stream = new MemoryStream();
await Wire.WriteAsync(stream, request, default); stream.Position = 0;
Check(await Wire.ReadAsync<LaunchRequest>(stream, default) == request, "Wire request round trip");
foreach (int size in new[] { -1, 0, Wire.MaxBytes + 1 })
{
    var header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, size);
    await RejectFrame(header, "Reject frame size " + size);
}
await RejectFrame([2, 0, 0, 0, (byte)'{'], "Reject truncated body");
await RejectFrame([1, 0], "Reject truncated header");
await RejectFrame([1, 0, 0, 0, (byte)'!'], "Reject malformed JSON");
using var fragmented = new FragmentedStream(stream.ToArray());
Check(await Wire.ReadAsync<LaunchRequest>(fragmented, default) == request, "Read fragmented transport frame");
var response = new LaunchResponse(Guid.NewGuid(), ResultCode.Success, "started");
using var responseFrame = new MemoryStream();
await Wire.WriteAsync(responseFrame, response, default);
using var closedWriter = new ReceiptFailureStream(responseFrame.ToArray());
Check(await ResponseDelivery.ReceiveAsync(closedWriter, response.RequestId, default) == response,
    "Preserve received success when receipt write fails");
try
{
    using var wrongResponse = new ReceiptFailureStream(responseFrame.ToArray());
    await ResponseDelivery.ReceiveAsync(wrongResponse, Guid.NewGuid(), default);
    throw new Exception("Expected response ID rejection");
}
catch (InvalidDataException) { Check(true, "Reject mismatched response ID"); }
var saveRequest = new LaunchRequest(2, Guid.NewGuid(), Guid.Empty, "save-configuration");
using var saveRequestFrame = new MemoryStream();
await Wire.WriteAsync(saveRequestFrame, saveRequest, default); saveRequestFrame.Position = 0;
Check(await Wire.ReadAsync<LaunchRequest>(saveRequestFrame, default) == saveRequest, "Save request carries explicit operation and protocol version");
var largeConfig = config with { Entries = Enumerable.Range(0, 8).Select(_ => entry with { Id = Guid.NewGuid(), ArgumentsRaw = new string('x', 24000) }).ToList() };
using var configFrame = new MemoryStream();
await Wire.WriteAsync(configFrame, largeConfig, default, Configuration.MaxStorageBytes); configFrame.Position = 0;
var receivedConfig = await Wire.ReadAsync<Configuration>(configFrame, default, Configuration.MaxStorageBytes);
Check(configFrame.Length > Wire.MaxBytes && receivedConfig.Entries.Count == 8 && receivedConfig.Entries[0].ArgumentsRaw.Length == 24000,
    "Transfer configuration larger than launch message limit without truncation");
configFrame.Position = 0;
try { await Wire.ReadAsync<Configuration>(configFrame, default); throw new Exception("Unbounded regular frame"); }
catch (InvalidDataException) { Check(true, "Regular message retains 64 KiB bound"); }
var oversizedConfigHeader = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(oversizedConfigHeader, Configuration.MaxStorageBytes + 1);
try { await Wire.ReadAsync<Configuration>(new MemoryStream(oversizedConfigHeader), default, Configuration.MaxStorageBytes); throw new Exception("Unbounded config frame"); }
catch (InvalidDataException) { Check(true, "Reject oversized configuration before allocating body"); }
var legacyEntry = JsonSerializer.Deserialize<LaunchEntry>("""{"displayName":"旧名称","enabled":false}""", JsonDefaults.Options)!;
Check(legacyEntry.ShortcutName == "旧名称" && legacyEntry.ShortcutDirectory == "" && !legacyEntry.Enabled,
    "Legacy name and disabled state load with desktop destination default");
var customEntry = entry with { ShortcutDirectory = @"D:\快捷方式 目录" };
var customJson = JsonSerializer.Serialize(customEntry, JsonDefaults.Options);
Check(customJson.Contains("displayName") && JsonSerializer.Deserialize<LaunchEntry>(customJson, JsonDefaults.Options) == customEntry,
    "Shortcut name and custom destination persist without changing legacy storage key");
Console.WriteLine($"{passed} checks passed");
sealed class FragmentedStream(byte[] bytes) : MemoryStream(bytes)
{
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        => base.ReadAsync(buffer[..Math.Min(buffer.Length, 1)], token);
}

sealed class ReceiptFailureStream(byte[] bytes) : MemoryStream(bytes)
{
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken token = default)
        => ValueTask.FromException(new IOException("Peer disconnected after result delivery"));
}
