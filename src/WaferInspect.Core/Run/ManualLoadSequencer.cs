namespace WaferInspect.Core.Run;

/// <summary>手动上料步骤（BR-05：顺序固定，不得跳步或乱序）。</summary>
public enum ManualLoadStep
{
    /// <summary>未开始。</summary>
    Idle,

    /// <summary>① 载物台移至「上料位」。</summary>
    MoveToLoadPosition,

    /// <summary>② 开安全门。</summary>
    OpenSafetyDoor,

    /// <summary>③ 人工放片。</summary>
    PlaceWafer,

    /// <summary>④ 关安全门。</summary>
    CloseSafetyDoor,

    /// <summary>⑤ 载物台移至「拍照位」。</summary>
    MoveToPhotoPosition,

    /// <summary>流程完成。</summary>
    Completed
}

/// <summary>
/// 手动模式上料顺序控制器（F-3.1.2-08 / F-3.1.2-12 / BR-05）。
/// 只负责顺序与状态判定；真实的 Chuck 台移动、安全门开关为设备动作（T-029 搁置），
/// 由 <c>IDeviceGateway</c> 实现，本类不发送任何设备命令。
/// </summary>
public sealed class ManualLoadSequencer
{
    /// <summary>固定顺序（含首尾），供界面渲染步骤条与断言使用。</summary>
    public static IReadOnlyList<ManualLoadStep> OrderedSteps { get; } =
    [
        ManualLoadStep.MoveToLoadPosition,
        ManualLoadStep.OpenSafetyDoor,
        ManualLoadStep.PlaceWafer,
        ManualLoadStep.CloseSafetyDoor,
        ManualLoadStep.MoveToPhotoPosition
    ];

    /// <summary>当前已完成到的步骤。</summary>
    public ManualLoadStep Current { get; private set; } = ManualLoadStep.Idle;

    /// <summary>下一步；已完成时为 <see cref="ManualLoadStep.Completed"/>。</summary>
    public ManualLoadStep Next => Current switch
    {
        ManualLoadStep.Idle => ManualLoadStep.MoveToLoadPosition,
        ManualLoadStep.MoveToLoadPosition => ManualLoadStep.OpenSafetyDoor,
        ManualLoadStep.OpenSafetyDoor => ManualLoadStep.PlaceWafer,
        ManualLoadStep.PlaceWafer => ManualLoadStep.CloseSafetyDoor,
        ManualLoadStep.CloseSafetyDoor => ManualLoadStep.MoveToPhotoPosition,
        ManualLoadStep.MoveToPhotoPosition => ManualLoadStep.Completed,
        _ => ManualLoadStep.Completed
    };

    /// <summary>五步走完即完成；<see cref="ManualLoadStep.Completed"/> 仅作为「无后续步骤」的哨兵。</summary>
    public bool IsCompleted => Current is ManualLoadStep.MoveToPhotoPosition or ManualLoadStep.Completed;

    /// <summary>界面提示语；未开始给出第一步指引。</summary>
    public string Prompt => Current switch
    {
        ManualLoadStep.Idle => "请点击【上料位】将载物台移至放片位置",
        ManualLoadStep.MoveToLoadPosition => "请【开安全门】后人工放片",
        ManualLoadStep.OpenSafetyDoor => "请放入待检测晶圆后点击【放片完成】",
        ManualLoadStep.PlaceWafer => "请【关安全门】",
        ManualLoadStep.CloseSafetyDoor => "请点击【拍照位】将载物台移至相机下方",
        ManualLoadStep.MoveToPhotoPosition => "手动上料完成（BR-05）",
        _ => "手动上料完成（BR-05）"
    };

    /// <summary>
    /// 推进一步。跳步或乱序时抛出 <see cref="InvalidOperationException"/>（BR-05），
    /// 界面据此置灰非法按钮而不是静默纠正。
    /// </summary>
    public ManualLoadStep Advance(ManualLoadStep step)
    {
        if (step != Next)
        {
            throw new InvalidOperationException(
                $"手动上料必须按 上料位 → 开安全门 → 放片 → 关安全门 → 拍照位 顺序执行（BR-05）；当前应执行：{Next}。");
        }

        Current = step;
        return Current;
    }

    /// <summary>重新开始一轮手动上料（例如换片）。</summary>
    public void Reset() => Current = ManualLoadStep.Idle;
}
