using WaferInspect.Core.Recipes;

namespace WaferInspect.Core.Alignment;

/// <summary>
/// 灰度视窗图像（0~255，行优先一维存储）。真实采集属 `H06`（设备连接搁置 `T-029`），
/// 当前由 <see cref="SyntheticFovImage"/> 确定性生成，供对位引擎在模拟图像上真实执行匹配计算。
/// </summary>
public sealed class GrayImage
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Pixels { get; }

    public GrayImage(int width, int height, byte[] pixels)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (pixels.Length != width * height) throw new ArgumentException("像素数组长度必须等于 width × height。");
        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public byte At(int x, int y) => Pixels[y * Width + x];
}

/// <summary>模板框选区域（像素坐标）。</summary>
public readonly record struct TemplateRegion(int X, int Y, int Width, int Height)
{
    public bool IsValid => Width > 0 && Height > 0;
    public long Area => (long)Width * Height;
}

/// <summary>
/// 模板匹配结果（B-08：全系统唯一实现，`H02` 配方向导与 `H05` Wafer 扫描共用同一引擎与取值口径）。
/// </summary>
public sealed record TemplateMatchOutcome
{
    /// <summary>匹配分数 0~100（BR-24：高于阈值才判定成功）。</summary>
    public double Score { get; init; }

    /// <summary>BR-24：<see cref="Score"/> 严格大于阈值。</summary>
    public bool Passed { get; init; }

    public IReadOnlyList<string> Advisories { get; init; } = [];

    public string EngineName => "H03.TemplateMatchingEngine（B-08 唯一实现）";
}

/// <summary>光照校准报告（BR-21：避免过暗丢细节 / 过亮饱和发白）。</summary>
public sealed record LightCalibrationReport
{
    public double MeanBrightness { get; init; }
    public double SaturatedFraction { get; init; }
    public IReadOnlyList<string> Advisories { get; init; } = [];

    public bool IsOk => Advisories.Count == 0;
}

/// <summary>
/// F-3.2.2-49 拉平方向。三点对位 / 2+1 对位为「推荐忽略」，按级 C **保留**（E-01）。
/// BR-26：水平拉平要求 2 点同一行、竖直拉平要求 2 点同一列。
/// </summary>
public enum MarkAlignmentMethod
{
    Horizontal,
    Vertical,
    ThreePoint,
    TwoPlusOne
}

public static class MarkAlignmentMethodText
{
    public static string Text(this MarkAlignmentMethod method) => method switch
    {
        MarkAlignmentMethod.Horizontal => "水平（2 Mark 同一行）",
        MarkAlignmentMethod.Vertical => "竖直（2 Mark 同一列）",
        MarkAlignmentMethod.ThreePoint => "三点对位（推荐忽略，级 C 保留）",
        MarkAlignmentMethod.TwoPlusOne => "2+1 对位（推荐忽略，级 C 保留）",
        _ => method.ToString()
    };
}

/// <summary>F-3.2.2-60：Map 图谱单元格分类颜色。</summary>
public enum DieCellClass
{
    /// <summary>黄色 = 待检测 Die。</summary>
    Yellow,
    /// <summary>蓝色 = 预聚焦点位。</summary>
    Blue,
    /// <summary>红色 = 参考 Die（可能没有）。</summary>
    Red,
    /// <summary>绿色 = 随机选中的参考 Die。</summary>
    Green
}

public static class DieCellClassText
{
    public static string Text(this DieCellClass cell) => cell switch
    {
        DieCellClass.Yellow => "待检测 Die（黄）",
        DieCellClass.Blue => "预聚焦点位（蓝）",
        DieCellClass.Red => "参考 Die（红，可能没有）",
        DieCellClass.Green => "随机选中参考 Die（绿）",
        _ => cell.ToString()
    };
}

public readonly record struct DieCell(int Row, int Col);

/// <summary>
/// B-09：Golden Die 生成结果。`H08` 只读展示，禁止复制生成逻辑；
/// 生成实现唯一归属 `H03`（<see cref="ReferenceImagePlanner"/>）。
/// </summary>
public sealed record GoldenDiePlan
{
    public int ReferenceDieCount { get; init; }
    public double FovZoom { get; init; }
    public IReadOnlyList<DieCell> ReferenceDies { get; init; } = [];

    /// <summary>F-3.2.2-60 分类计数。</summary>
    public int YellowCount { get; init; }
    public int BlueCount { get; init; }
    public int RedCount { get; init; }
    public int GreenCount { get; init; }

    /// <summary>BR-29：参考 Die 数量越大，生成 GoldenDie 耗时越长（确定性耗时指数 = 数量本身）。</summary>
    public int EstimatedCostIndex { get; init; }

    /// <summary>BR-30：视野缩放修改后需重新生成参考图像。</summary>
    public bool NeedsRegeneration { get; init; }

    /// <summary>F-3.2.2-64/66：FOV 图谱手动调整标记。</summary>
    public bool ManualAdjusted { get; init; }
}

/// <summary>缩略图比例与倍镜规则（BR-27/28，F-3.2.2-41/42）。</summary>
public static class ThumbnailRules
{
    /// <summary>BR-27：缩略图比例必须为 1/2ⁿ（0 &lt; ratio ≤ 1）。</summary>
    public static bool IsPowerOfTwoRatio(double ratio)
    {
        if (ratio is <= 0 or > 1) return false;
        double inv = 1.0 / ratio;                       // 2^n
        double n = Math.Log2(inv);
        return Math.Abs(n - Math.Round(n)) < 1e-9;
    }

    /// <summary>BR-28：缩略图倍镜必须与左侧倍镜一致（维持默认即一致）。</summary>
    public static bool IsMagnifierConsistent(string thumbnailMagnifier, string leftPanelMagnifier)
        => string.Equals(thumbnailMagnifier?.Trim(), leftPanelMagnifier?.Trim(), StringComparison.OrdinalIgnoreCase);
}

/// <summary>平台（Chuck）坐标。</summary>
public readonly record struct PlatformPoint(double Xmm, double Ymm);

/// <summary>
/// Map 定位网格状态（F-3.2.2-45/46/47）：
/// 「创建 Map 定位网格」后可手动拖动；「确定 Map 定位」后锁定不可再移动；「重置」删除 Map 图。
/// </summary>
public sealed class MapGridState
{
    public bool IsLoaded { get; private set; }
    public bool IsLocked { get; private set; }
    public double OffsetX { get; private set; }
    public double OffsetY { get; private set; }

    public int Columns { get; private set; }
    public int Rows { get; private set; }

    public void Load(int columns, int rows)
    {
        if (columns <= 0 || rows <= 0) throw new ArgumentOutOfRangeException(nameof(columns));
        Columns = columns;
        Rows = rows;
        IsLoaded = true;
        IsLocked = false;
        OffsetX = 0;
        OffsetY = 0;
    }

    /// <summary>F-3.2.2-45：手动拖动网格；确定定位（锁定）后不可再移动（F-3.2.2-46）。</summary>
    public void Move(double dx, double dy)
    {
        if (!IsLoaded) throw new InvalidOperationException("尚未创建 Map 定位网格，无法移动。");
        if (IsLocked) throw new InvalidOperationException("Map 定位已确定并锁定，不可再移动网格（F-3.2.2-46）。");
        OffsetX += dx;
        OffsetY += dy;
    }

    /// <summary>F-3.2.2-46：确定 Map 定位 → 锁定。</summary>
    public void ConfirmLocation()
    {
        if (!IsLoaded) throw new InvalidOperationException("尚未创建 Map 定位网格，无法确定定位。");
        IsLocked = true;
    }

    /// <summary>F-3.2.2-47：重置 Map 定位 → 删除 Map 图，回到未加载状态。</summary>
    public void Reset()
    {
        IsLoaded = false;
        IsLocked = false;
        OffsetX = 0;
        OffsetY = 0;
    }
}
