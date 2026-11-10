using System.Globalization;
using System.Linq;
using WaferInspect.Core.Alignment;
using WaferInspect.Core.Maps;
using WaferInspectHMI.Services;
using WaferInspectHMI.ViewModels.Modules;
using WaferInspectHMI.Views;
using Xunit;

namespace WaferInspect.App.Tests;

/// <summary>
/// P8 增量（1.10.0，H06 可插拔 IScanExecutor 骨架）：离线实现（OfflineScanExecutor）经 IScanExecutor 装配缝委托，
/// 真实设备实现（DeviceScanExecutor）为诚实预留位——T-029/T-035 未闭环时 Execute 抛 DeviceScanNotConfirmedException，
/// 绝不注册生产 IDeviceGateway、绝不伪造设备结果（E-08/E-10）。闭环后仅实现 DeviceScanExecutor 并切换
/// SelectedScanExecutorKind 即完成真实设备替换，调用方与 UI 不变。
/// </summary>
public sealed class ScanExecutorTests
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

    private static void PopulateMap(WaferScanViewModel vm, int rows = 6, int cols = 6)
    {
        var catalog = new EditableMapTypeCatalog();
        var matrix = Enumerable.Repeat(BinKind.BlankDie, rows * cols).ToList();
        Assert.True(vm.Map.LoadWafer("MAP-PL-300", catalog, matrix, rows, cols));
    }

    /* ---------------- 离线实现：IScanExecutor 契约 ---------------- */

    [Fact]
    public void OfflineExecutor_ImplementsInterface_AndOfflineContract()
    {
        IScanExecutor executor = new OfflineScanExecutor(new TemplateMatchingEngine(), new MarkAlignmentService());

        Assert.Equal(ScanExecutorKind.Offline, executor.Kind);
        Assert.True(executor.IsOffline);                     // 诚实标注非真实设备

        var report = executor.Execute(6, 6, matchThreshold: 50);
        Assert.IsType<ScanReport>(report);
        Assert.True(report.IsOffline);
        Assert.InRange(report.MatchScore, 0, 100);
        Assert.Equal(6, report.Columns);
        Assert.Equal(6, report.Rows);
    }

    [Fact]
    public void OfflineExecutor_HighThresholdFails_ThenLowerThresholdPasses()
    {
        IScanExecutor executor = new OfflineScanExecutor(new TemplateMatchingEngine(), new MarkAlignmentService());
        var high = executor.Execute(6, 6, matchThreshold: 100);
        Assert.False(high.MatchPassed);

        var low = executor.RetryWithLowerThreshold(high, 0);
        Assert.Equal(high.MatchScore, low.MatchScore);       // 分数不变（同一视窗）
        Assert.True(low.MatchPassed);                        // 阈值 0 即通过
        Assert.True(low.IsOffline);
    }

    /* ---------------- 设备预留位：诚实占位，绝不伪造 ---------------- */

    [Fact]
    public void DeviceExecutor_IsDevice_AndExecuteThrowsHonest()
    {
        IScanExecutor executor = new DeviceScanExecutor();

        Assert.Equal(ScanExecutorKind.Device, executor.Kind);
        Assert.False(executor.IsOffline);                    // 非离线（真实设备来源，待闭环）

        // T-029/T-035 未闭环：拒绝伪造真实设备扫描结果（E-08/E-10）
        Assert.Throws<DeviceScanNotConfirmedException>(() => executor.Execute(6, 6));
        Assert.Throws<DeviceScanNotConfirmedException>(() => executor.RetryWithLowerThreshold(
            new ScanReport { Columns = 6, Rows = 6, FovSeed = 1 }, 0));
    }

    /* ---------------- ViewModel 选择面：默认离线、选项含两者 ---------------- */

    [Fact]
    public void ViewModel_DefaultsToOffline_AndOptionsContainBoth()
    {
        var (vm, _) = NewVm();
        Assert.Equal(ScanExecutorKind.Offline, vm.SelectedScanExecutorKind);
        Assert.Equal(2, vm.ScanExecutorOptions.Count);
        Assert.Contains(ScanExecutorKind.Offline, vm.ScanExecutorOptions);
        Assert.Contains(ScanExecutorKind.Device, vm.ScanExecutorOptions);
    }

    /* ---------------- 转换器：真实设备项依 T 状态禁用 ---------------- */

    [Fact]
    public void Converter_DeviceDisabled_OfflineEnabled()
    {
        var conv = new ScanKindToEnabledConverter();
        Assert.Equal(false, conv.Convert(ScanExecutorKind.Device, typeof(bool), null, CultureInfo.InvariantCulture));
        Assert.Equal(true, conv.Convert(ScanExecutorKind.Offline, typeof(bool), null, CultureInfo.InvariantCulture));

        var text = new ScanKindToTextConverter();
        Assert.Contains("T-029", (string)text.Convert(ScanExecutorKind.Device, typeof(string), null, CultureInfo.InvariantCulture));
        Assert.Contains("离线", (string)text.Convert(ScanExecutorKind.Offline, typeof(string), null, CultureInfo.InvariantCulture));
    }

    /* ---------------- 诚实失败：选中设备且未闭环时，扫描不伪造、不崩 ---------------- */

    [Fact]
    public void ViewModel_DeviceSelected_ScanHonestlyRefuses()
    {
        var (vm, _) = NewVm();
        PopulateMap(vm, 6, 6);
        vm.SelectedScanExecutorKind = ScanExecutorKind.Device;   // 模拟 UI 选择（实际 UI 禁用，此处仅验证诚实失败路径）

        vm.RunOfflineScanCommand.Execute(null);

        // DeviceScanExecutor 抛诚实异常 → 命令捕获并设置 StatusText，不写出任何伪造结果
        Assert.False(vm.HasScanReport);
        Assert.Null(vm.LastScanReport);
        Assert.Contains("T-029", vm.StatusText);
        Assert.Contains("T-035", vm.StatusText);
    }
}
