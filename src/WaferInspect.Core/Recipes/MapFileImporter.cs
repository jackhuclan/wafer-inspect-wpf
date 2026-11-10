namespace WaferInspect.Core.Recipes;

/// <summary>Map 文件导入结果（F-3.2.1-17 / BR-78）；失败必须带原因，不得静默通过。</summary>
public sealed record MapImportResult(
    bool Success,
    double DieXmm,
    double DieYmm,
    int Columns,
    int Rows,
    IReadOnlyList<string> BarcodeCodes,
    string Error)
{
    public static MapImportResult Fail(string error)
        => new(false, 0, 0, 0, 0, Array.Empty<string>(), error);
}

/// <summary>
/// Map 文件导入器（`F-3.2.1-11/17/18`）：解析 Die 尺寸、行列数与条码分类代码。
/// 契约：
/// <list type="bullet">
/// <item>声明类型与扩展名不匹配 → 直接失败（BR-78），不做「猜格式」的兜底；</item>
/// <item>解析失败（缺表头、非数字、Die 尺寸 ≤ 0）→ 失败并给出原因，界面保留手动输入路径；</item>
/// <item>当前原型实现 TXT / CSV 两种解析器；XML / SINF 保留枚举位，导入时明确报「解析器未接入」。</item>
/// </list>
/// 文件格式（原型约定，见 docs/P2-Migration-Status.md）：
/// CSV：首行 `DieX,DieY,Cols,Rows`，其后每行 `col,row,code`；
/// TXT：`DieX=..`/`DieY=..`/`Cols=..`/`Rows=..` 逐行，其后同 CSV 的 `col,row,code`。
/// </summary>
public sealed class MapFileImporter
{
    public MapImportResult Import(string path, MapFileType declaredType)
    {
        if (string.IsNullOrWhiteSpace(path))
            return MapImportResult.Fail("Map 文件路径为空（F-3.2.1-17 必填）。");

        if (!File.Exists(path))
            return MapImportResult.Fail($"Map 文件不存在：{path}（F-3.2.1-17）。");

        string ext = Path.GetExtension(path);
        string expected = declaredType.Extension();
        if (!string.Equals(ext, expected, StringComparison.OrdinalIgnoreCase))
            return MapImportResult.Fail($"Map 文件类型与实际格式不匹配：声明 {declaredType.Text()}，文件扩展名为 {ext}（BR-78）。");

        return declaredType switch
        {
            MapFileType.Csv => ParseCsv(path),
            MapFileType.Txt => ParseKeyValueText(path),
            _ => MapImportResult.Fail(
                $"{declaredType.Text()} 解析器未接入：原型阶段仅实现 TXT / CSV（F-3.2.1-17 扩展点，登记于 P2 状态文档）。")
        };
    }

    private static MapImportResult ParseCsv(string path)
    {
        string[] lines = ReadLines(path);
        if (lines.Length < 2)
            return MapImportResult.Fail("CSV Map 文件为空或缺少表头（期望首行 DieX,DieY,Cols,Rows）。");

        // 首行允许是文字表头（DieX,DieY,Cols,Rows），此时第二行才是数值；也接受直接给数值的首行。
        int valueLine = 0;
        string[] header = SplitCsv(lines[0]);
        if (!double.TryParse(header[0].Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out _))
        {
            if (lines.Length < 3)
                return MapImportResult.Fail("CSV Map 文件缺少数值行（表头之后需给出 DieX,DieY,Cols,Rows 的取值）。");
            valueLine = 1;
            header = SplitCsv(lines[1]);
        }

        if (header.Length < 4)
            return MapImportResult.Fail("CSV 表头不完整，期望 DieX,DieY,Cols,Rows。");

        if (!TryParsePositive(header[0], out double dieX) || !TryParsePositive(header[1], out double dieY))
            return MapImportResult.Fail("CSV 表头中的 DieX / DieY 非法（必须为正数）。");

        if (!int.TryParse(header[2], out int cols) || !int.TryParse(header[3], out int rows) || cols <= 0 || rows <= 0)
            return MapImportResult.Fail("CSV 表头中的 Cols / Rows 非法（必须为正整数）。");

        return CollectDieCodes(lines.Skip(valueLine + 1), dieX, dieY, cols, rows);
    }

    private static MapImportResult ParseKeyValueText(string path)
    {
        string[] lines = ReadLines(path);
        double dieX = 0, dieY = 0;
        int cols = 0, rows = 0;
        var dieLines = new List<string>();

        foreach (string raw in lines)
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            int eq = line.IndexOf('=');
            if (eq > 0)
            {
                string key = line[..eq].Trim();
                string value = line[(eq + 1)..].Trim();
                switch (key.ToLowerInvariant())
                {
                    case "diex": if (!TryParsePositive(value, out dieX)) return MapImportResult.Fail("TXT 中 DieX 非法（必须为正数）。"); break;
                    case "diey": if (!TryParsePositive(value, out dieY)) return MapImportResult.Fail("TXT 中 DieY 非法（必须为正数）。"); break;
                    case "cols": if (!int.TryParse(value, out cols) || cols <= 0) return MapImportResult.Fail("TXT 中 Cols 非法（必须为正整数）。"); break;
                    case "rows": if (!int.TryParse(value, out rows) || rows <= 0) return MapImportResult.Fail("TXT 中 Rows 非法（必须为正整数）。"); break;
                    default: break;   // 其它键为扩展位，忽略但不算失败
                }
                continue;
            }
            dieLines.Add(line);
        }

        if (dieX <= 0 || dieY <= 0 || cols <= 0 || rows <= 0)
            return MapImportResult.Fail("TXT 缺少 DieX / DieY / Cols / Rows 定义。");

        return CollectDieCodes(dieLines, dieX, dieY, cols, rows);
    }

    private static MapImportResult CollectDieCodes(
        IEnumerable<string> dieLines, double dieX, double dieY, int cols, int rows)
    {
        var codes = new List<string>();
        foreach (string raw in dieLines)
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            string[] parts = SplitCsv(line);
            if (parts.Length < 3)
                return MapImportResult.Fail($"Die 行格式非法（期望 col,row,code）：{line}");

            if (!int.TryParse(parts[0].Trim(), out int col) || !int.TryParse(parts[1].Trim(), out int row))
                return MapImportResult.Fail($"Die 行列号非法：{line}");

            if (col < 0 || col >= cols || row < 0 || row >= rows)
                return MapImportResult.Fail($"Die 坐标越界：{line}（Cols={cols}, Rows={rows}）");

            string code = parts[2].Trim();
            if (code.Length == 0)
                return MapImportResult.Fail($"Die 分类代码为空：{line}");

            if (!codes.Contains(code, StringComparer.Ordinal)) codes.Add(code);
        }

        if (codes.Count == 0)
            return MapImportResult.Fail("Map 文件中没有任何 Die 记录，无法确定条码分类代码（F-3.2.1-18）。");

        return new MapImportResult(true, dieX, dieY, cols, rows, codes, string.Empty);
    }

    private static string[] ReadLines(string path)
        => File.ReadAllLines(path)
            .Select(l => l.TrimStart('\uFEFF'))
            .ToArray();

    private static string[] SplitCsv(string line) => line.Split(',');

    private static bool TryParsePositive(string text, out double value)
        => double.TryParse(text.Trim(), System.Globalization.NumberStyles.Float,
               System.Globalization.CultureInfo.InvariantCulture, out value) && value > 0;
}
