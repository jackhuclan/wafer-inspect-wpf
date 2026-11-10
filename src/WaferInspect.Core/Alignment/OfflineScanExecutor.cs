using WaferInspect.Core.Recipes;

namespace WaferInspect.Core.Alignment;

/// <summary>
/// H06 离线确定性扫描执行 / 对位沙盒（P6 1.8.0）。
/// 真实调用 B-08 唯一匹配引擎（<see cref="TemplateMatchingEngine"/>）与 H03 对位服务（<see cref="MarkAlignmentService"/>），
/// 但视窗 FOV 由 <see cref="SyntheticFovImage"/> 合成——未接入真实相机采集 / AI 推理（T-029 / T-035 搁置）。
/// </summary>
/// <remarks>
/// 【诚实边界】本类是离线演算沙盒，<b>不是</b> <see cref="IDeviceGateway"/> 生产适配器（AGENTS.md：T-029 未闭环前不得注册
/// 虚构生产网关）；所有输出 <see cref="OfflineScanReport.IsOffline"/> 恒为 true，并附带离线说明，绝不伪造设备采集 /
/// AI 推理结果（E-08 / E-10）。真实设备扫描需待 T-029 / T-035 闭环后替换 FOV 来源与匹配输入。
/// </remarks>
public sealed class OfflineScanExecutor
{
    private readonly TemplateMatchingEngine _matcher;
    private readonly MarkAlignmentService _alignment;

    /// <summary>构造：注入 B-08 唯一匹配引擎与 H03 对位服务（禁止另写匹配 / 对位逻辑）。</summary>
    public OfflineScanExecutor(TemplateMatchingEngine matcher, MarkAlignmentService alignment)
    {
        _matcher = matcher ?? throw new ArgumentNullException(nameof(matcher));
        _alignment = alignment ?? throw new ArgumentNullException(nameof(alignment));
    }

    /// <summary>
    /// 运行一次离线扫描演算：合成 FOV → 取中心 Die 特征块为模板 → B-08 引擎算匹配分 → H03 对位算平台坐标。
    /// </summary>
    /// <param name="columns">Map 网格列数（需先有 Map）。</param>
    /// <param name="rows">Map 网格行数。</param>
    /// <param name="matchThreshold">匹配分数阈值（0~100，BR-24 高于阈值才判成功）。</param>
    /// <param name="seed">合成 FOV 种子（确定性，可复现）。</param>
    /// <param name="dieXmm">单 Die 宽（mm），对位换算用。</param>
    /// <param name="dieYmm">单 Die 高（mm），对位换算用。</param>
    public OfflineScanReport Execute(
        int columns, int rows, double matchThreshold = 75, int? seed = null, double dieXmm = 10, double dieYmm = 10)
    {
        if (columns <= 0 || rows <= 0)
            throw new ArgumentOutOfRangeException(nameof(columns), "列/行必须大于 0（需先有 Map 网格，BR-45）。");
        if (matchThreshold is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(matchThreshold), "匹配分数阈值须在 0~100 之间（BR-24）。");
        if (dieXmm <= 0) throw new ArgumentOutOfRangeException(nameof(dieXmm), "Die 宽必须大于 0。");
        if (dieYmm <= 0) throw new ArgumentOutOfRangeException(nameof(dieYmm), "Die 高必须大于 0。");

        int s = seed ?? 20261110;
        var fov = SyntheticFovImage.Generate(seed: s);

        // 取中心 Die 的 20x20 模板区：覆盖 die 基底（150）与部分特征块（220），
        // 保证方差 > 30（B-08 引擎 NoContrastVarianceThreshold），避免被判「灰度无对比」返回 0 分。
        int pitch = SyntheticFovImage.DiePitch;
        int centerDieX = (fov.Width / 2 / pitch) * pitch;
        int centerDieY = (fov.Height / 2 / pitch) * pitch;
        const int templateSpan = 20;
        var region = new TemplateRegion(centerDieX, centerDieY, templateSpan, templateSpan);

        // B-08 唯一引擎真实算分（灰度模板，合成 FOV 与真实 FOV 走同一路径）。
        var outcome = _matcher.Match(fov, region, TemplateType.Gray, matchThreshold);

        // H03 对位：取中心 Die 映射平台（Chuck）坐标。
        var grid = new MapGridState();
        grid.Load(columns, rows);
        int refRow = rows / 2;
        int refCol = columns / 2;
        var pt = _alignment.DieToPlatform(refRow, refCol, dieXmm, dieYmm, grid);

        return new OfflineScanReport
        {
            IsOffline = true,
            FovSeed = s,
            FovWidth = fov.Width,
            FovHeight = fov.Height,
            TemplateType = TemplateType.Gray,
            MatchScore = outcome.Score,
            MatchPassed = outcome.Passed,
            MatchThreshold = matchThreshold,
            Advisories = outcome.Advisories,
            PlatformX = pt.Xmm,
            PlatformY = pt.Ymm,
            ReferenceRow = refRow,
            ReferenceColumn = refCol,
            Columns = columns,
            Rows = rows,
            GeneratedAt = DateTime.Now,
            Note = "确定性离线演算：FOV 由 SyntheticFovImage 合成，未接入真实相机采集（T-029）；" +
                   "匹配走 B-08 唯一引擎、对位走 H03 MarkAlignmentService。本结果不代表真实设备扫描 / AI 推理（E-08/E-10）。"
        };
    }

    /// <summary>
    /// 步骤 8「降低匹配分数重试」：在<b>同一合成 FOV 与模板</b>上用更低阈值真实重跑 B-08 引擎（不伪造、可复现）。
    /// </summary>
    /// <param name="previous">上一次离线演算报告（取其 FOV 种子与网格，保证同一视窗）。</param>
    /// <param name="lowerThreshold">降低后的阈值（0~100）。</param>
    public OfflineScanReport RetryWithLowerThreshold(OfflineScanReport previous, double lowerThreshold)
    {
        if (previous == null) throw new ArgumentNullException(nameof(previous));
        if (lowerThreshold is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(lowerThreshold), "匹配分数阈值须在 0~100 之间（BR-24）。");

        return Execute(previous.Columns, previous.Rows, lowerThreshold, previous.FovSeed);
    }
}

/// <summary>
/// H06 离线扫描演算结果（P6 1.8.0）。<see cref="IsOffline"/> 恒为 true，明确标注非真实设备结果。
/// </summary>
public sealed record OfflineScanReport
{
    /// <summary>恒为 true：本结果来自离线合成 FOV，未接入真实设备（E-08/E-10 诚实边界）。</summary>
    public bool IsOffline { get; init; }

    /// <summary>合成 FOV 种子（确定性复现）。</summary>
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

    /// <summary>Map 网格列数（重算时复用，保证同一视窗）。</summary>
    public int Columns { get; init; }

    /// <summary>Map 网格行数。</summary>
    public int Rows { get; init; }

    /// <summary>报告生成时间。</summary>
    public DateTime GeneratedAt { get; init; }

    /// <summary>离线演算诚实说明（非真实设备结果）。</summary>
    public string Note { get; init; } = "";
}
