namespace WaferInspect.Core.Alignment;

/// <summary>
/// BR-21 光照校准：光照过暗导致细节丢失、过亮导致饱和发白时给出建议，不静默通过。
/// 判定基于图像统计（平均亮度 / 饱和像素占比），与图像来源无关（模拟与真实采集同一口径）。
/// </summary>
public sealed class LightCalibrationAnalyzer
{
    /// <summary>平均亮度低于此值判定过暗（细节丢失风险）。</summary>
    public const double DarkMeanThreshold = 40;

    /// <summary>平均亮度高于此值判定过亮（饱和发白风险）。</summary>
    public const double BrightMeanThreshold = 215;

    /// <summary>饱和像素（≥250）占比超过此值判定饱和。</summary>
    public const double SaturationFractionThreshold = 0.25;

    public LightCalibrationReport Analyze(GrayImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        long sum = 0;
        int saturated = 0;
        foreach (byte p in image.Pixels)
        {
            sum += p;
            if (p >= 250) saturated++;
        }

        double mean = sum / (double)image.Pixels.Length;
        double fraction = saturated / (double)image.Pixels.Length;

        var advisories = new List<string>();
        if (mean < DarkMeanThreshold)
            advisories.Add($"光照过暗（均值 {mean:F1} < {DarkMeanThreshold}）：细节可能丢失，请提高亮度（BR-21）");
        if (mean > BrightMeanThreshold)
            advisories.Add($"光照过亮（均值 {mean:F1} > {BrightMeanThreshold}）：可能饱和发白，请降低亮度（BR-21）");
        if (fraction > SaturationFractionThreshold)
            advisories.Add($"饱和像素占比 {fraction:P1} 超过 {SaturationFractionThreshold:P0}：请降低亮度（BR-21）");

        return new LightCalibrationReport
        {
            MeanBrightness = Math.Round(mean, 1),
            SaturatedFraction = Math.Round(fraction, 4),
            Advisories = advisories
        };
    }
}
