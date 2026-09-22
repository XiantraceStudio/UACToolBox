using System.Buffers.Binary;
using System.Text.Json;
namespace UACToolBox.Contracts;
public static class Wire
{
    public const int MaxBytes = 65536;
    public static async Task WriteAsync<T>(Stream stream, T message, CancellationToken token, int maxBytes = MaxBytes)
    {
        var data = JsonSerializer.SerializeToUtf8Bytes(message, JsonDefaults.Options);
        if (data.Length > maxBytes) throw new InvalidDataException(UACToolBox.Localization.Loc.T("err.msgTooLarge"));
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, data.Length);
        await stream.WriteAsync(header, token);
        await stream.WriteAsync(data, token);
        await stream.FlushAsync(token);
    }
    public static async Task<T> ReadAsync<T>(Stream stream, CancellationToken token, int maxBytes = MaxBytes)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, token);
        var size = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (size <= 0 || size > maxBytes) throw new InvalidDataException(UACToolBox.Localization.Loc.T("err.msgLengthInvalid"));
        var data = new byte[size];
        await stream.ReadExactlyAsync(data, token);
        return JsonSerializer.Deserialize<T>(data, JsonDefaults.Options) ?? throw new InvalidDataException(UACToolBox.Localization.Loc.T("err.msgEmpty"));
    }
}
