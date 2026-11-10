using System.IO;
using System.Text;
using WaferInspect.Core.Maps;

namespace WaferInspectHMI.Services;

/// <summary>
/// 默认 Sinf 导出器：自洽内部 Map 表示（CSV）。文件头明确标注「非工业 Sinf 字节流，T-037 待确认」，
/// 与 H08 报表内部 CSV 先例一致。不伪造工业格式（E-08 / E-10）。
/// Die 矩阵 / Bin 代码 / 坐标 / Ink·ReferenceDie 标记全部来自 <see cref="MapGridModel"/>（B-06 唯一数据来源）。
/// </summary>
public sealed class InternalCsvSinfExporter : ISinfExporter
{
    public SinfExporterKind Kind => SinfExporterKind.InternalCsv;

    public bool IsIndustrial => false;

    public string SuggestedFileName(SinfExportContext ctx)
        => $"wafer_{(ctx.TypeId ?? "map")}_v{ctx.Map.RefreshVersion}.sinf.internal.csv";

    public void Export(SinfExportContext ctx, string filePath)
    {
        var map = ctx.Map;
        var sb = new StringBuilder();
        sb.AppendLine("# Wafer Map 内部导出（诚实表示，非工业 Sinf 字节流）");
        sb.AppendLine("# 说明：T-037 Sinf 文件格式规范待确认；本文件为自洽的 Map 数据导出，供工程校验 / 迁移比对；不得作为工业 Sinf 字节流使用");
        sb.AppendLine($"# 生成时间,{System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"# 图谱类型,{ctx.TypeId ?? ""},{ctx.TypeName ?? ""}");
        sb.AppendLine($"# 创建方式,{ctx.CreateMode}");
        sb.AppendLine($"# 晶圆尺寸(寸),{ctx.WaferSize}");
        sb.AppendLine($"# 扩膜,{ctx.ExpandFilm}");
        sb.AppendLine($"# 行数,{map.Rows}");
        sb.AppendLine($"# 列数,{map.Columns}");
        sb.AppendLine($"# 墨点标记数,{map.InkedCount}");
        sb.AppendLine("列,行,坐标,BinKind,BinCode,等级,颜色,墨点,标签");
        foreach (var c in map.Cells)
            sb.AppendLine($"{c.Column},{c.Row},{c.Coordinate},{c.Kind},{c.Code},{c.Level},{c.ColorHex},{(c.Inked ? 1 : 0)},{c.Label}");
        File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
    }
}
