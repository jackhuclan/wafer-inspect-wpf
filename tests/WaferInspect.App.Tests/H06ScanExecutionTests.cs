using System.Linq;
using WaferInspect.Core.Alignment;
using WaferInspect.Core.Maps;
using WaferInspectHMI.ViewModels.Modules;
using Xunit;

namespace WaferInspect.App.Tests;

/// <summary>
/// P6 增量（1.8.0，H06 扫描执行 / 对位，离线演算沙盒）：
/// <see cref="OfflineScanExecutor"/> 在合成 FOV 上真实跑 B-08 唯一匹配引擎与 H03 对位服务，
/// 但 <see cref="OfflineScanReport.IsOffline"/> 恒为 true，不注册生产 IDeviceGateway、不伪造设备结果（E-08/E-10）。
/// 真实相机采集 / AI 推理属 T-029/T-035 搁置。
/// </summary>
public sealed class H06ScanExecutionTests
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

    /* ---------------- Core：OfflineScanExecutor 离线演算（确定性、诚实标注） ---------------- */

    [Fact]
    public void Executor_ProducesOfflineReport_WithRealScoreInRange()
    {
        var executor = new OfflineScanExecutor(new TemplateMatchingEngine(), new MarkAlignmentService());

        var report = executor.Execute(6, 6, matchThreshold: 50);

        // 诚实边界：恒为离线、不伪造设备结果
        Assert.True(report.IsOffline);
        Assert.Contains("不", report.Note);     // 注明「不代表真实设备」
        // 真实计算：分数在合法区间，平台坐标由 H03 对位解出
        Assert.InRange(report.MatchScore, 0, 100);
        Assert.Equal(6, report.Columns);
        Assert.Equal(6, report.Rows);
        Assert.Equal(3, report.ReferenceRow);   // 中心 Die
        Assert.Equal(3, report.ReferenceColumn);
        Assert.NotEqual(0, report.FovSeed);
    }

    [Fact]
    public void Executor_HighThreshold_Fails_ThenLowerThreshold_Passes()
    {
        var executor = new OfflineScanExecutor(new TemplateMatchingEngine(), new MarkAlignmentService());
        var high = executor.Execute(6, 6, matchThreshold: 100);   // BR-24 严格 > 阈值：分数 ≤ 100 不会 > 100 → 不通过
        Assert.False(high.MatchPassed);
        Assert.InRange(high.MatchScore, 0, 100);

        // 同一合成 FOV（同种子）上用更低阈值重跑：分数为确定值，仅阈值改变 Pass
        var low = executor.RetryWithLowerThreshold(high, 0);
        Assert.Equal(high.MatchScore, low.MatchScore);           // 分数不变（同一视窗）
        Assert.True(low.MatchPassed);                            // 阈值 0：任意正分即通过
        Assert.True(low.IsOffline);
    }

    [Fact]
    public void Executor_NegativeColumns_Throws()
    {
        var executor = new OfflineScanExecutor(new TemplateMatchingEngine(), new MarkAlignmentService());
        Assert.Throws<System.ArgumentOutOfRangeException>(() => executor.Execute(-1, 6));
    }

    /* ---------------- App：VM 命令与流程诚实部分解锁 ---------------- */

    [Fact]
    public void RunOfflineScan_WithoutMap_RefusesGracefully()
    {
        var (vm, _) = NewVm();

        vm.RunOfflineScanCommand.Execute(null);

        Assert.False(vm.HasScanReport);
        Assert.Null(vm.LastScanReport);
        Assert.Contains("请先生成 Map 图", vm.StatusText);
    }

    [Fact]
    public void RunOfflineScan_WithMap_SetsOfflineReport()
    {
        var (vm, _) = NewVm();
        PopulateMap(vm, 6, 6);

        vm.RunOfflineScanCommand.Execute(null);

        Assert.True(vm.HasScanReport);
        Assert.NotNull(vm.LastScanReport);
        Assert.True(vm.LastScanReport.IsOffline);
        Assert.InRange(vm.LastScanReport.MatchScore, 0, 100);
        Assert.Contains("离线演算", vm.ScanReportText);
    }

    [Fact]
    public void RetryLowerScore_WithoutScan_Refuses()
    {
        var (vm, _) = NewVm();
        PopulateMap(vm, 6, 6);

        vm.RetryLowerScoreCommand.Execute(null);

        Assert.Contains("请先", vm.StatusText);
    }

    [Fact]
    public void RunStep_Step7_OfflineMatch_MarksPlanned()
    {
        var (vm, _) = NewVm();
        PopulateMap(vm, 6, 6);
        var step7 = vm.FlowSteps.First(s => s.StepNo == 7);

        vm.RunStepCommand.Execute(step7);

        Assert.Equal(FlowStepStatus.Planned, step7.Status);
        Assert.True(vm.HasScanReport);
        Assert.Contains("规划完成", vm.FlowStatusText);
    }

    [Fact]
    public void RunStep_Step8_RequiresStep7First_RemainsDeferred()
    {
        var (vm, _) = NewVm();
        PopulateMap(vm, 6, 6);
        var step8 = vm.FlowSteps.First(s => s.StepNo == 8);

        vm.RunStepCommand.Execute(step8);

        Assert.Equal(FlowStepStatus.Deferred, step8.Status);
        Assert.False(vm.HasScanReport);
    }

    [Fact]
    public void RunStep_Step7_Then_Step8_BothPlanned()
    {
        var (vm, _) = NewVm();
        PopulateMap(vm, 6, 6);
        var step7 = vm.FlowSteps.First(s => s.StepNo == 7);
        var step8 = vm.FlowSteps.First(s => s.StepNo == 8);

        vm.RunStepCommand.Execute(step7);
        vm.RunStepCommand.Execute(step8);

        Assert.Equal(FlowStepStatus.Planned, step7.Status);
        Assert.Equal(FlowStepStatus.Planned, step8.Status);
    }

    [Fact]
    public void Steps3_4_6_9_RemainDeferred_AfterOfflineScan()
    {
        var (vm, _) = NewVm();
        PopulateMap(vm, 6, 6);

        vm.RunOfflineScanCommand.Execute(null);   // 离线扫描完成
        foreach (var no in new[] { 3, 4, 6, 9 })
            vm.RunStepCommand.Execute(vm.FlowSteps.First(s => s.StepNo == no));

        // 这些步骤仍需真实设备采集 / ROI / 锁定 / 填充（T-029/T-035），维持诚实搁置
        Assert.All(new[] { 3, 4, 6, 9 },
            no => Assert.Equal(FlowStepStatus.Deferred, vm.FlowSteps.First(s => s.StepNo == no).Status));
    }
}
