using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;
namespace UACToolBox.WindowsIntegration;

public static class PipeSecurity
{
    public static string Name => $"LaunchManager-{Access.Sid}-{Process.GetCurrentProcess().SessionId}";
    public static NamedPipeServerStream CreateServer()
    {
        // A single protected, local-only endpoint; clients are processed serially.
        string sddl = $"D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GRGW;;;{Access.Sid})S:(ML;;NW;;;ME)";
        if (!ConvertStringSecurityDescriptorToSecurityDescriptor(sddl, 1, out var descriptor, out _)) throw new Win32Exception();
        try
        {
            var sa = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), Descriptor = descriptor };
            var handle = CreateNamedPipe(@"\\.\pipe\" + Name, 3 | 0x40000000 | 0x00080000,
                0x8, 1, 65536, 65536, 0, ref sa); // OVERLAPPED, FIRST_PIPE_INSTANCE, REJECT_REMOTE_CLIENTS
            if (handle.IsInvalid) { handle.Dispose(); throw new Win32Exception(); }
            return new NamedPipeServerStream(PipeDirection.InOut, true, false, handle);
        }
        finally { LocalFree(descriptor); }
    }
    public static void VerifyClient(NamedPipeServerStream pipe)
    {
        string owner = Access.Sid;
        pipe.RunAsClient(() =>
        {
            using var identity = WindowsIdentity.GetCurrent(true) ?? throw new UnauthorizedAccessException();
            if (identity.User?.Value != owner || Integrity(identity.AccessToken) < 0x2000)
                throw new UnauthorizedAccessException("请求者身份或完整性级别不符。");
        });
        if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out uint pid) ||
            !ProcessIdToSessionId(pid, out uint session) || session != Process.GetCurrentProcess().SessionId)
            throw new UnauthorizedAccessException("请求会话不符。");
    }
    public static void VerifyConfigurationClient(NamedPipeServerStream pipe)
    {
        VerifyClient(pipe);
        if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out uint pid)) throw new Win32Exception();
        using var process = OpenProcess(0x1000, false, pid);
        var path = new StringBuilder(32768); uint length = (uint)path.Capacity;
        string expected = Path.Combine(AppContext.BaseDirectory, "Config.exe");
        if (process.IsInvalid || !QueryFullProcessImageName(process, 0, path, ref length) ||
            !string.Equals(Path.GetFullPath(path.ToString()), expected, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("仅接受本安装的配置界面提交保存。");
        Access.ProtectedPath(expected);
        Access.ProtectedPath(AppContext.BaseDirectory, true);
    }
    public static void VerifyServer(NamedPipeClientStream pipe)
    {
        if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out uint pid)) throw new Win32Exception();
        using var process = OpenProcess(0x1000, false, pid);
        if (process.IsInvalid || !OpenProcessToken(process, 8, out var token)) throw new UnauthorizedAccessException("无法核验执行端。");
        using (token)
        using (var identity = new WindowsIdentity(token.DangerousGetHandle()))
        {
            if (identity.User?.Value != Access.Sid || Integrity(token) < 0x3000 ||
                !ProcessIdToSessionId(pid, out uint session) || session != Process.GetCurrentProcess().SessionId)
                throw new UnauthorizedAccessException("执行端身份、权限或会话不符。");
        }
        var name = new StringBuilder(32768); uint length = (uint)name.Capacity;
        if (!QueryFullProcessImageName(process, 0, name, ref length) ||
            !string.Equals(Path.GetFullPath(name.ToString()), Path.GetFullPath(Store.LauncherPath), StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("执行端路径不符，请修复安装。");
        Access.ProtectedPath(name.ToString());
        Access.ProtectedPath(Path.GetDirectoryName(name.ToString())!, true);
    }
    static int Integrity(SafeAccessTokenHandle token)
    {
        GetTokenInformation(token, 25, IntPtr.Zero, 0, out int size);
        if (size <= 0 || size > 65536) throw new Win32Exception();
        var memory = Marshal.AllocHGlobal(size);
        try
        {
            if (!GetTokenInformation(token, 25, memory, size, out _)) throw new Win32Exception();
            var sid = new SecurityIdentifier(Marshal.ReadIntPtr(memory));
            return int.Parse(sid.Value.Split('-')[^1]);
        }
        finally { Marshal.FreeHGlobal(memory); }
    }
    [StructLayout(LayoutKind.Sequential)] struct SecurityAttributes { public int Length; public IntPtr Descriptor; public int Inherit; }
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string sddl, uint revision, out IntPtr descriptor, out uint size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern SafePipeHandle CreateNamedPipe(string name, uint open, uint mode, uint instances, uint outputSize, uint inputSize, uint timeout, ref SecurityAttributes security);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr memory);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool ProcessIdToSessionId(uint pid, out uint session);
    [DllImport("kernel32.dll", SetLastError = true)] static extern SafeProcessHandle OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool GetTokenInformation(SafeAccessTokenHandle token, int kind, IntPtr info, int size, out int needed);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder name, ref uint size);
}
