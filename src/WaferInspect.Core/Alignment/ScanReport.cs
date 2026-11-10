using WaferInspect.Core.Recipes;

namespace WaferInspect.Core.Alignment;

/// <summary>
/// 扫描执行 / 对位结果（H06）。<see cref="IsOffline"/> 标志区分来源：离线演算恒为 true，
/// 真实设备执行待 T-029/T-035 闭环后为 false。两种来源共用本记录，便于调用方与 UI 统一处理，
/// 并对非真实设备结果诚实标注（E-08 / E-10）。
/// </summary>
public sealed record ScanReport
{
    /// <summary>是否为离线确定性演算：离线恒为 true，真实设备为 false（E-08/E-10 诚实边界）。</summary>
    public bool IsOffline { get; init; }

    /// <summary>合成 FOV 种子（确定性复现；真实设备时为 0）。</summary>
    public int FovSeed { get; init; }

    /// <summary>FOV 宽（像素）。</summary>
    public int FovWidth { get; init; }

    /// <summary>FOV 高（像素）。</summary>
    public int FovHeight { get; init; }

    /// <summary>使用的模板类型（当前固定灰度模板）。</summary>
    public TemplateType TemplateType { get; init; }

    /// <summary>B-08 引擎真实算出的匹配分数（0~100）。</summary>
    public double MatchScore { get; init; }

    /// <summary>BR-24：分数严格大于阈值则通过。</summary>
    public bool MatchPassed { get; init; }

    /// <summary>本次匹配阈值。</summary>
    public double MatchThreshold { get; init; }

    /// <summary>引擎给出的建议（如框选无效、超界）。</summary>
    public IReadOnlyList<string> Advisories { get; init; } = [];

    /// <summary>H03 对位解出的平台 X 坐标（mm）。</summary>
    public double PlatformX { get; init; }

    /// <summary>H03 对位解出的平台 Y 坐标（mm）。</summary>
    public double PlatformY { get; init; }

    /// <summary>参与对位的参考 Die 行（中心 Die）。</summary>
    public int ReferenceRow { get; init; }

    /// <summary>参与对位的参考 Die 列（中心 Die）。</summary>
    public int ReferenceColumn { get; init; }

    /// <summary>Map 网格列数。</summary>
    public int Columns { get; init; }

    /// <summary>Map 网格行数。</summary>
    public int Rows { get; init; }

    /// <summary>报告生成时间。</summary>
    public DateTime GeneratedAt { get; init; }

    /// <summary>诚实说明（离线演算注明非真实设备；设备执行注明真实来源）。</summary>
    public string Note { get; init; } = "";
}
