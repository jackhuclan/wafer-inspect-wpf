namespace WaferInspect.Core.Events;

/// <summary>
/// 报警/日志统一事件。Level 只保留事件自身的原始级别文本，
/// 高低等级与灯/蜂鸣器联动规则不在 P0 臆造，见【待确认 T-025】。
/// </summary>
public sealed record SystemEvent(
    string EventId,
    DateTimeOffset Timestamp,
    string Source,
    string Kind,
    string Level,
    string Message,
    IReadOnlyDictionary<string, string> Attributes);

/// <summary>H09 统一事件源：报警与日志后续共用同一发布/订阅入口。</summary>
public interface IEventSource
{
    event EventHandler<SystemEvent>? EventPublished;

    void Publish(SystemEvent systemEvent);
}

/// <summary>P0 进程内事件总线；不伪造持久化、报警清除或设备联动行为。</summary>
public sealed class InMemoryEventSource : IEventSource
{
    public event EventHandler<SystemEvent>? EventPublished;

    public void Publish(SystemEvent systemEvent)
    {
        ArgumentNullException.ThrowIfNull(systemEvent);
        EventPublished?.Invoke(this, systemEvent);
    }
}
