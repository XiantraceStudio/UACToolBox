using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using UACToolBox.Contracts;
using UACToolBox.WindowsIntegration;
using UACToolBox.Localization;
using PipeSecurity = UACToolBox.WindowsIntegration.PipeSecurity;
namespace UACToolBox.Launcher;
internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        bool silent = args.Contains("--silent");
        args = args.Where(a => a != "--silent").ToArray();
        try
        {
            if (args is ["broker"]) { Broker().GetAwaiter().GetResult(); return 0; }
            Guid id;
            if (args is ["launch", var text] && Guid.TryParse(text, out id)) { }
            else if (args is ["resolve", var path]) id = Resolve(path);
            else throw new ArgumentException(Loc.T("err.usage"));
            var response = Client(id).GetAwaiter().GetResult();
            if (response.Code != ResultCode.Success && !silent) MessageBox(IntPtr.Zero, response.Message, Loc.T("msg.launchFailed"), 0x10);
            return (int)response.Code;
        }
        catch (Exception ex)
        {
            if (!silent && args is not ["broker"]) MessageBox(IntPtr.Zero, ex.Message, Loc.T("msg.launchFailed"), 0x10);
            return (int)ResultCode.StartFailed;
        }
    }
    static Guid Resolve(string path)
    {
        var imported = Shortcuts.Import(path);
        var config = Store.Load();
        if (string.Equals(imported.ExecutablePath, Store.LauncherPath, StringComparison.OrdinalIgnoreCase) &&
            imported.ArgumentsRaw.StartsWith("launch ") && Guid.TryParse(imported.ArgumentsRaw[7..], out var own))
            return config.Entries.Single(e => e.Id == own && e.Enabled).Id;
        var entries = config.Entries.Where(e => e.Enabled &&
            string.Equals(e.ExecutablePath, imported.ExecutablePath, StringComparison.OrdinalIgnoreCase));
        if (imported.SourceShortcut is not null)
            entries = entries.Where(e => e.ArgumentsRaw == imported.ArgumentsRaw &&
                string.Equals(e.WorkingDirectory, imported.WorkingDirectory, StringComparison.OrdinalIgnoreCase) && e.ShowMode == imported.ShowMode);
        var matches = entries.ToArray();
        if (matches.Length != 1) throw new InvalidOperationException(Loc.T(matches.Length == 0 ? "err.notConfigured" : "err.multiMatch"));
        return matches[0].Id;
    }
    static async Task<LaunchResponse> Client(Guid id)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var pipe = new NamedPipeClientStream(".", PipeSecurity.Name, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
        try { await pipe.ConnectAsync(150, deadline.Token); }
        catch (TimeoutException)
        {
            if (!Scheduler.Installed()) throw new InvalidOperationException(Loc.T("err.taskMissing"));
            Scheduler.Run(); await pipe.ConnectAsync(deadline.Token);
        }
        PipeSecurity.VerifyServer(pipe);
        var request = new LaunchRequest(1, Guid.NewGuid(), id);
        try
        {
            using var responseDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await Wire.WriteAsync(pipe, request, responseDeadline.Token);
            var response = await ResponseDelivery.ReceiveAsync(pipe, request.RequestId, responseDeadline.Token);
            return response;
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException)
        { return new(request.RequestId, ResultCode.Unknown, Loc.T("err.unknownResult")); }
    }
    static async Task Broker()
    {
        Access.RequireAdmin();
        Access.ProtectedPath(Environment.ProcessPath!); Access.ProtectedPath(AppContext.BaseDirectory, true);
        using var pipe = PipeSecurity.CreateServer();
        var completed = new Dictionary<Guid, (Guid Entry, LaunchResponse Response)>();
        while (completed.Count < 4096)
        {
            using var idle = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try { await pipe.WaitForConnectionAsync(idle.Token); }
            catch (OperationCanceledException) { return; }
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                // Reading starts the impersonation context for RunAsClient.
                var request = await Wire.ReadAsync<LaunchRequest>(pipe, timeout.Token);
                PipeSecurity.VerifyClient(pipe);
                LaunchResponse response;
                if (request.Version == 2 && request.Operation == "save-configuration" && request.RequestId != Guid.Empty && request.EntryId == Guid.Empty)
                    response = await SaveConfiguration(pipe, request);
                else if (request.Version != 1 || request.Operation != "launch" || request.RequestId == Guid.Empty || request.EntryId == Guid.Empty)
                    response = new(request.RequestId, ResultCode.InvalidInput, "请求格式无效。");
                else if (completed.TryGetValue(request.RequestId, out var previous))
                    response = previous.Entry == request.EntryId ? previous.Response : new(request.RequestId, ResultCode.InvalidInput, "重复请求 ID 内容不符。");
                else
                {
                    response = Execute(request);
                    completed.Add(request.RequestId, (request.EntryId, response));
                }
                using var deliveryDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await ResponseDelivery.SendAsync(pipe, response, deliveryDeadline.Token);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or OperationCanceledException or System.Text.Json.JsonException or System.ComponentModel.Win32Exception) { }
            finally { pipe.Disconnect(); }
        }
    }
    static async Task<LaunchResponse> SaveConfiguration(NamedPipeServerStream pipe, LaunchRequest request)
    {
        try
        {
            PipeSecurity.VerifyConfigurationClient(pipe);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await ResponseDelivery.SendAsync(pipe, new(request.RequestId, ResultCode.Success, "可以提交配置"), timeout.Token);
            var config = await Wire.ReadAsync<Configuration>(pipe, timeout.Token, Configuration.MaxStorageBytes);
            ConfigurationWriter.Save(config);
            return new(request.RequestId, ResultCode.Success, "配置已保存。");
        }
        catch (Exception ex) { return new(request.RequestId, ResultCode.StartFailed, ex.Message); }
    }
    static LaunchResponse Execute(LaunchRequest request)
    {
        try
        {
            var entry = Store.Load().Entries.SingleOrDefault(e => e.Id == request.EntryId && e.Enabled);
            if (entry is null) return new(request.RequestId, ResultCode.NotAuthorized, "配置不存在或已禁用。");
            Access.ValidateTarget(entry.ExecutablePath, entry.WorkingDirectory);
            // Hold executable against replacement while creating the process.
            using var guard = new FileStream(entry.ExecutablePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var start = new ProcessStartInfo(entry.ExecutablePath, entry.ArgumentsRaw)
            {
                WorkingDirectory = entry.WorkingDirectory, UseShellExecute = false,
                WindowStyle = entry.ShowMode switch { 3 => ProcessWindowStyle.Maximized, 7 => ProcessWindowStyle.Minimized, _ => ProcessWindowStyle.Normal }
            };
            using var process = Process.Start(start) ?? throw new InvalidOperationException("未创建目标进程。");
            return new(request.RequestId, ResultCode.Success, "已启动", process.Id);
        }
        catch (Exception ex) { return new(request.RequestId, ResultCode.StartFailed, ex.Message); }
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int MessageBox(IntPtr window, string text, string title, uint type);
}
