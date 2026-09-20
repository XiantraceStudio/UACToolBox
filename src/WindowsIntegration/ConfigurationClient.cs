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
            if (!Scheduler.Installed()) throw new InvalidOperationException("请先在设置页安装 / 修复计划任务，完成一次管理员授权后即可免提示保存。");
            Scheduler.Run(); await pipe.ConnectAsync(deadline.Token);
        }
        PipeSecurity.VerifyServer(pipe);
        var request = new LaunchRequest(2, Guid.NewGuid(), Guid.Empty, "save-configuration");
        using var responseDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        await Wire.WriteAsync(pipe, request, responseDeadline.Token);
        var ready = await ResponseDelivery.ReceiveAsync(pipe, request.RequestId, responseDeadline.Token);
        if (ready.Code != ResultCode.Success)
            throw new InvalidOperationException(ready.Message + " 请确认已替换完整新版程序，并等待旧执行端退出。");
        try
        {
            await Wire.WriteAsync(pipe, config, responseDeadline.Token, Configuration.MaxStorageBytes);
            var result = await ResponseDelivery.ReceiveAsync(pipe, request.RequestId, responseDeadline.Token);
            if (result.Code != ResultCode.Success) throw new InvalidOperationException(result.Message);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException)
        {
            throw new IOException("保存连接中断或超时，结果未知。请重新打开配置界面核对已保存内容后再操作。", ex);
        }
    }
}
