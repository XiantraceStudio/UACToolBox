using System.Runtime.InteropServices;
namespace UACToolBox.WindowsIntegration;
public static class Scheduler
{
    public static string TaskName => "LaunchManager-" + Access.Sid;
    // Explicitly release all acquired RCWs; task settings polling must not retain COM objects.
    sealed class ComScope : IDisposable
    {
        readonly List<object> values = [];
        public dynamic Track(object value)
        {
            if (Marshal.IsComObject(value) && !values.Any(x => ReferenceEquals(x, value))) values.Add(value);
            return value;
        }
        public void Dispose() { for (int i = values.Count - 1; i >= 0; i--) Marshal.ReleaseComObject(values[i]); }
    }
    static dynamic Connect(ComScope scope)
    {
        dynamic service = scope.Track(Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!);
        service.Connect(); return service;
    }
    static InstallationStatus InspectTask(dynamic task) => TaskPolicy.Inspect((string)task.Xml,
        (string)task.GetSecurityDescriptor(7), Access.Sid, Store.LauncherPath, Store.Root);
    public static InstallationStatus Inspect()
    {
        using var scope = new ComScope();
        dynamic service = Connect(scope); dynamic root = scope.Track(service.GetFolder(@"\"));
        try { return InspectTask(scope.Track(root.GetTask(TaskName))); }
        catch (Exception ex) when ((uint)ex.HResult == 0x80070002)
        { return new(InstallationState.Missing, "尚未安装计划任务。"); }
    }
    public static bool Installed() => Inspect().State == InstallationState.Ready;
    public static void Install()
    {
        Access.RequireAdmin(); Store.Ensure();
        // 执行端部署到受保护的配置根目录；安装目录本身不再要求受保护。
        string staged = Path.Combine(Store.Root, Guid.NewGuid() + ".tmp");
        try
        {
            File.Copy(Store.LauncherSource, staged, true);
            File.Move(staged, Store.LauncherPath, true);
        }
        finally { if (File.Exists(staged)) File.Delete(staged); }
        Access.ProtectedPath(Store.LauncherPath); Access.ProtectedPath(Store.Root, true);
        // 记录配置界面位置与哈希，作为保存请求的调用方核验锚点。
        Store.RecordConfigExe(Environment.ProcessPath!);
        using var scope = new ComScope();
        dynamic service = Connect(scope); dynamic root = scope.Track(service.GetFolder(@"\"));
        string? oldXml = null, oldSecurity = null;
        try
        {
            dynamic old = scope.Track(root.GetTask(TaskName));
            if (InspectTask(old).State == InstallationState.Conflict) throw new InvalidOperationException("存在不属于本工具的同名任务。");
            oldXml = old.Xml; oldSecurity = old.GetSecurityDescriptor(7);
        }
        catch (Exception ex) when ((uint)ex.HResult == 0x80070002) { }
        dynamic task = scope.Track(service.NewTask(0));
        dynamic registration = scope.Track(task.RegistrationInfo); registration.Description = TaskPolicy.Marker;
        dynamic principal = scope.Track(task.Principal);
        principal.UserId = Access.Sid; principal.LogonType = 3; principal.RunLevel = 1;
        dynamic settings = scope.Track(task.Settings);
        settings.MultipleInstances = 2; settings.DisallowStartIfOnBatteries = false;
        settings.StopIfGoingOnBatteries = false; settings.ExecutionTimeLimit = "PT0S";
        settings.AllowDemandStart = true; settings.Enabled = true;
        dynamic actions = scope.Track(task.Actions); dynamic action = scope.Track(actions.Create(0));
        action.Path = Store.LauncherPath; action.Arguments = "broker"; action.WorkingDirectory = Store.Root;
        string sddl = $"O:BAG:BAD:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GRGX;;;{Access.Sid})";
        bool registered = false;
        try
        {
            // DONT_ADD_PRINCIPAL_ACE prevents an additional permissive default ACE.
            dynamic installed = scope.Track(root.RegisterTaskDefinition(TaskName, task, 6 | 0x10, Access.Sid, null, 3, sddl));
            registered = true;
            InstallationStatus status = InspectTask(installed);
            if (status.State != InstallationState.Ready) throw new InvalidOperationException(status.Message);
        }
        catch (Exception original) when (registered)
        {
            try
            {
                if (oldXml is null) root.DeleteTask(TaskName, 0);
                else scope.Track(root.RegisterTask(TaskName, oldXml, 6 | 0x10, null, null, 3, oldSecurity));
            }
            catch (Exception rollback) { throw new AggregateException("任务安装验证失败，回滚也失败，请检查任务计划。", original, rollback); }
            throw;
        }
    }
    public static void Run()
    {
        using var scope = new ComScope();
        dynamic service = Connect(scope); dynamic root = scope.Track(service.GetFolder(@"\"));
        dynamic task = scope.Track(root.GetTask(TaskName));
        InstallationStatus status = InspectTask(task);
        if (status.State != InstallationState.Ready) throw new InvalidOperationException(status.Message);
        scope.Track(task.Run(null));
    }
    public static void Uninstall()
    {
        Access.RequireAdmin();
        using var scope = new ComScope();
        dynamic service = Connect(scope); dynamic root = scope.Track(service.GetFolder(@"\"));
        dynamic task;
        try { task = scope.Track(root.GetTask(TaskName)); }
        catch (Exception ex) when ((uint)ex.HResult == 0x80070002) { return; }
        if (InspectTask(task).State == InstallationState.Conflict) throw new InvalidOperationException("同名任务不属于本工具，未删除。");
        task.Stop(0); root.DeleteTask(TaskName, 0);
    }
}
