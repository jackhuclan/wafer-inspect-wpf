using System.IO;
using System.Linq;
using WaferInspect.Core.Maps;
using WaferInspectHMI.Services;
using WaferInspectHMI.ViewModels.Modules;
using Xunit;

namespace WaferInspect.App.Tests;

/// <summary>
/// P7 增量（1.9.0，T-037 可插拔 Sinf 导出骨架）：ISinfExporter 抽象、默认内部 CSV 诚实导出、
/// 工业字节流预留位诚实占位（T-037 未确认不伪造）。与 H08 报表内部 CSV 先例一致。
/// </summary>
public sealed class SinfExporterTests
{
    private sealed class StubDialogs : IH05DialogService
    {
        public string ExportPath { get; set; }
        public string PickMapFile(string currentPath) => string.Empty;
        public string PickExportPath(string suggestedName) => ExportPath ?? string.Empty;
        public string PromptTypeName(string title, string initialName) => string.Empty;
        public bool Confirm(string title, string message) => true;
        public void ShowMessage(string title, string message) { }
    }

    private static (WaferScanViewModel vm, StubDialogs dialogs) NewVm()
    {
        var catalog = new EditableMapTypeCatalog();
        var dialogs = new StubDialogs();
        return (new WaferScanViewModel(catalog, dialogs), dialogs);
    }

    private static void PopulateMap(WaferScanViewModel vm, int rows = 4, int cols = 4)
    {
        var catalog = new EditableMapTypeCatalog();
        var matrix = Enumerable.Repeat(BinKind.BlankDie, rows * cols).ToList();
        Assert.True(vm.Map.LoadWafer("MAP-PL-300", catalog, matrix, rows, cols));
    }

    /* ---------------- 可插拔骨架：ViewModel 选择面 ---------------- */

    [Fact]
    public void ViewModel_DefaultsToInternalCsv_AndOptionsContainBoth()
    {
        var (vm, _) = NewVm();
        Assert.Equal(SinfExporterKind.InternalCsv, vm.SelectedSinfExporterKind);
        Assert.Equal(2, vm.SinfExporterOptions.Count);
        Assert.Contains(SinfExporterKind.InternalCsv, vm.SinfExporterOptions);
        Assert.Contains(SinfExporterKind.Industrial, vm.SinfExporterOptions);
    }

    [Fact]
    public void ViewModel_InternalCsvExport_Succeeds()
    {
        var (vm, dialogs) = NewVm();
        PopulateMap(vm, 4, 4);
        string tmp = Path.GetTempFileName();
        dialogs.ExportPath = tmp;

        vm.ExportSinfCommand.Execute(null);

        Assert.True(File.Exists(tmp));
        string content = File.ReadAllText(tmp);
        Assert.Contains("非工业 Sinf 字节流", content);
        Assert.Contains("T-037", content);
        Assert.Contains("列,行,坐标,BinKind,BinCode,等级,颜色,墨点,标签", content);
        // 4x4 = 16 颗 Die 数据行
        int dataRows = content.Split('\n').Count(l => l.StartsWith("0,") || l.StartsWith("1,") || l.StartsWith("2,") || l.StartsWith("3,"));
        Assert.Equal(16, dataRows);
        File.Delete(tmp);
    }

    [Fact]
    public void ViewModel_IndustrialSelected_ExportHonestlyRefuses()
    {
        var (vm, dialogs) = NewVm();
        PopulateMap(vm, 4, 4);
        // 用不存在的路径（避免 Path.GetTempFileName 预创建空文件干扰断言）
        string tmp = Path.Combine(Path.GetTempPath(), "sinf_test_" + Guid.NewGuid().ToString() + ".sinf");
        dialogs.ExportPath = tmp;
        vm.SelectedSinfExporterKind = SinfExporterKind.Industrial;

        vm.ExportSinfCommand.Execute(null);

        // 工业实现抛诚实异常 → TryExportSinf 返回 false，不写出任何伪造文件
        Assert.False(File.Exists(tmp));
        Assert.Contains("T-037", vm.StatusText);
    }

    /* ---------------- 导出器单元：默认内部 CSV ---------------- */

    [Fact]
    public void InternalCsvExporter_ProducesHonestInternalCsv()
    {
        var (vm, _) = NewVm();
        PopulateMap(vm, 4, 4);
        var ctx = new SinfExportContext(vm.Map, "MAP-PL-300", "PL 300um", "模板匹配（其他）", "12", false);
        var exporter = new InternalCsvSinfExporter();

        Assert.Equal(SinfExporterKind.InternalCsv, exporter.Kind);
        Assert.False(exporter.IsIndustrial);
        Assert.EndsWith(".sinf.internal.csv", exporter.SuggestedFileName(ctx));

        string tmp = Path.GetTempFileName();
        exporter.Export(ctx, tmp);

        string content = File.ReadAllText(tmp);
        Assert.Contains("# Wafer Map 内部导出（诚实表示，非工业 Sinf 字节流）", content);
        Assert.Contains("# 说明：T-037 Sinf 文件格式规范待确认", content);
        Assert.Contains("列,行,坐标,BinKind,BinCode,等级,颜色,墨点,标签", content);
        File.Delete(tmp);
    }

    /* ---------------- 导出器单元：工业预留位诚实占位 ---------------- */

    [Fact]
    public void IndustrialExporter_NotIndustrialFalse_AndExportThrowsHonest()
    {
        var (vm, _) = NewVm();
        PopulateMap(vm, 4, 4);
        var ctx = new SinfExportContext(vm.Map, "MAP-PL-300", "PL 300um", "模板匹配（其他）", "12", false);
        var exporter = new IndustrialSinfExporter();

        Assert.Equal(SinfExporterKind.Industrial, exporter.Kind);
        Assert.True(exporter.IsIndustrial);
        Assert.EndsWith(".sinf", exporter.SuggestedFileName(ctx));

        // T-037 未确认：拒绝伪造工业字节流（E-08 / E-10）
        Assert.Throws<SinfFormatNotConfirmedException>(() => exporter.Export(ctx, Path.GetTempFileName()));
    }
}
