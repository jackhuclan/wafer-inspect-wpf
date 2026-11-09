using System.Diagnostics;

// 启动冒烟：启动 Release 产物，等待后断言进程存活且已创建主窗口。
// 用法：dotnet run --project tools/WaferInspect.Smoke -c Release -- [可选 exe 绝对路径]

var exe = ResolveExecutable(args);
Console.WriteLine($"[smoke] 启动 {exe}");

using var process = Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false })
    ?? throw new InvalidOperationException("进程启动失败");

Thread.Sleep(3000);
process.Refresh();

if (process.HasExited)
{
    Console.Error.WriteLine($"[smoke] FAIL 进程提前退出，ExitCode={process.ExitCode}");
    return 1;
}

if (process.MainWindowHandle == IntPtr.Zero)
{
    Console.Error.WriteLine("[smoke] FAIL 未创建主窗口句柄");
    process.Kill();
    return 2;
}

Console.WriteLine($"[smoke] OK hwnd={process.MainWindowHandle} title={process.MainWindowTitle}");
process.Kill();
return 0;

static string ResolveExecutable(string[] args)
{
    if (args.Length > 0 && File.Exists(args[0]))
    {
        return Path.GetFullPath(args[0]);
    }

    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "WaferInspect.slnx")))
        {
            return Path.Combine(dir.FullName, "src", "WaferInspect.App", "bin",
                "Release", "net10.0-windows", "WaferInspectHMI.exe");
        }

        dir = dir.Parent;
    }

    throw new FileNotFoundException("未找到 WaferInspect.slnx，请显式传入 WaferInspectHMI.exe 路径");
}
