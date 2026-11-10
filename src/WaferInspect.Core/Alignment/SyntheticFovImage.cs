namespace WaferInspect.Core.Alignment;

/// <summary>
/// 确定性模拟视窗图像生成器：die 阵列 + 切割道 + 每 die 中央特征块 + 种子噪声（LCG）。
/// 用途：`H03` 拉平 / 模板匹配在**模拟图像**上真实执行匹配计算（引擎不区分图像来源）。
/// 真实全景图 / 视窗图像由 `H06` 采集，设备连接搁置（`T-029`），本生成器显式标注模拟来源，不伪造采集。
/// </summary>
public static class SyntheticFovImage
{
    /// <summary>每个 die 的像素间距（含切割道）。</summary>
    public const int DiePitch = 40;

    /// <summary>切割道宽度（像素）。</summary>
    public const int Street = 4;

    /// <summary>die 中央特征块边长（像素，含亮块与边框）。</summary>
    public const int FeatureSize = 8;

    private const int NoiseAmplitude = 6;

    public static GrayImage Generate(int width = 480, int height = 360, int seed = 20261110)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

        var pixels = new byte[width * height];
        uint state = LcgSeed(seed);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int dx = x % DiePitch;
                int dy = y % DiePitch;

                bool inStreet = dx < Street || dy < Street;

                // die 基底 150 / 切割道 70；die 中央 8×8 特征块 220。
                int value = inStreet ? 70 : 150;
                int featureStart = (DiePitch - FeatureSize) / 2;   // = 16
                if (!inStreet && dx >= featureStart && dx < featureStart + FeatureSize
                                && dy >= featureStart && dy < featureStart + FeatureSize)
                    value = 220;

                int noise = NextNoise(ref state, NoiseAmplitude);
                int final = Math.Clamp(value + noise, 0, 255);
                pixels[y * width + x] = (byte)final;
            }
        }

        return new GrayImage(width, height, pixels);
    }

    private static uint LcgSeed(int seed) => (uint)(seed * 2654435761) | 1u;

    private static int NextNoise(ref uint state, int amplitude)
    {
        state = state * 1664525u + 1013904223u;
        int raw = (int)(state >> 24) & 0xFF;                 // 0~255
        return raw % (amplitude * 2 + 1) - amplitude;        // -amplitude ~ +amplitude
    }
}
