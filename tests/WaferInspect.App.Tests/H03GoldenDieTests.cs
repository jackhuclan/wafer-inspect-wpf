using System.Linq;
using WaferInspect.Core.Alignment;
using WaferInspect.Core.Maps;
using WaferInspectHMI.ViewModels.Modules;
using Xunit;

namespace WaferInspect.App.Tests;

/// <summary>
/// P5 增量（1.7.0，H03 参考图像 / Golden Die 规划与落格，B-09）：
/// <see cref="ReferenceImagePlanner"/> 本地确定性规划接入 WaferScanViewModel，
/// 并将规划出的参考 Die 落格为 B-11 参考 Die（RD）。真实图像拟合属 H06（T-029 搁置），不伪造（E-08/E-10）。
/// </summary>
public sealed class H03GoldenDieTests
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

    /* ---------------- 规划（B-09，本地确定性） ---------------- */

    [Fact]
    public void PlanGoldenDie_WithoutMap_RefusesGracefully()
    {
        var (vm, _) = NewVm();
        vm.ReferenceDieCount = 5;

        vm.PlanGoldenDieCommand.Execute(null);

        Assert.False(vm.HasGoldenPlan);
        Assert.Null(vm.GoldenPlan);
        Assert.Contains("请先生成 Map 图", vm.StatusText);
    }

    [Fact]
    public void PlanGoldenDie_WithMap_ProducesPlan_WithCorrectClassification()
    {
        var (vm, _) = NewVm();
        PopulateMap(vm, 6, 6);
        vm.ReferenceDieCount = 5;
        vm.FovZoom = 1.0;

        vm.PlanGoldenDieCommand.Execute(null);

        Assert.True(vm.HasGoldenPlan);
        Assert.NotNull(vm.GoldenPlan);
        // F-3.2.2-67：确定性等距散布选中 5 颗参考 Die（绿 = 选中参考 Die）
        Assert.Equal(5, vm.GoldenPlan.GreenCount);
        Assert.Equal(5, vm.GoldenPlan.ReferenceDies.Count);
        // 其余待检测（黄）= 总数 - 绿
        Assert.Equal(36 - 5, vm.GoldenPlan.YellowCount);
        // 预聚焦（蓝）与参考 Die 同点位
        Assert.Equal(5, vm.GoldenPlan.BlueCount);
        // BR-29：耗时指数 = 参考 Die 数量（单调）
        Assert.Equal(5, vm.GoldenPlan.EstimatedCostIndex);
        // 初始规划无需重生成
        Assert.False(vm.GoldenPlan.NeedsRegeneration);
        // 坐标文本列表已填充
        Assert.Equal(5, vm.GoldenReferenceDieTexts.Count);
    }

    [Fact]
    public void PlanGoldenDie_ExceedingDieCount_Refuses()
    {
        var (vm, _) = NewVm();
        PopulateMap(vm, 4, 4);   // 16 颗
        vm.ReferenceDieCount = 20;

        vm.PlanGoldenDieCommand.Execute(null);

        Assert.False(vm.HasGoldenPlan);
        Assert.Contains("超过可用 Die 总数", vm.StatusText);
    }

    /* ---------------- 落格（B-06 / B-11） ---------------- */

    [Fact]
    public void ApplyGoldenDieToMap_MarksReferenceDies_OnGrid()
    {
        var (vm, _) = NewVm();
        PopulateMap(vm, 6, 6);
        vm.ReferenceDieCount = 5;
        vm.PlanGoldenDieCommand.Execute(null);
        Assert.True(vm.HasGoldenPlan);

        vm.ApplyGoldenDieToMapCommand.Execute(null);

        int rd = vm.Map.Cells.Count(c => c.Kind == BinKind.ReferenceDie);
        Assert.Equal(5, rd);
        Assert.Contains("落格", vm.StatusText);
        Assert.Contains("RD", vm.StatusText);
    }

    [Fact]
    public void ApplyGoldenDie_WithoutPlan_Refuses()
    {
        var (vm, _) = NewVm();
        PopulateMap(vm, 6, 6);

        vm.ApplyGoldenDieToMapCommand.Execute(null);

        Assert.Contains("请先", vm.StatusText);
        Assert.Equal(0, vm.Map.Cells.Count(c => c.Kind == BinKind.ReferenceDie));
    }

    /* ---------------- BR-30：FOV 变更标记 NeedsRegeneration ---------------- */

    [Fact]
    public void ChangeFovZoom_AfterPlan_MarksNeedsRegeneration()
    {
        var (vm, _) = NewVm();
        PopulateMap(vm, 6, 6);
        vm.ReferenceDieCount = 3;
        vm.FovZoom = 1.0;
        vm.PlanGoldenDieCommand.Execute(null);
        Assert.False(vm.GoldenPlan.NeedsRegeneration);

        vm.FovZoom = 2.5;
        vm.ChangeFovZoomCommand.Execute(null);

        Assert.True(vm.GoldenPlan.NeedsRegeneration);
        Assert.Equal(2.5, vm.GoldenPlan.FovZoom);
    }

    [Fact]
    public void ChangeFovZoom_SameValue_DoesNotFlagRegen()
    {
        var (vm, _) = NewVm();
        PopulateMap(vm, 6, 6);
        vm.ReferenceDieCount = 3;
        vm.FovZoom = 1.0;
        vm.PlanGoldenDieCommand.Execute(null);

        vm.ChangeFovZoomCommand.Execute(null);   // FOV 未变

        Assert.False(vm.GoldenPlan.NeedsRegeneration);
    }

    /* ---------------- 流程诚实部分解锁：步骤 5 → Planned ---------------- */

    [Fact]
    public void RunStep_Step5_PlansAndMarksPlanned_NotDoneNorDeferred()
    {
        var (vm, _) = NewVm();
        PopulateMap(vm, 6, 6);
        vm.ReferenceDieCount = 4;
        var step5 = vm.FlowSteps.First(s => s.StepNo == 5);

        vm.RunStepCommand.Execute(step5);

        Assert.Equal(FlowStepStatus.Planned, step5.Status);
        Assert.NotEqual(FlowStepStatus.Done, step5.Status);
        Assert.NotEqual(FlowStepStatus.Deferred, step5.Status);
        Assert.True(vm.HasGoldenPlan);
        Assert.Contains("规划完成", vm.FlowStatusText);
    }

    [Fact]
    public void Step5_RemainsNonDeliverable_ButPlannable()
    {
        var (vm, _) = NewVm();
        // 设备执行（真实搜索）仍依赖 B-10/T-029，故本增量不可整体交付
        Assert.False(vm.FlowSteps.First(s => s.StepNo == 5).Deliverable);
    }
}
