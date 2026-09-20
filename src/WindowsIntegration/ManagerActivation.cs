using System.IO.Pipes;
using UACToolBox.Contracts;
namespace UACToolBox.WindowsIntegration;
public sealed record ManagerRequest(string[] Arguments);
public sealed class ManagerActivation(string name) : IDisposable
{
    readonly CancellationTokenSource stopping = new();
    public async Task ListenAsync(Action<string[]> enqueue)
    {
        while (!stopping.IsCancellationRequested)
        {
            using var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try
            {
                await pipe.WaitForConnectionAsync(stopping.Token);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                var request = await Wire.ReadAsync<ManagerRequest>(pipe, timeout.Token);
                if (request.Arguments is not ([] or ["import", _])) throw new InvalidDataException("无效的管理界面请求。");
                enqueue(request.Arguments);
                await ResponseDelivery.SendAsync(pipe, new(Guid.Empty, ResultCode.Success, "已交给已有窗口"), timeout.Token);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or OperationCanceledException or System.Text.Json.JsonException) { }
        }
    }
    public static async Task ForwardAsync(string name, string[] arguments)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(timeout.Token);
        await Wire.WriteAsync(pipe, new ManagerRequest(arguments), timeout.Token);
        await ResponseDelivery.ReceiveAsync(pipe, Guid.Empty, timeout.Token);
    }
    public void Dispose() => stopping.Cancel();
}
