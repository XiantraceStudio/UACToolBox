using System.IO.Pipes;
using System.Security.Principal;
using UACToolBox.Contracts;
namespace UACToolBox.WindowsIntegration;
public static class ConfigurationClient
{
    public static async Task SaveAsync(Configuration config)
    {
        ConfigurationWriter.Validate(config);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var pipe = new NamedPipeClientStream(".", PipeSecurity.Name, PipeDirection.InOut,
            PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
        try { await pipe.ConnectAsync(150, deadline.Token); }
        catch (TimeoutException)
        {
            if (!Scheduler.Installed()) throw new InvalidOperationException(UACToolBox.Localization.Loc.T("err.installTaskFirst"));
            Scheduler.Run(); await pipe.ConnectAsync(deadline.Token);
        }
        PipeSecurity.VerifyServer(pipe);
        var request = new LaunchRequest(2, Guid.NewGuid(), Guid.Empty, "save-configuration");
        using var responseDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        await Wire.WriteAsync(pipe, request, responseDeadline.Token);
        var ready = await ResponseDelivery.ReceiveAsync(pipe, request.RequestId, responseDeadline.Token);
        if (ready.Code != ResultCode.Success)
            throw new InvalidOperationException(ready.Message + UACToolBox.Localization.Loc.T("err.staleBrokerSuffix"));
        try
        {
            await Wire.WriteAsync(pipe, config, responseDeadline.Token, Configuration.MaxStorageBytes);
            var result = await ResponseDelivery.ReceiveAsync(pipe, request.RequestId, responseDeadline.Token);
            if (result.Code != ResultCode.Success) throw new InvalidOperationException(result.Message);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException)
        {
            throw new IOException(UACToolBox.Localization.Loc.T("err.saveConnectionLost"), ex);
        }
    }
}
