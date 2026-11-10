namespace WaferInspect.Core.Alignment;

/// <summary>
/// 扫描执行源种类（H06）。T-029（相机采集）/ T-035（AI 推理）规范未闭环前，仅 <see cref="Offline"/> 可用；
/// <see cref="Device"/> 为预留实现位，当前不可选、不伪造（E-08 / E-10）。
/// </summary>
public enum ScanExecutorKind
{
    /// <summary>离线确定性演算：合成 FOV（SyntheticFovImage）+ B-08 唯一匹配引擎 + H03 对位；当前默认可用。</summary>
    Offline,

    /// <summary>真实设备扫描（T-029/T-035）：真实相机 FOV + AI 推理。规范待确认，预留实现位，当前不可选 / 不伪造。</summary>
    Device
}
