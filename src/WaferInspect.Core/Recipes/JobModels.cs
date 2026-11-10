namespace WaferInspect.Core.Recipes;

/// <summary>晶圆类型 3 枚举（BR-16）：无图案 / 有图案无 Mark / 有图案有 Mark（默认）。</summary>
public enum WaferPatternType
{
    NoPattern,
    PatternWithoutMark,
    PatternWithMark
}

/// <summary>晶圆尺寸（F-3.2.1-08，必填）。</summary>
public enum WaferSize
{
    Inch6,
    Inch8,
    Inch12
}

/// <summary>
/// Map 文件类型（F-3.2.1-15）。BF-78：类型必须与实际文件格式匹配，否则解析失败；
/// 当前原型仅实现 <see cref="Txt"/> / <see cref="Csv"/> 解析器，`Xml` / `Sinf` 保留枚举位并明确报错。
/// </summary>
public enum MapFileType
{
    Txt,
    Csv,
    Xml,
    Sinf
}

/// <summary>行列坐标增长方向（F-3.2.1-16）。</summary>
public enum AxisDirection
{
    Positive,
    Negative
}

/// <summary>OCR 位置：正面 / 背面（F-3.2.1-21）。</summary>
public enum OcrPosition
{
    Front,
    Back
}

/// <summary>
/// 回溯图存储策略（BR-15）：Job 级设置**覆盖**系统设置中的全局回溯图开关；
/// `null`（未勾选）表示不覆盖、继承全局。
/// </summary>
public enum TraceImagePolicy
{
    Upload,
    DoNotUpload,
    UploadDefectOnly
}

/// <summary>枚举的中文呈现文本，界面与日志共用一份，避免多处硬编码（X01 术语统一）。</summary>
public static class JobEnumText
{
    public static string Text(this WaferPatternType value) => value switch
    {
        WaferPatternType.NoPattern => "无图案",
        WaferPatternType.PatternWithoutMark => "有图案无 Mark",
        WaferPatternType.PatternWithMark => "有图案有 Mark",
        _ => value.ToString()
    };

    public static string Text(this WaferSize value) => value switch
    {
        WaferSize.Inch6 => "6 寸",
        WaferSize.Inch8 => "8 寸",
        WaferSize.Inch12 => "12 寸",
        _ => value.ToString()
    };

    public static string Text(this MapFileType value) => value switch
    {
        MapFileType.Txt => "TXT",
        MapFileType.Csv => "CSV",
        MapFileType.Xml => "XML",
        MapFileType.Sinf => "SINF",
        _ => value.ToString()
    };

    public static string Text(this AxisDirection value) => value == AxisDirection.Positive ? "正向" : "反向";

    public static string Text(this OcrPosition value) => value == OcrPosition.Front ? "正面" : "背面";

    public static string Text(this TraceImagePolicy value) => value switch
    {
        TraceImagePolicy.Upload => "上传回溯图",
        TraceImagePolicy.DoNotUpload => "不上传",
        TraceImagePolicy.UploadDefectOnly => "只上传缺陷回溯图",
        _ => value.ToString()
    };

    /// <summary>Map 文件类型对应的标准扩展名（BR-78 匹配判定）。</summary>
    public static string Extension(this MapFileType value) => value switch
    {
        MapFileType.Txt => ".txt",
        MapFileType.Csv => ".csv",
        MapFileType.Xml => ".xml",
        MapFileType.Sinf => ".sinf",
        _ => string.Empty
    };
}

/// <summary>
/// Job（大配方）公共参数，对应 `F-3.2.1-06~28` 共 23 个字段组。
/// 「推荐忽略」类字段（`F-3.2.1-22~27`）按级 C **全部保留**，语义待 `T-004` 闭环，实现处已有扩展点。
/// </summary>
public sealed class JobDefinition
{
    public string JobId { get; set; } = string.Empty;

    /// <summary>F-3.2.1-06 名称（必填）。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>F-3.2.1-07 晶圆类型（BR-16，默认「有图案有 Mark」）。</summary>
    public WaferPatternType PatternType { get; set; } = WaferPatternType.PatternWithMark;

    /// <summary>F-3.2.1-08 晶圆尺寸。</summary>
    public WaferSize Size { get; set; } = WaferSize.Inch12;

    /// <summary>F-3.2.1-09 晶圆材料：当前版本固定硅（Si），级 C 保留字段，不提供编辑。</summary>
    public string Material { get; set; } = "Si";

    /// <summary>F-3.2.1-10 晶圆厚度：默认 0，当前版本未启用（级 C 保留，界面置灰但仍可见）。</summary>
    public double ThicknessMm { get; set; }

    /// <summary>F-3.2.1-11 Die X (mm)：由 Map 文件自动加载，加载无效转手动输入。</summary>
    public double DieXmm { get; set; }

    /// <summary>F-3.2.1-12 Die Y (mm)（必填）。</summary>
    public double DieYmm { get; set; }

    /// <summary>F-3.2.1-13 Notch 方向（BR-17：默认 270°）。</summary>
    public double NotchAngle { get; set; } = 270;

    /// <summary>F-3.2.1-14 图谱选择：引用 H05 已保存的图谱类型（必填）。</summary>
    public string MapTypeId { get; set; } = string.Empty;

    /// <summary>F-3.2.1-15 Map 文件类型（BR-78 必填且必须与实际格式匹配）。</summary>
    public MapFileType MapFileType { get; set; } = MapFileType.Csv;

    /// <summary>F-3.2.1-17 Map 文件路径（必填）。</summary>
    public string MapFilePath { get; set; } = string.Empty;

    /// <summary>F-3.2.1-16 列方向。</summary>
    public AxisDirection ColumnDirection { get; set; } = AxisDirection.Positive;

    /// <summary>F-3.2.1-16 行方向。</summary>
    public AxisDirection RowDirection { get; set; } = AxisDirection.Positive;

    /// <summary>F-3.2.1-18 条码分类代码（必填，来源 Map 文件；1=良品、2=缺陷品等）。</summary>
    public string BarcodeCode { get; set; } = string.Empty;

    /// <summary>F-3.2.1-19 OCR Job（当前 Job 使用的 OCR 配方，OCR 设备接入搁置 T-029）。</summary>
    public string OcrJob { get; set; } = string.Empty;

    /// <summary>F-3.2.1-20 OCR 识别角度（字符在 Aligner 上的角度）。</summary>
    public double OcrAngle { get; set; }

    /// <summary>F-3.2.1-21 OCR 位置：正面 / 背面。</summary>
    public OcrPosition OcrPosition { get; set; } = OcrPosition.Front;

    /* ---- F-3.2.1-22~27：手册标「推荐忽略」，按级 C 保留；语义待 T-004 闭环 ---- */

    /// <summary>F-3.2.1-22 定位 Die X（指定 Map 中任意 Die 为原点）。【待确认 T-004】</summary>
    public int? LocateDieX { get; set; }

    /// <summary>F-3.2.1-22 定位 Die Y。【待确认 T-004】</summary>
    public int? LocateDieY { get; set; }

    /// <summary>F-3.2.1-23 方向（快捷设置 Map 原点）。【待确认 T-004】</summary>
    public string Orientation { get; set; } = string.Empty;

    /// <summary>F-3.2.1-24 列偏移。【待确认 T-004】</summary>
    public int ColumnOffset { get; set; }

    /// <summary>F-3.2.1-24 行偏移。【待确认 T-004】</summary>
    public int RowOffset { get; set; }

    /// <summary>F-3.2.1-25 Col/Row。【待确认 T-004】</summary>
    public string ColRow { get; set; } = string.Empty;

    /// <summary>F-3.2.1-26 X/Y。【待确认 T-004】</summary>
    public string Xy { get; set; } = string.Empty;

    /// <summary>F-3.2.1-27 Include/Exclude。【待确认 T-004】</summary>
    public string IncludeExclude { get; set; } = string.Empty;

    /// <summary>
    /// F-3.2.1-28 回溯图存储（BR-15）：勾选后覆盖系统设置的全局开关；
    /// `null` = 未勾选，继承全局。
    /// </summary>
    public TraceImagePolicy? TraceImage { get; set; }

    public JobDefinition Clone() => (JobDefinition)MemberwiseClone();
}

/// <summary>单条校验问题：字段名 + 用户可读原因（E-05：失败必须显式可见）。</summary>
public sealed record JobValidationIssue(string Field, string Message);

/// <summary>校验失败：携带逐字段问题，界面据此定位到具体控件（不得静默通过）。</summary>
public sealed class JobValidationException : Exception
{
    public JobValidationException(IReadOnlyList<JobValidationIssue> issues)
        : base(issues.Count == 0 ? "Job 校验未通过。" : issues[0].Message)
        => Issues = issues;

    public IReadOnlyList<JobValidationIssue> Issues { get; }
}

/// <summary>
/// Job 参数校验：必填项、枚举边界与 BR-78 文件格式匹配。
/// 校验口径逐条对应 `F-3.2.1-06~28` 的「必填 / 固定值 / 未启用」标注，不新增手册外规则。
/// </summary>
public static class JobValidator
{
    /// <summary>必填字段集合（界面用于标注 * 与定位首个错误）。</summary>
    public static readonly IReadOnlyList<string> RequiredFields =
    [
        nameof(JobDefinition.Name),
        nameof(JobDefinition.PatternType),
        nameof(JobDefinition.Size),
        nameof(JobDefinition.DieYmm),
        nameof(JobDefinition.MapTypeId),
        nameof(JobDefinition.MapFileType),
        nameof(JobDefinition.MapFilePath),
        nameof(JobDefinition.BarcodeCode)
    ];

    public static IReadOnlyList<JobValidationIssue> Validate(JobDefinition job)
    {
        ArgumentNullException.ThrowIfNull(job);
        var issues = new List<JobValidationIssue>();

        if (string.IsNullOrWhiteSpace(job.Name))
            issues.Add(new(nameof(JobDefinition.Name), "Job 名称必填（F-3.2.1-06）。"));

        if (!Enum.IsDefined(job.PatternType))
            issues.Add(new(nameof(JobDefinition.PatternType), "晶圆类型必须为 3 种枚举之一（BR-16）。"));

        if (!Enum.IsDefined(job.Size))
            issues.Add(new(nameof(JobDefinition.Size), "晶圆尺寸必填（F-3.2.1-08）。"));

        if (job.DieXmm <= 0)
            issues.Add(new(nameof(JobDefinition.DieXmm), "Die X 必须大于 0；Map 文件加载无效时请手动输入（F-3.2.1-11）。"));

        if (job.DieYmm <= 0)
            issues.Add(new(nameof(JobDefinition.DieYmm), "Die Y 必填且必须大于 0（F-3.2.1-12）。"));

        if (job.NotchAngle is < 0 or >= 360)
            issues.Add(new(nameof(JobDefinition.NotchAngle), "Notch 方向必须在 0~359° 之间，默认 270°（BR-17）。"));

        if (string.IsNullOrWhiteSpace(job.MapTypeId))
            issues.Add(new(nameof(JobDefinition.MapTypeId), "图谱选择必填，须引用图谱管理中已保存的图谱类型（F-3.2.1-14）。"));

        if (string.IsNullOrWhiteSpace(job.MapFilePath))
        {
            issues.Add(new(nameof(JobDefinition.MapFilePath), "Map 文件必填（F-3.2.1-17）。"));
        }
        else
        {
            // BR-78：声明的文件类型必须与实际文件格式匹配，否则解析必然失败，此处提前拦截。
            string ext = Path.GetExtension(job.MapFilePath);
            string expected = job.MapFileType.Extension();
            if (!string.Equals(ext, expected, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new(nameof(JobDefinition.MapFileType),
                    $"Map 文件类型与实际格式不匹配：声明 {job.MapFileType.Text()}，文件扩展名为 {ext}（BR-78）。"));
            }
            else if (!File.Exists(job.MapFilePath))
            {
                issues.Add(new(nameof(JobDefinition.MapFilePath), $"Map 文件不存在：{job.MapFilePath}（F-3.2.1-17）。"));
            }
        }

        if (string.IsNullOrWhiteSpace(job.BarcodeCode))
            issues.Add(new(nameof(JobDefinition.BarcodeCode), "条码分类代码必填，来源 Map 文件定义（F-3.2.1-18）。"));

        if (job.OcrAngle is < 0 or >= 360)
            issues.Add(new(nameof(JobDefinition.OcrAngle), "OCR 识别角度必须在 0~359° 之间（F-3.2.1-20）。"));

        return issues;
    }

    public static bool IsValid(JobDefinition job) => Validate(job).Count == 0;
}
