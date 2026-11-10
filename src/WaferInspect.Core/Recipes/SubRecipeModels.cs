using WaferInspect.Core.Alignment;
using WaferInspect.Core.Inspection;

namespace WaferInspect.Core.Recipes;

/// <summary>
/// 子配方（子 Recipe）向导步骤（`F-3.2.2-01~06`）。
/// **T-042 已确认（2026-11-10）**：完整顺序即手册规定的 7 步，依次为
/// 基础信息 → 拉平 → Mark → 参考图像 → ROI → 扫描区域 → 复判设置，没有更多步骤。
/// 枚举声明顺序即向导顺序，禁止重排（B-25 编排依赖此顺序）。
/// </summary>
public enum WizardStepKey
{
    BasicInfo,
    Leveling,
    Mark,
    ReferenceImage,
    Roi,
    ScanArea,
    Recheck
}

/// <summary>步骤执行状态：失败（`Failed`）禁止进入下一步（B-25）。</summary>
public enum WizardStepStatus
{
    NotStarted,
    InProgress,
    Completed,
    Failed
}

/// <summary>拉平模式（BR-22）：Die 模式（非小 Die）/ 视野模式（小 Die）。</summary>
public enum LevelingMode
{
    Die,
    FieldOfView
}

/// <summary>对位模板类型（BR-23）：形状模板（区域不宜过大）/ 灰度模板（大小无限制、创建更快）。</summary>
public enum TemplateType
{
    Shape,
    Gray
}

/// <summary>复判模式（BR-20）：离线复判 / 在线验证。</summary>
public enum RecheckMode
{
    OfflineReview,
    OnlineVerify
}

/// <summary>
/// 子配方步骤的中文标题，界面与日志共用一份（T-042 确认顺序的呈现文本，全系统唯一）。
/// </summary>
public static class WizardStepText
{
    public static string Text(this WizardStepKey step) => step switch
    {
        WizardStepKey.BasicInfo => "基础信息",
        WizardStepKey.Leveling => "拉平",
        WizardStepKey.Mark => "Mark",
        WizardStepKey.ReferenceImage => "参考图像",
        WizardStepKey.Roi => "ROI",
        WizardStepKey.ScanArea => "扫描区域",
        WizardStepKey.Recheck => "复判设置",
        _ => step.ToString()
    };

    public static string Text(this WizardStepStatus status) => status switch
    {
        WizardStepStatus.NotStarted => "未开始",
        WizardStepStatus.InProgress => "进行中",
        WizardStepStatus.Completed => "已完成",
        WizardStepStatus.Failed => "失败",
        _ => status.ToString()
    };

    public static string Text(this LevelingMode mode) => mode == LevelingMode.Die ? "Die 模式（非小 Die）" : "视野模式（小 Die）";

    public static string Text(this TemplateType type) => type == TemplateType.Shape ? "形状模板" : "灰度模板";

    public static string Text(this RecheckMode mode) => mode == RecheckMode.OfflineReview ? "离线复判" : "在线验证";
}

/// <summary>
/// 子配方「基础信息」步骤字段清单（`F-3.2.2-06`）。
/// **T-005 已确认（2026-11-10，实机截图）**，共 5 个分区 33 个字段：
/// 晶圆信息（名称/晶圆类型/晶圆尺寸/晶圆材料/晶圆厚度/Die X/Die Y/Notch 方向/
/// 图谱选择/Map 文件类型/Bin 等级设定/列方向/行方向/Map 文件/图谱 Notch 方向）＋
/// 解析脚本（解析脚本/条码/OCR Job/OCR 识别角度/OCR 位置/定位 DieX/定位 DieY/方向/
/// 列偏移/行偏移/Col/Row/X/Y/Include-Exclude/使用 Klar 圆心巡边工具）＋
/// 回溯图存储（启用 + 策略）＋ Mapping（厚度/SlotNum）＋ 抽检区设置（启用 + 设置）。
/// 新建子配方时由父 Job 公共参数继承初值（4.2.1 数据流）。
/// </summary>
public sealed class SubRecipeBasicInfo
{
    /// <summary>子配方名称（必填，Job 内唯一）。</summary>
    public string Name { get; set; } = string.Empty;

    /* ---- 晶圆信息 ---- */

    public WaferPatternType PatternType { get; set; } = WaferPatternType.PatternWithMark;

    public WaferSize Size { get; set; } = WaferSize.Inch12;

    /// <summary>晶圆材料：固定硅（级 C 保留，不提供编辑）。</summary>
    public string Material { get; set; } = "Si";

    /// <summary>晶圆厚度：默认 0，当前版本未启用（级 C 保留）。</summary>
    public double ThicknessMm { get; set; }

    public double DieXmm { get; set; }

    public double DieYmm { get; set; }

    /// <summary>Notch 方向（BR-17：默认 270°）。</summary>
    public double NotchAngle { get; set; } = 270;

    /// <summary>图谱选择：引用 H05 已保存的图谱类型（必填）。</summary>
    public string MapTypeId { get; set; } = string.Empty;

    public MapFileType MapFileType { get; set; } = MapFileType.Csv;

    /// <summary>Bin 等级设定（T-005 截图新增）：引用图谱的 Bin 等级，默认第 0 级「未分类」。</summary>
    public int BinGradeLevel { get; set; }

    public AxisDirection ColumnDirection { get; set; } = AxisDirection.Positive;

    public AxisDirection RowDirection { get; set; } = AxisDirection.Positive;

    /// <summary>Map 文件路径（必填）。</summary>
    public string MapFilePath { get; set; } = string.Empty;

    /// <summary>图谱 Notch 方向（T-005 截图新增，红色 * 必填，0~359°）。</summary>
    public double MapNotchAngle { get; set; } = 270;

    /* ---- 解析脚本 ---- */

    /// <summary>解析脚本路径（T-005 截图新增，配合「脚本配置」按钮；解析器接入属后续增量）。</summary>
    public string ParseScriptPath { get; set; } = string.Empty;

    /// <summary>条码分类代码（必填，来源 Map 文件）。</summary>
    public string BarcodeCode { get; set; } = string.Empty;

    public string OcrJob { get; set; } = string.Empty;

    public double OcrAngle { get; set; }

    public OcrPosition OcrPosition { get; set; } = OcrPosition.Front;

    /// <summary>定位 Die X（推荐忽略，级 C 保留；语义待 T-004）。</summary>
    public int? LocateDieX { get; set; }

    /// <summary>定位 Die Y（推荐忽略，级 C 保留；语义待 T-004）。</summary>
    public int? LocateDieY { get; set; }

    /// <summary>方向（推荐忽略，级 C 保留；语义待 T-004）。</summary>
    public string Orientation { get; set; } = string.Empty;

    public int ColumnOffset { get; set; }

    public int RowOffset { get; set; }

    public string ColRow { get; set; } = string.Empty;

    public string Xy { get; set; } = string.Empty;

    public string IncludeExclude { get; set; } = string.Empty;

    /// <summary>使用 Klar 圆心巡边工具（T-005 截图新增，复选框）。</summary>
    public bool UseKlarCenterTool { get; set; }

    /* ---- 回溯图存储 ---- */

    /// <summary>回溯图存储（BR-15）：null = 未勾选，继承全局。</summary>
    public TraceImagePolicy? TraceImage { get; set; }

    /* ---- Mapping ---- */

    /// <summary>Mapping 厚度（T-005 截图新增，下拉可选；0 表示未设置）。</summary>
    public double MappingThicknessMm { get; set; }

    /// <summary>Mapping SlotNum（T-005 截图新增，下拉可选；0 表示未设置）。</summary>
    public int MappingSlotNum { get; set; }

    /* ---- 抽检区设置 ---- */

    /// <summary>抽检区设置启用（T-005 截图新增，复选框；区域明细由「设置」入口维护，属 H04 ROI 数据源）。</summary>
    public bool SamplingZoneEnabled { get; set; }

    public SubRecipeBasicInfo Clone() => (SubRecipeBasicInfo)MemberwiseClone();
}

/// <summary>「拉平」步骤配置（BR-22/23/24；执行能力由 H03 提供，全系统共用模板类型与匹配分数 B-08）。</summary>
public sealed class LevelingConfig
{
    public LevelingMode Mode { get; set; } = LevelingMode.Die;

    public TemplateType TemplateType { get; set; } = TemplateType.Gray;

    /// <summary>匹配分数阈值（BR-24：高于阈值才成功，0~100）。</summary>
    public double MatchScore { get; set; } = 70;

    /// <summary>
    /// F-3.2.2-39：最近一次拉平执行是否成功（成功后才允许下一步）；null = 尚未执行。
    /// 执行链路：H03 <c>TemplateMatchingEngine</c>（B-08 唯一实现）在视窗图像上真实计算。
    /// </summary>
    public bool? LastExecutionPassed { get; set; }

    /// <summary>最近一次拉平执行的匹配分数（0~100）。</summary>
    public double LastExecutionScore { get; set; }

    /// <summary>最近一次拉平执行的图像来源标注（模拟 / 采集），显式登记不伪造。</summary>
    public string LastExecutionImageSource { get; set; } = string.Empty;

    public LevelingConfig Clone() => (LevelingConfig)MemberwiseClone();
}

/// <summary>「Mark」步骤的单个 Mark 点（BR-25/26：三点对位，至少 2 个同行或同列；坐标写入属 H03 增量）。</summary>
public sealed record MarkPoint(int Index, int Row, int Col, double PlatformX, double PlatformY);

/// <summary>「参考图像」步骤配置（图像采集由 H06 提供，此处保存引用路径与说明）。</summary>
public sealed class ReferenceImageConfig
{
    /// <summary>参考图像路径；为空表示尚未采集/未选择本地文件。</summary>
    public string ImagePath { get; set; } = string.Empty;

    /// <summary>F-3.2.2-67：参考 Die 数量（BR-29：数量越大生成耗时越长）。</summary>
    public int ReferenceDieCount { get; set; }

    /// <summary>F-3.2.2-62：视野缩放（相机视野利用率）；修改后需重新生成参考图像（BR-30）。</summary>
    public double FovZoom { get; set; } = 1;

    /// <summary>F-3.2.2-61：Block（推荐忽略，级 C 保留）。</summary>
    public string BlockParam { get; set; } = string.Empty;

    /// <summary>F-3.2.2-65：Golden 创建参数（推荐忽略，级 C 保留）。</summary>
    public string GoldenParam { get; set; } = string.Empty;

    /// <summary>F-3.2.2-67：参考图像是否已生成成功（成功后才允许下一步）。</summary>
    public bool Generated { get; set; }

    /// <summary>F-3.2.2-64/66：是否进入过 FOV 图谱手动调整。</summary>
    public bool ManualAdjusted { get; set; }

    /// <summary>B-09：Golden Die 规划结果（H03 唯一生成，H08 只读展示）。</summary>
    public GoldenDiePlan? GoldenDie { get; set; }

    public ReferenceImageConfig Clone() => (ReferenceImageConfig)MemberwiseClone();
}

/// <summary>
/// H03 Mark 点设置步骤的配置（`F-3.2.2-41~49`），随子配方持久化。
/// 「推荐忽略」项（倍镜 / 三点 / 2+1）按级 C 全量保留（E-01）。
/// </summary>
public sealed class MarkSettingsConfig
{
    /// <summary>BR-27：缩略图比例必须为 1/2ⁿ，默认 1/4。</summary>
    public double ThumbnailRatio { get; set; } = 0.25;

    /// <summary>BR-28：缩略图倍镜与左侧倍镜一致，维持默认（推荐忽略，级 C 保留）。</summary>
    public string ThumbnailMagnifier { get; set; } = "默认";

    /// <summary>F-3.2.2-43：缩略图扫描轴速度（维持默认）。</summary>
    public double ScanAxisSpeed { get; set; } = 100;

    /// <summary>F-3.2.2-49：拉平方向；三点 / 2+1 为推荐忽略，级 C 保留。</summary>
    public MarkAlignmentMethod Method { get; set; } = MarkAlignmentMethod.Horizontal;

    /// <summary>F-3.2.2-46：Map 定位是否已确定（锁定后不可再移动网格）。</summary>
    public bool GridLocked { get; set; }

    /// <summary>Map 定位网格偏移（F-3.2.2-45 手动拖动的累计量）。</summary>
    public double GridOffsetX { get; set; }
    public double GridOffsetY { get; set; }

    public MarkSettingsConfig Clone() => (MarkSettingsConfig)MemberwiseClone();
}

/// <summary>
/// 「ROI」步骤的区域定义（AC-P2-03：ROI 与扫描区域在向导与参数验证中是同一份数据，H04 唯一数据源）。
/// 运行时权威数据是 <see cref="IRoiScanAreaStore"/>；本记录只是随 Recipe 持久化的投影。
/// </summary>
public sealed record RoiZone(int Index, string Name, double X, double Y, double Width, double Height);

/// <summary>「扫描区域」步骤配置（AC-P2-03：与 ROI 共用 H04 单一数据源，本步骤保存区域引用）。</summary>
public sealed class ScanAreaConfig
{
    /// <summary>扫描区域覆盖的 ROI 索引集合；空表示全片扫描。</summary>
    public IReadOnlyList<int> RoiIndexes { get; set; } = [];

    /// <summary>全片扫描（历史默认行为：未圈 ROI 时扫描整个晶圆）。</summary>
    public bool FullWafer { get; set; } = true;

    public ScanAreaConfig Clone() => (ScanAreaConfig)MemberwiseClone();
}

/// <summary>「复判设置」步骤（R-H02-07 / F-3.2.2-80~82 / BR-20）：写入 Recipe，运行时供 H08 读取。</summary>
public sealed class RecheckConfig
{
    /// <summary>复判模式（BR-20：默认离线复判）。</summary>
    public RecheckMode Mode { get; set; } = RecheckMode.OfflineReview;

    /// <summary>相机类型（BR-20：默认复查相机；当前历史系统仅此一种）。</summary>
    public bool UseReviewCamera { get; set; } = true;

    public RecheckConfig Clone() => (RecheckConfig)MemberwiseClone();
}

/// <summary>向导中单个步骤的执行状态（BR-19 可重入；B-25 失败门禁）。</summary>
public sealed class WizardStepState
{
    public WizardStepKey Key { get; init; }

    public WizardStepStatus Status { get; set; } = WizardStepStatus.NotStarted;

    /// <summary>失败原因（B-25：失败必须显式可见）；成功后清空。</summary>
    public string FailureReason { get; set; } = string.Empty;
}

/// <summary>子配方（子 Recipe）：归属某个 Job，承载 7 步向导的全部配置与步骤状态。</summary>
public sealed class SubRecipe
{
    public string Id { get; set; } = string.Empty;

    public string ParentJobId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>T-005 基础信息（步骤 1）。</summary>
    public SubRecipeBasicInfo BasicInfo { get; set; } = new();

    /// <summary>步骤 2 拉平（BR-22/23/24）。</summary>
    public LevelingConfig Leveling { get; set; } = new();

    /// <summary>步骤 3 Mark（BR-25/26：三点对位）。</summary>
    public IReadOnlyList<MarkPoint> Marks { get; set; } = [];

    /// <summary>步骤 3 Mark 设置（F-3.2.2-41~49：缩略图/方向/网格定位，H03）。</summary>
    public MarkSettingsConfig MarkSettings { get; set; } = new();

    /// <summary>步骤 4 参考图像。</summary>
    public ReferenceImageConfig ReferenceImage { get; set; } = new();

    /// <summary>步骤 5 ROI。</summary>
    public IReadOnlyList<RoiZone> RoiZones { get; set; } = [];

    /// <summary>步骤 6 扫描区域（AC-P2-03）。</summary>
    public ScanAreaConfig ScanArea { get; set; } = new();

    /// <summary>
    /// H04 检测配置快照（R-H04-01「写入 Recipe」）：检测设置列表、扫描区域、抽检 Die、
    /// 可扩展算法参数集合与 BR-40 自动保存版本。运行时权威数据在 <see cref="IRoiScanAreaStore"/>。
    /// </summary>
    public InspectionConfig Inspection { get; set; } = new();

    /// <summary>步骤 7 复判设置（R-H02-07）。</summary>
    public RecheckConfig Recheck { get; set; } = new();

    /// <summary>7 步执行状态（T-042 顺序），随子配方持久化，重开向导不丢失。</summary>
    public IReadOnlyList<WizardStepState> Steps { get; set; }
        = Enum.GetValues<WizardStepKey>().Select(k => new WizardStepState { Key = k }).ToList();

    public SubRecipe Clone()
    {
        // 局部变量避免属性名 Inspection 与 WaferInspect.Core.Inspection 命名空间在初始化器中产生歧义。
        var inspection = Inspection.Clone();
        return new()
        {
            Id = Id,
            ParentJobId = ParentJobId,
            Name = Name,
            BasicInfo = BasicInfo.Clone(),
            Leveling = Leveling.Clone(),
            Marks = Marks.ToList(),
            MarkSettings = MarkSettings.Clone(),
            ReferenceImage = ReferenceImage.Clone(),
            RoiZones = RoiZones.ToList(),
            ScanArea = ScanArea.Clone(),
            Inspection = inspection,
            Recheck = Recheck.Clone(),
            Steps = Steps.Select(s => new WizardStepState { Key = s.Key, Status = s.Status, FailureReason = s.FailureReason }).ToList()
        };
    }
}

/// <summary>基础信息校验问题（与 Job 校验同构，便于界面统一呈现）。</summary>
public sealed record SubRecipeValidationIssue(string Field, string Message);

/// <summary>
/// 子配方基础信息校验（T-005 字段口径）：
/// 必填 = 名称 / 晶圆类型 / 晶圆尺寸 / Die Y / 图谱选择 / Map 文件类型 / Map 文件 /
/// 图谱 Notch 方向 / 条码（截图红色 * 与 4.2.1 必填口径取并集）；BR-16/17/78 复用 Job 同一规则。
/// </summary>
public static class SubRecipeValidator
{
    public static readonly IReadOnlyList<string> RequiredFields =
    [
        nameof(SubRecipeBasicInfo.Name),
        nameof(SubRecipeBasicInfo.DieYmm),
        nameof(SubRecipeBasicInfo.MapTypeId),
        nameof(SubRecipeBasicInfo.MapFilePath),
        nameof(SubRecipeBasicInfo.MapNotchAngle),
        nameof(SubRecipeBasicInfo.BarcodeCode)
    ];

    public static IReadOnlyList<SubRecipeValidationIssue> Validate(SubRecipeBasicInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        var issues = new List<SubRecipeValidationIssue>();

        if (string.IsNullOrWhiteSpace(info.Name))
            issues.Add(new(nameof(SubRecipeBasicInfo.Name), "子配方名称必填（F-3.2.2-06）"));

        if (!Enum.IsDefined(info.PatternType))
            issues.Add(new(nameof(SubRecipeBasicInfo.PatternType), "晶圆类型必须为 3 种枚举之一（BR-16）"));

        if (!Enum.IsDefined(info.Size))
            issues.Add(new(nameof(SubRecipeBasicInfo.Size), "晶圆尺寸必填（F-3.2.1-08）"));

        if (info.DieYmm <= 0)
            issues.Add(new(nameof(SubRecipeBasicInfo.DieYmm), "Die Y 必填且必须大于 0（F-3.2.1-12）"));

        if (info.NotchAngle is < 0 or >= 360)
            issues.Add(new(nameof(SubRecipeBasicInfo.NotchAngle), "Notch 方向必须在 0~359° 之间，默认 270°（BR-17）"));

        if (string.IsNullOrWhiteSpace(info.MapTypeId))
            issues.Add(new(nameof(SubRecipeBasicInfo.MapTypeId), "图谱选择必填（F-3.2.1-14）"));

        if (info.MapNotchAngle is < 0 or >= 360)
            issues.Add(new(nameof(SubRecipeBasicInfo.MapNotchAngle), "图谱 Notch 方向必填且必须在 0~359° 之间（T-005 截图必填项）"));

        if (string.IsNullOrWhiteSpace(info.MapFilePath))
        {
            issues.Add(new(nameof(SubRecipeBasicInfo.MapFilePath), "Map 文件必填（F-3.2.1-17）"));
        }
        else
        {
            // BR-78：类型与实际格式匹配，与 JobValidator 同一判定口径。
            string ext = Path.GetExtension(info.MapFilePath);
            string expected = info.MapFileType.Extension();
            if (!string.Equals(ext, expected, StringComparison.OrdinalIgnoreCase))
                issues.Add(new(nameof(SubRecipeBasicInfo.MapFileType),
                    $"Map 文件类型与实际格式不匹配：声明 {info.MapFileType.Text()}，文件扩展名为 {ext}（BR-78）"));
        }

        if (string.IsNullOrWhiteSpace(info.BarcodeCode))
            issues.Add(new(nameof(SubRecipeBasicInfo.BarcodeCode), "条码分类代码必填（F-3.2.1-18）"));

        if (info.OcrAngle is < 0 or >= 360)
            issues.Add(new(nameof(SubRecipeBasicInfo.OcrAngle), "OCR 识别角度必须在 0~359° 之间（F-3.2.1-20）"));

        return issues;
    }

    public static bool IsValid(SubRecipeBasicInfo info) => Validate(info).Count == 0;

    /// <summary>拉平配置校验（BR-24：匹配分数 0~100）。</summary>
    public static IReadOnlyList<SubRecipeValidationIssue> ValidateLeveling(LevelingConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var issues = new List<SubRecipeValidationIssue>();
        if (config.MatchScore is < 0 or > 100)
            issues.Add(new(nameof(LevelingConfig.MatchScore), "匹配分数必须在 0~100 之间，高于阈值才判定成功（BR-24）"));
        return issues;
    }

    /// <summary>
    /// Mark 点结构校验（BR-25/26）：必须为 3 个点；
    /// 水平拉平要求 2 点同行、竖直要求同列 —— 即**至少 2 个点同行或同列**。
    /// 图形化框选与坐标写入属 H03 增量，本规则先行保证数据结构合法。
    /// </summary>
    public static IReadOnlyList<SubRecipeValidationIssue> ValidateMarks(IReadOnlyList<MarkPoint> marks)
    {
        ArgumentNullException.ThrowIfNull(marks);
        var issues = new List<SubRecipeValidationIssue>();

        if (marks.Count != 3)
        {
            issues.Add(new("Marks", "必须设置 3 个 Mark 点（BR-25：三点对位）"));
            return issues;
        }

        bool sameRow = marks.GroupBy(m => m.Row).Any(g => g.Count() >= 2);
        bool sameCol = marks.GroupBy(m => m.Col).Any(g => g.Count() >= 2);
        if (!sameRow && !sameCol)
            issues.Add(new("Marks", "三个 Mark 点中至少 2 个须同行或同列（BR-26：水平拉平 2 点同行、竖直 2 点同列）"));

        return issues;
    }

    /// <summary>ROI 区域校验（AC-P2-03：H04 单一数据源的结构合法性）。</summary>
    public static IReadOnlyList<SubRecipeValidationIssue> ValidateRoiZones(IReadOnlyList<RoiZone> zones)
    {
        ArgumentNullException.ThrowIfNull(zones);
        var issues = new List<SubRecipeValidationIssue>();

        if (zones.Count == 0)
        {
            issues.Add(new("RoiZones", "至少需要 1 个 ROI 区域（全片扫描请在「扫描区域」步骤勾选全片）"));
            return issues;
        }

        for (int i = 0; i < zones.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(zones[i].Name))
                issues.Add(new("RoiZones", $"ROI {i + 1} 名称必填"));
            if (zones[i].Width <= 0 || zones[i].Height <= 0)
                issues.Add(new("RoiZones", $"ROI {i + 1} 宽度与高度必须大于 0"));
        }

        return issues;
    }
}
