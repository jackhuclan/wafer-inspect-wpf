using WaferInspect.Core.Recipes;

namespace WaferInspect.Core.Alignment;

/// <summary>
/// B-08 唯一模板匹配实现：拉平（`F-3.2.2-35~39`）与 Wafer 扫描（`F-3.7.3-02~05/12~13`）共用同一引擎与取值口径
/// （AC-P2-04）。本类是全系统唯一的匹配分数计算来源，`H05` Wafer 扫描接入时必须复用本实例，禁止另写匹配逻辑。
/// 匹配口径：
/// <list type="bullet">
/// <item>灰度匹配（BR-23）：零均值归一化互相关（ZNCC）在整幅视窗内搜索最佳位置；模板无灰度对比（如切割道/空白）→ 0 分；</item>
/// <item>形状匹配（BR-23）：边缘密度评分；形状模板区域不宜过大，超过视窗面积 25% 给出建议（不判失败）；</item>
/// <item>BR-24：分数严格大于阈值才判定成功；</item>
/// <item>引擎不区分图像来源：模拟图像（<see cref="SyntheticFovImage"/>）与真实采集图像（`H06`）走同一计算路径。</item>
/// </list>
/// </summary>
public sealed class TemplateMatchingEngine
{
    /// <summary>形状模板区域面积超过视窗面积此比例时给出「不宜过大」建议（BR-23）。</summary>
    public const double ShapeOversizeFraction = 0.25;

    /// <summary>灰度模板方差低于此值视为「无对比」（高于模拟噪声方差 ≈12，低于任何真实结构对比）。</summary>
    private const double NoContrastVarianceThreshold = 30;

    public TemplateMatchOutcome Match(GrayImage image, TemplateRegion region, LevelingConfig config)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(config);
        return Match(image, region, config.TemplateType, config.MatchScore);
    }

    public TemplateMatchOutcome Match(GrayImage image, TemplateRegion region, TemplateType templateType, double scoreThreshold)
    {
        ArgumentNullException.ThrowIfNull(image);

        if (!region.IsValid)
            return new TemplateMatchOutcome
            {
                Score = 0,
                Passed = false,
                Advisories = ["框选区域无效：宽高必须大于 0（F-3.2.2-38）"]
            };

        if (region.X < 0 || region.Y < 0
            || region.X + region.Width > image.Width || region.Y + region.Height > image.Height)
            return new TemplateMatchOutcome
            {
                Score = 0,
                Passed = false,
                Advisories = ["框选区域超出视窗图像边界（F-3.2.2-38）"]
            };

        if (scoreThreshold is < 0 or > 100)
            return new TemplateMatchOutcome
            {
                Score = 0,
                Passed = false,
                Advisories = ["匹配分数阈值必须在 0~100 之间（BR-24）"]
            };

        return templateType == TemplateType.Shape
            ? MatchShape(image, region, scoreThreshold)
            : MatchGray(image, region, scoreThreshold);
    }

    /* ---------------- 灰度匹配（BR-23：区域大小无限制、创建更快） ---------------- */

    private static TemplateMatchOutcome MatchGray(GrayImage image, TemplateRegion region, double scoreThreshold)
    {
        // 模板统计量：零均值归一化互相关需要模板方差 > 0（切割道/空白区方差为 0 → 无特征）。
        double templateMean = RegionMean(image, region);
        double templateVar = RegionVariance(image, region, templateMean);

        if (templateVar < NoContrastVarianceThreshold)
        {
            return new TemplateMatchOutcome
            {
                Score = 0,
                Passed = false,
                Advisories = ["灰度模板无对比（框选到切割道/空白区域）：请框选含特征的 Die 区域（BR-23/BR-24）"]
            };
        }

        double best = 0;
        const int coarseStride = 4;   // 粗搜步长 4px；特征块邻域内偏移数像素仍保持高相关，满足阈值判定
        for (int oy = 0; oy + region.Height <= image.Height; oy += coarseStride)
        {
            for (int ox = 0; ox + region.Width <= image.Width; ox += coarseStride)
            {
                double score = ZnccAt(image, region, ox, oy, templateMean, templateVar);
                if (score > best) best = score;
            }
        }
        double score100 = Math.Clamp(best * 100, 0, 100);
        return new TemplateMatchOutcome
        {
            Score = Math.Round(score100, 1),
            Passed = score100 > scoreThreshold,
            Advisories = []
        };
    }

    private static double ZnccAt(GrayImage image, TemplateRegion region, int ox, int oy, double templateMean, double templateVar)
    {
        // 零均值归一化互相关：sum((T[i]-meanT)(I[i]-meanI)) / sqrt(varT*varI*n)
        double sum = 0, sumSq = 0, cross = 0;
        int n = region.Width * region.Height;
        for (int y = 0; y < region.Height; y++)
        {
            int iy = oy + y;
            for (int x = 0; x < region.Width; x++)
            {
                double t = image.At(region.X + x, region.Y + y) - templateMean;
                double i = image.At(ox + x, iy);
                sum += i;
                sumSq += i * i;
                cross += t * i;
            }
        }

        double meanI = sum / n;
        double varI = sumSq / n - meanI * meanI;
        if (varI <= 0) return 0;

        // Zncc = Σ((T-T̄)(I-Ī)) / (n·√(varT·varI))；由 Σ(T-T̄)=0 得 Σ((T-T̄)(I-Ī)) = cross。
        return cross / (n * Math.Sqrt(templateVar * varI));
    }

    private static double RegionMean(GrayImage image, TemplateRegion region)
    {
        long sum = 0;
        for (int y = 0; y < region.Height; y++)
            for (int x = 0; x < region.Width; x++)
                sum += image.At(region.X + x, region.Y + y);
        return sum / (double)(region.Width * region.Height);
    }

    private static double RegionVariance(GrayImage image, TemplateRegion region, double mean)
    {
        double sumSq = 0;
        for (int y = 0; y < region.Height; y++)
            for (int x = 0; x < region.Width; x++)
            {
                double d = image.At(region.X + x, region.Y + y) - mean;
                sumSq += d * d;
            }
        return sumSq / (region.Width * region.Height);
    }

    /* ---------------- 形状匹配（BR-23：模板区域不宜过大） ---------------- */

    private static TemplateMatchOutcome MatchShape(GrayImage image, TemplateRegion region, double scoreThreshold)
    {
        var advisories = new List<string>();

        long imageArea = (long)image.Width * image.Height;
        if (region.Area > (long)(imageArea * ShapeOversizeFraction))
            advisories.Add($"形状模板区域不宜过大（BR-23）：当前占视窗 {region.Area * 100.0 / imageArea:F0}%，建议缩小框选区域");

        // 形状匹配以边缘密度评分：|dx|+|dy| 梯度超过阈值计为边缘像素。
        int edgeCount = 0;
        int edgeThreshold = 40;
        for (int y = region.Y; y < region.Y + region.Height; y++)
        {
            for (int x = region.X; x < region.X + region.Width; x++)
            {
                int right = image.At(Math.Min(x + 1, image.Width - 1), y);
                int down = image.At(x, Math.Min(y + 1, image.Height - 1));
                if (Math.Abs(image.At(x, y) - right) + Math.Abs(image.At(x, y) - down) >= edgeThreshold)
                    edgeCount++;
            }
        }

        double density = edgeCount / (double)(region.Width * region.Height);
        double score100 = Math.Clamp(density * 900, 0, 100);   // 标定：特征块边框密度 ≈ 0.11 → ≈99 分

        return new TemplateMatchOutcome
        {
            Score = Math.Round(score100, 1),
            Passed = score100 > scoreThreshold,
            Advisories = advisories
        };
    }
}
