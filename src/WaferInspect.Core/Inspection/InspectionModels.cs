using WaferInspect.Core.Recipes;
using WaferInspect.Core.Run;

namespace WaferInspect.Core.Inspection;

/// <summary>
/// H04「检测算法与 ROI 配置」的领域模型（`R-H04-01~06`）。
/// B-01 / B-02：ROI 设置（`UI-3.2.2-H`）与扫描区域（`UI-3.2.2-I` / `UI-3.8.1`）是
/// 同一份配置的「编辑态」与「调试态」，本命名空间是**唯一数据定义处**，
/// 向导步骤 5/6、参数验证页、H01 中止调参均通过 <see cref="IRoiScanAreaStore"/> 读写，禁止各自复制一份。
/// </summary>

/// <summary>区域划分（F-3.2.2-68）：Pad 检测选 Pad，其他所有检测选 Zone。</summary>
public enum RoiTargetKind
{
    Zone,
    Pad
}

/// <summary>区域提取算法（F-3.2.2-71）：模板匹配 / 二值化两种自动特征提取。</summary>
public enum RegionExtraction
{
    TemplateMatching,
    Binarization
}

/// <summary>Golden 图像明/暗辅助算法调参（F-3.2.2-73）。</summary>
public enum GoldenImageTone
{
    Bright,
    Dark
}

/// <summary>参数验证的测试范围（F-3.8.1-05/06/07）。</summary>
public enum VerificationScope
{
    SingleImage,
    AllImages,
    Prescan
}

/// <summary>区域 / 检测项 / 扫描区域枚举的中文呈现文本，界面与日志共用一份。</summary>
public static class InspectionText
{
    public static string Text(this RoiTargetKind kind) => kind == RoiTargetKind.Pad ? "Pad" : "Zone";

    public static string Text(this RegionExtraction extraction)
        => extraction == RegionExtraction.TemplateMatching ? "模板匹配" : "二值化";

    public static string Text(this GoldenImageTone tone) => tone == GoldenImageTone.Bright ? "亮 Golden" : "暗 Golden";

    public static string Text(this VerificationScope scope) => scope switch
    {
        VerificationScope.SingleImage => "单幅图片测试",
        VerificationScope.AllImages => "全部图片测试",
        VerificationScope.Prescan => "预扫描",
        _ => scope.ToString()
    };

    /// <summary>BR-35：检测算法参数类型常用 Surface 算法，或按实际选择其他。</summary>
    public const string DefaultParameterType = "Surface";

    /// <summary>设备动作统一提示（T-029 搁置，不伪造下发成功）。</summary>
    public const string DeferredDeviceAction = "[搁置] 未下发（T-029）";
}

/// <summary>
/// Map 图上的矩形区域（Die 网格坐标，**1-based**：X = 起始列、Y = 起始行，宽高为 Die 个数）。
/// 与缺陷列表 Row/Column 同一口径；ROI 区域、有效/屏蔽区域与扫描区域共用本坐标系，
/// 保证 AC-H04-01/04 的区域判定不出现第二套换算。
/// </summary>
public sealed record MapRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;

    public bool Contains(int column, int row)
        => column >= X && column < Right && row >= Y && row < Bottom;

    public static MapRect Whole(int columns, int rows) => new(1, 1, columns, rows);

    public static MapRect From(RoiZone zone) => new(zone.X, zone.Y, zone.Width, zone.Height);
}

/// <summary>Die 网格坐标（1-based，与缺陷列表 Row/Column 口径一致）。</summary>
public sealed record DieCoordinate(int Row, int Column);

/// <summary>
/// 算法参数项（T-014 / E-08 / R-H04-06）：具体参数清单待实机确认，
/// 因此实现为**可扩展的键值集合**，界面用动态列表呈现，禁止用 3 个固定滑块替代。
/// 默认项仅作占位，全部标注待确认，不影响后续按实机清单增删。
/// </summary>
public sealed record AlgorithmParameter(
    string Key,
    string Group,
    string Value,
    string Unit = "",
    string ValueType = "数值",
    string Description = "");

/// <summary>T-014 默认占位参数目录（检测 / 量测 / 全局三组）；可增删改，非实机最终清单。</summary>
public static class AlgorithmParameterCatalog
{
    public const string DetectionGroup = "检测算法";
    public const string MetrologyGroup = "量测算法";
    public const string GlobalGroup = "全局参数";

    private const string Pending = "【待确认 T-014】默认占位参数项，按实机算法文档增删";

    public static IReadOnlyList<AlgorithmParameter> Defaults { get; } =
    [
        new("Detection.Threshold", DetectionGroup, "120", "灰度", "数值", Pending),
        new("Detection.MinArea", DetectionGroup, "16", "像素", "数值", Pending),
        new("Detection.Sensitivity", DetectionGroup, "0.85", "", "数值", Pending),
        new("Metrology.Scale", MetrologyGroup, "1.0", "μm/px", "数值", Pending),
        new("Metrology.EdgeThreshold", MetrologyGroup, "35", "灰度", "数值", Pending),
        new("Metrology.FitOrder", MetrologyGroup, "2", "阶", "整数", Pending),
        new("Global.ParameterType", GlobalGroup, InspectionText.DefaultParameterType, "", "文本",
            "BR-35：常用 Surface 算法，或按实际选择其他；【待确认 T-014】"),
        new("Global.ReviewThreshold", GlobalGroup, "0.60", "", "数值", Pending),
        new("Global.WorkerCount", GlobalGroup, "4", "线程", "整数", Pending)
    ];

    public static string ParameterTypeOf(IEnumerable<AlgorithmParameter> parameters)
        => parameters.FirstOrDefault(p => p.Key == "Global.ParameterType")?.Value
           ?? InspectionText.DefaultParameterType;
}

/// <summary>
/// 检测设置列表项（F-3.2.2-69）：每项为独立检测单元。
/// 字段：①Zone/Pad 名称 ②算法类型 ③Golden Die 模板中的检测区域；
/// 另含 F-3.2.2-71/72/74/75 的区域提取、有效/屏蔽区域、Golden 明暗、参数类型与参数集合。
/// <c>Saved</c> 对应「每项保存后才算完成」（4.4.1 状态与反馈）。
/// </summary>
public sealed class DetectionItem
{
    public string Id { get; set; } = string.Empty;

    /// <summary>①Zone/Pad 名称。</summary>
    public string Name { get; set; } = string.Empty;

    public RoiTargetKind Kind { get; set; } = RoiTargetKind.Zone;

    /// <summary>②算法类型。</summary>
    public string AlgorithmType { get; set; } = InspectionText.DefaultParameterType;

    /// <summary>③Golden Die 模板中的检测区域。</summary>
    public MapRect GoldenTemplateRegion { get; set; } = new(1, 1, 1, 1);

    /// <summary>引用的 ROI 区域索引（F-3.2.2-70 手动绘制检测区域）。</summary>
    public int? RoiZoneIndex { get; set; }

    /// <summary>BR-35：参数类型常用 Surface，或按实际选择其他。</summary>
    public string ParameterType { get; set; } = InspectionText.DefaultParameterType;

    public RegionExtraction Extraction { get; set; } = RegionExtraction.TemplateMatching;

    /// <summary>
    /// BR-36：有效 / 屏蔽区域**仅**限制区域提取算法生效范围，不影响检测本身。
    /// 因此这两个字段只参与 <see cref="RegionExtraction"/> 的输入裁剪，不参与扫描区域与检测结果生成。
    /// </summary>
    public MapRect? EffectiveRegion { get; set; }
    public MapRect? MaskedRegion { get; set; }

    public GoldenImageTone GoldenTone { get; set; } = GoldenImageTone.Bright;

    public IReadOnlyList<AlgorithmParameter> Parameters { get; set; } = [];

    /// <summary>右下角「保存」后才为 true（F-3.2.2-75 第 6 步）；未保存项禁止完成 ROI 步骤。</summary>
    public bool Saved { get; set; }

    public DetectionItem Clone() => new()
    {
        Id = Id,
        Name = Name,
        Kind = Kind,
        AlgorithmType = AlgorithmType,
        GoldenTemplateRegion = GoldenTemplateRegion,
        RoiZoneIndex = RoiZoneIndex,
        ParameterType = ParameterType,
        Extraction = Extraction,
        EffectiveRegion = EffectiveRegion,
        MaskedRegion = MaskedRegion,
        GoldenTone = GoldenTone,
        Parameters = Parameters.ToList(),
        Saved = Saved
    };
}

/// <summary>扫描区域（F-3.2.2-77 / F-3.8.1-03）：Map 图上绘制的局部检测区域，可单个或多个。</summary>
public sealed class ScanRegion
{
    public string Name { get; set; } = string.Empty;

    public MapRect Rect { get; set; } = new(1, 1, 1, 1);

    /// <summary>来源：Map 圈选 / ROI 引用（AC-P2-03：两者是同一份数据）。</summary>
    public string Source { get; set; } = "Map 圈选";

    public ScanRegion Clone() => new() { Name = Name, Rect = Rect, Source = Source };
}

/// <summary>抽检 Die（BR-38：双击选单个 Die，可设单个或多个）。</summary>
public sealed record SamplingDie(int Row, int Column);

/// <summary>
/// H04 配置快照：随子配方持久化（R-H04-01「写入 Recipe」）。
/// 运行时权威数据在 <see cref="IRoiScanAreaStore"/>，本快照只是落盘载体，两处界面均不得直接改它。
/// </summary>
public sealed class InspectionConfig
{
    public IReadOnlyList<DetectionItem> DetectionItems { get; set; } = [];

    public IReadOnlyList<ScanRegion> ScanRegions { get; set; } = [];

    public IReadOnlyList<SamplingDie> SamplingDies { get; set; } = [];

    public IReadOnlyList<AlgorithmParameter> Parameters { get; set; } = AlgorithmParameterCatalog.Defaults;

    /// <summary>BR-37：默认检测整个 Map 图区域。</summary>
    public bool FullWafer { get; set; } = true;

    /// <summary>抽检模式（F-3.2.2-79 / BR-38）。</summary>
    public bool SamplingEnabled { get; set; }

    /// <summary>F-3.8.1-02：本地图片文件夹（不访问设备）。</summary>
    public string ImageFolderPath { get; set; } = string.Empty;

    /// <summary>F-3.8.1-05/09：底部缺陷图片路径 / 选中记录对应的原始图片路径。</summary>
    public string SelectedImagePath { get; set; } = string.Empty;

    /// <summary>BR-40 关闭自动保存的累计次数（AC-H04-03 可验证）。</summary>
    public int SavedVersion { get; set; }

    public DateTimeOffset? LastSavedAt { get; set; }

    public InspectionConfig Clone() => new()
    {
        DetectionItems = DetectionItems.Select(i => i.Clone()).ToList(),
        ScanRegions = ScanRegions.Select(r => r.Clone()).ToList(),
        SamplingDies = SamplingDies.ToList(),
        Parameters = Parameters.ToList(),
        FullWafer = FullWafer,
        SamplingEnabled = SamplingEnabled,
        ImageFolderPath = ImageFolderPath,
        SelectedImagePath = SelectedImagePath,
        SavedVersion = SavedVersion,
        LastSavedAt = LastSavedAt
    };
}

/// <summary>唯一数据源的只读快照，供两个界面呈现（AC-P2-03 证明同一份数据）。</summary>
public sealed record RoiScanAreaSnapshot(
    string RecipeId,
    IReadOnlyList<RoiZone> Zones,
    IReadOnlyList<DetectionItem> DetectionItems,
    IReadOnlyList<ScanRegion> ScanRegions,
    IReadOnlyList<SamplingDie> SamplingDies,
    IReadOnlyList<AlgorithmParameter> Parameters,
    bool FullWafer,
    bool SamplingEnabled,
    string ImageFolderPath,
    string SelectedImagePath,
    int SavedVersion,
    DateTimeOffset? LastSavedAt);

/// <summary>
/// 参数验证 / 预扫描结果（F-3.8.1-05~09、BR-41/42）。
/// 真实相机采集与模型推理由 H06 / AI 平台提供（T-029 / T-035 搁置），
/// 此处为**确定性离线演算**：同一输入恒得同一结果，不伪造设备执行成功。
/// </summary>
public sealed record VerificationOutcome(
    VerificationScope Scope,
    IReadOnlyList<string> ImagePaths,
    IReadOnlyList<DieCoordinate> CoveredDies,
    IReadOnlyList<DefectRecord> Defects,
    string Summary,
    string DeviceActionNote = InspectionText.DeferredDeviceAction,
    bool IsOfflineComputation = true);
