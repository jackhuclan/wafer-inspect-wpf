#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Automation;

// 启动冒烟：启动 Release 产物，通过 UIAutomation 以初始账号（BR-76 / T-027 已确认：admin / 123456）
// 真实登录，再断言进程存活且已创建主窗口。登录门禁（AC-P0-04）生效后，未认证不会出现主窗口。
// 用法：dotnet run --project tools/WaferInspect.Smoke -c Release -- [可选 exe 绝对路径]

const string LoginWindowTitle = "WaferInspect · 用户登录";
const string MainWindowTitle = "WaferInspect · 晶圆检测上位机";

var exe = ResolveExecutable(args);
Console.WriteLine($"[smoke] 启动 {exe}");

using var process = Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false })
    ?? throw new InvalidOperationException("进程启动失败");

// 1) 等待登录窗口出现（门禁生效：先登录后主窗口）
AutomationElement? loginWindow = WaitForElement(
    () => FindWindowByTitle(LoginWindowTitle),
    TimeSpan.FromSeconds(15),
    "[smoke] 登录窗口");
if (loginWindow is null)
{
    Console.Error.WriteLine("[smoke] FAIL 未在超时内找到登录窗口（AC-P0-04 门禁可能失效）");
    process.Kill();
    return 2;
}

// 2) UIAutomation 真实登录：填入初始账号 admin / 123456（BR-76，T-027 已确认原样保留）
if (!SetValueById(loginWindow, "LoginUserIdBox", "admin")
    || !SetValueById(loginWindow, "LoginPasswordBox", "123456"))
{
    Console.Error.WriteLine("[smoke] FAIL 登录输入框不可用（ValuePattern）");
    process.Kill();
    return 3;
}

AutomationElement? submit = FindDescendantById(loginWindow, "LoginSubmitButton");
if (submit is null || !submit.TryGetCurrentPattern(InvokePattern.Pattern, out object? invokeObj))
{
    Console.Error.WriteLine("[smoke] FAIL 未找到登录按钮或不可调用（InvokePattern）");
    process.Kill();
    return 3;
}
((InvokePattern)invokeObj).Invoke();
Console.WriteLine("[smoke] 已提交登录（admin）");

// 3) 等待主窗口出现并断言进程与主窗口句柄
AutomationElement? mainWindow = WaitForElement(
    () => FindWindowByTitle(MainWindowTitle),
    TimeSpan.FromSeconds(20),
    "[smoke] 主窗口");
if (mainWindow is null)
{
    Console.Error.WriteLine("[smoke] FAIL 登录后未出现主窗口（认证或门禁链路异常）");
    DumpDesktopWindows();
    DumpLoginErrorText(loginWindow);
    process.Kill();
    return 4;
}

process.Refresh();
if (process.HasExited)
{
    Console.Error.WriteLine($"[smoke] FAIL 进程提前退出，ExitCode={process.ExitCode}");
    return 1;
}
if (process.MainWindowHandle == IntPtr.Zero)
{
    Console.Error.WriteLine("[smoke] FAIL 未创建主窗口句柄");
    return 2;
}

Console.WriteLine($"[smoke] OK hwnd={process.MainWindowHandle} title={MainWindowTitle}");
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

static AutomationElement? FindWindowByTitle(string title)
    => AutomationElement.RootElement.FindFirst(
        TreeScope.Children,
        new PropertyCondition(AutomationElement.NameProperty, title));

static AutomationElement? FindDescendantById(AutomationElement root, string automationId)
    => root.FindFirst(
        TreeScope.Descendants,
        new PropertyCondition(AutomationElement.AutomationIdProperty, automationId));

static bool SetValueById(AutomationElement root, string automationId, string value)
{
    var element = FindDescendantById(root, automationId);
    if (element is null || !element.TryGetCurrentPattern(ValuePattern.Pattern, out object? patternObj))
        return false;
    ((ValuePattern)patternObj).SetValue(value);
    return true;
}

static AutomationElement? WaitForElement(Func<AutomationElement?> probe, TimeSpan timeout, string label)
{
    var deadline = DateTime.UtcNow + timeout;
    while (DateTime.UtcNow < deadline)
    {
        var found = probe();
        if (found is not null)
        {
            Console.WriteLine($"{label} 已出现");
            return found;
        }
        Thread.Sleep(300);
    }
    return null;
}

// 失败诊断：枚举桌面顶层窗口，确认登录窗口是否留驻、是否有意外弹窗。
static void DumpDesktopWindows()
{
    var windows = AutomationElement.RootElement.FindAll(
        TreeScope.Children, Condition.TrueCondition);
    foreach (AutomationElement window in windows)
        Console.Error.WriteLine($"[smoke][诊断] 顶层窗口：{window.Current.Name}");
}

// 失败诊断：读回登录窗口错误文本与输入框当前值，定位认证失败原因。
static void DumpLoginErrorText(AutomationElement loginWindow)
{
    var texts = loginWindow.FindAll(
        TreeScope.Descendants,
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text));
    foreach (AutomationElement text in texts)
    {
        if (!string.IsNullOrEmpty(text.Current.Name))
            Console.Error.WriteLine($"[smoke][诊断] 文本：{text.Current.Name}");
    }

    foreach (string id in new[] { "LoginUserIdBox", "LoginPasswordBox" })
    {
        var box = FindDescendantById(loginWindow, id);
        if (box is not null && box.TryGetCurrentPattern(ValuePattern.Pattern, out object? valuePattern))
            Console.Error.WriteLine($"[smoke][诊断] {id} 当前值长度：{((ValuePattern)valuePattern).Current.Value.Length}");
    }
}
