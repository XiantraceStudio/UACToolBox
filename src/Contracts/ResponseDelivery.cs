namespace UACToolBox.Contracts;
public sealed record ResponseReceipt(Guid RequestId);
public static class ResponseDelivery
{
    // A completed pipe write only reaches the kernel buffer. Keep the connection alive
    // until the peer consumes the frame; DisconnectNamedPipe discards unread data.
    public static async Task SendAsync(Stream stream, LaunchResponse response, CancellationToken token)
    {
        await Wire.WriteAsync(stream, response, token);
        var receipt = await Wire.ReadAsync<ResponseReceipt>(stream, token);
        if (receipt.RequestId != response.RequestId) throw new InvalidDataException(UACToolBox.Localization.Loc.T("err.receiptMismatch"));
    }
    public static async Task<LaunchResponse> ReceiveAsync(Stream stream, Guid requestId, CancellationToken token)
    {
        var response = await Wire.ReadAsync<LaunchResponse>(stream, token);
        if (response.RequestId != requestId) throw new InvalidDataException(UACToolBox.Localization.Loc.T("err.responseMismatch"));
        try { await Wire.WriteAsync(stream, new ResponseReceipt(requestId), token); }
        catch (Exception ex) when (ex is IOException or OperationCanceledException) { }
        // Once a valid result is received, a failed acknowledgement cannot undo it.
        return response;
    }
}
