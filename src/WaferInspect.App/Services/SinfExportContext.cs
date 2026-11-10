using WaferInspect.Core.Maps;

namespace WaferInspectHMI.Services;

/// <summary>
/// Sinf 导出上下文：承载 Map 数据与元数据，避免在导出器内直接依赖 ViewModel，
/// 使导出器可独立单元测试（与 H08 报表内部导出先例一致）。
/// </summary>
/// <param name="Map">Wafer Map 模型（Core，唯一绘制/数据来源 B-06）。</param>
/// <param name="TypeId">当前图谱类型 Id（B-11 唯一数据源）。</param>
/// <param name="TypeName">当前图谱类型名称。</param>
/// <param name="CreateMode">创建方式（F-3.7.3-09）。</param>
/// <param name="WaferSize">晶圆尺寸（寸，BR-46）。</param>
/// <param name="ExpandFilm">是否扩膜（BR-48）。</param>
public sealed record SinfExportContext(
    MapGridModel Map,
    string TypeId,
    string TypeName,
    string CreateMode,
    string WaferSize,
    bool ExpandFilm);
