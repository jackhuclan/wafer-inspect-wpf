using WaferInspect.Core.Run;

namespace WaferInspect.Core.Events;

/// <summary>报警等级（BR-74）：低等级可随蜂鸣器解除一并清除；高等级必须手动处理，否则拦截作业。</summary>
public enum AlarmSeverity
{
    /// <summary>低等级（警告性）：第一次按【蜂鸣器解除】即随声音一并清除。</summary>
    Low,

    /// <summary>高等级：必须手动点击处理，未处理时拦截运行（BR-74）。</summary>
    High
}

/// <summary>机台报警（F-3.6.1-01~03）。BR-75：报警须用户手动处理才能取消，不自动消失。</summary>
public sealed record SystemAlarm(
    string AlarmId,
    AlarmSeverity Severity,
    string Message,
    DateTimeOffset StartTime)
{
    public bool Handled { get; init; }
}

/// <summary>H09 报警中心：报警列表、蜂鸣器解除、手动处理与运行拦截信号（R-H09-01/02/06）。</summary>
public interface IAlarmCenter : IRunInterlock
{
    IReadOnlyList<SystemAlarm> ActiveAlarms { get; }

    event EventHandler<SystemAlarm>? AlarmRaised;

    event EventHandler? AlarmsChanged;

    /// <summary>报警同时进入统一事件源（AC-H09-05：一次事件两处一致）。</summary>
    void Raise(SystemAlarm alarm);

    /// <summary>蜂鸣器解除（F-3.6.1-03）：清除声音；低等级报警随之一并清除（BR-74）。</summary>
    int ClearBuzzer();

    /// <summary>手动处理报警（F-4.2 / BR-75）：高等级必须逐条手动处理。</summary>
    bool Handle(string alarmId);
}

/// <summary>进程内报警中心实现；真实蜂鸣器 / 四色灯联动为设备动作（T-025/T-029 搁置），状态与拦截逻辑在此完整实现。</summary>
public sealed class InMemoryAlarmCenter : IAlarmCenter
{
    private readonly IEventSource _events;
    private readonly List<SystemAlarm> _active = [];
    private bool _buzzerActive;

    public InMemoryAlarmCenter(IEventSource events)
        => _events = events ?? throw new ArgumentNullException(nameof(events));

    public IReadOnlyList<SystemAlarm> ActiveAlarms => _active;

    public event EventHandler<SystemAlarm>? AlarmRaised;
    public event EventHandler? AlarmsChanged;

    public bool IsRunBlocked => _active.Any(a => a.Severity == AlarmSeverity.High && !a.Handled);

    public string? BlockReason => IsRunBlocked
        ? $"存在未处理的高等级报警：{string.Join("；", _active.Where(a => a.Severity == AlarmSeverity.High && !a.Handled).Select(a => a.Message))}"
        : null;

    public void Raise(SystemAlarm alarm)
    {
        ArgumentNullException.ThrowIfNull(alarm);
        if (alarm.Handled)
            throw new ArgumentException("新报警不能携带已处理状态。", nameof(alarm));

        _active.Add(alarm);
        _buzzerActive = true;
        // 同一事件进入统一事件源：日志栏与报警列表一次来源两处一致（AC-H09-05）。
        _events.Publish(new SystemEvent(
            alarm.AlarmId, alarm.StartTime, "AlarmCenter",
            alarm.Severity == AlarmSeverity.High ? "HighAlarm" : "LowAlarm",
            alarm.Severity == AlarmSeverity.High ? "高等级报警" : "低等级报警",
            alarm.Message, new Dictionary<string, string>()));
        AlarmRaised?.Invoke(this, alarm);
        AlarmsChanged?.Invoke(this, EventArgs.Empty);
    }

    public int ClearBuzzer()
    {
        _buzzerActive = false;
        // BR-74：低等级警告性错误随蜂鸣器解除一并被清除；高等级必须手动处理。
        int removed = _active.RemoveAll(a => a.Severity == AlarmSeverity.Low);
        if (removed > 0)
        {
            _events.Publish(new SystemEvent(
                $"BUZZ-{DateTimeOffset.Now:HHmmss-ffffff}", DateTimeOffset.Now, "AlarmCenter",
                "BuzzerClear", "Info", $"蜂鸣器解除：{removed} 条低等级报警一并清除（BR-74）",
                new Dictionary<string, string>()));
            AlarmsChanged?.Invoke(this, EventArgs.Empty);
        }
        return removed;
    }

    public bool Handle(string alarmId)
    {
        int index = _active.FindIndex(a => a.AlarmId == alarmId);
        if (index < 0)
            return false;

        var alarm = _active[index];
        _active.RemoveAt(index);
        _events.Publish(new SystemEvent(
            $"{alarm.AlarmId}-H", DateTimeOffset.Now, "AlarmCenter", "AlarmHandled",
            "Info", $"报警已手动处理：{alarm.Message}", new Dictionary<string, string>()));
        AlarmsChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>蜂鸣器当前状态（四色灯/蜂鸣器真实联动为设备动作，T-025 搁置；状态本身用于 UI 提示）。</summary>
    public bool BuzzerActive => _buzzerActive;
}

/// <summary>
/// 全局格式策略（R-X01-03）：时间、坐标、良率、单位集中定义，禁止模块内各自格式化。
/// </summary>
public static class LogFormat
{
    /// <summary>日志时间格式 BR-18：MM-DD HH:MM:SS（月-日 时:分:秒，历史显示原样）。</summary>
    public const string LogTimePattern = "MM-dd HH:mm:ss";

    public static string LogTime(DateTimeOffset timestamp) => timestamp.ToString(LogTimePattern);
}
