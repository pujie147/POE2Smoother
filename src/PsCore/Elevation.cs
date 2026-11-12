using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Principal;

namespace PsCore;

/// <summary>
/// Detects whether the process can write into the game directory, and relaunches the GUI with
/// UAC elevation when it cannot. Nothing here is required for read-only operations, so the tool
/// stays usable without administrator rights.
/// </summary>
public static class Elevation
{
    public static bool IsElevated =>
        OperatingSystem.IsWindows() && WindowsIsElevated();

    /// <summary>
    /// True when the current process is our own apphost, so relaunching it reproduces this run.
    /// Under `dotnet run` the host is dotnet.exe, which would start the SDK instead of the app.
    /// </summary>
    public static bool CanRelaunch
    {
        get
        {
            if (!OperatingSystem.IsWindows() || IsElevated)
                return false;

            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe))
                return false;

            var name = Path.GetFileNameWithoutExtension(exe);
            return !name.Equals("dotnet", StringComparison.OrdinalIgnoreCase);
        }
    }

    public static bool TryRelaunchElevated(out string? error)
    {
        error = null;

        if (!CanRelaunch)
        {
            error = OperatingSystem.IsWindows()
                ? "当前进程无法自动提权，请手动右键“以管理员身份运行”。 / Cannot relaunch automatically — start it manually as administrator."
                : "自动提权仅支持 Windows。 / Automatic elevation is Windows-only.";
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppContext.BaseDirectory
            };

            foreach (var arg in Environment.GetCommandLineArgs().Skip(1))
                startInfo.ArgumentList.Add(arg);

            Process.Start(startInfo);
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            error = "已取消管理员授权。 / The elevation prompt was cancelled.";
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool WindowsIsElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
}