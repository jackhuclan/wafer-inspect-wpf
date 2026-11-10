using System.Collections.Generic;
using System.IO;
using System.Linq;
using WaferInspect.Core.Maps;
using WaferInspectHMI.ViewModels.Modules;
using Xunit;

namespace WaferInspect.App.Tests;

/// <summary>
/// P4-B 增量（1.6.0，H05 Wafer 扫描，PRD 4.5.2 / UI-3.7.3）：
/// 12 步流程编排（B-24 / F-3.7.3-25）的诚实标记与 Sinf 诚实内部导出（BR-50，T-037 待确认）。
/// 依赖 H03/H06 的步骤（3~9）不得伪造设备 / 算法结果（E-08 / E-10）。
/// </summary>
public sealed class WaferScanFlowTests
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

    /* ---------------- 12 步流程定义与诚实边界（B-24） ---------------- */

    [Fact]
    public void FlowSteps_AreTwelve_WithCorrectDeliverableClassification()
    {
        var (vm, _) = NewVm();
        Assert.Equal(12, vm.FlowSteps.Count);

        var deliverable = vm.FlowSteps.Where(s => s.Deliverable).Select(s => s.StepNo).OrderBy(n => n).ToList();
        Assert.Equal(new[] { 1, 2, 10, 11, 12 }, deliverable);   // 纯本地步可交付

        var deferred = vm.FlowSteps.Where(s => !s.Deliverable).Select(s => s.StepNo).OrderBy(n => n).ToList();
        Assert.Equal(new[] { 3, 4, 5, 6, 7, 8, 9 }, deferred);  // 依赖 H03/H06，诚实搁置

        // 搁置步骤必须带依赖说明（T-029 / T-035），不得无声吞掉
        Assert.All(vm.FlowSteps.Where(s => !s.Deliverable), s => Assert.False(string.IsNullOrWhiteSpace(s.DependencyNote)));
    }

    [Fact]
    public void RunStep_DeferredStep_MarksDeferred_NotDone()   // 诚实：不伪造设备 / 算法结果（E-08/E-10）
    {
        var (vm, _) = NewVm();
        var step3 = vm.FlowSteps.First(s => s.StepNo == 3);   // 选模板，依赖 H03

        vm.RunStepCommand.Execute(step3);

        Assert.Equal(FlowStepStatus.Deferred, step3.Status);
        Assert.NotEqual(FlowStepStatus.Done, step3.Status);
        Assert.Contains("搁置", vm.StatusText);
    }

    [Fact]
    public void RunFlow_LocalStepsDone_DeferredStepsRemainDeferred()
    {
        var (vm, _) = NewVm();
        PopulateMap(vm);

        vm.RunFlowCommand.Execute(null);

        // 依赖 H03/H06 的步骤（3/4/6/7/8/9）仍诚实搁置；步骤 5（搜索圆规划）现可由 H03 参考图像
        // 在本地规划 → 标记为 Planned（P5，B-09），不再是 Deferred。
        Assert.All(vm.FlowSteps.Where(s => !s.Deliverable && s.StepNo != 5),
            s => Assert.Equal(FlowStepStatus.Deferred, s.Status));
        Assert.Equal(FlowStepStatus.Planned, vm.FlowSteps.First(s => s.StepNo == 5).Status);
        Assert.Contains("诚实搁置", vm.FlowStatusText);
    }

    [Fact]
    public void Navigation_GotoNextPrev_StaysWithinBounds()
    {
        var (vm, _) = NewVm();
        Assert.Equal(0, vm.CurrentStepIndex);

        vm.GotoNextCommand.Execute(null);
        Assert.Equal(1, vm.CurrentStepIndex);
        Assert.Equal(2, vm.SelectedStep.StepNo);

        vm.GotoPrevCommand.Execute(null);
        Assert.Equal(0, vm.CurrentStepIndex);

        vm.GotoPrevCommand.Execute(null);   // 已在首步，不应越界
        Assert.Equal(0, vm.CurrentStepIndex);
    }

    /* ---------------- Sinf 诚实内部导出（BR-50，T-037 待确认） ---------------- */

    [Fact]
    public void ExportSinf_ProducesHonestInternalExport_NotIndustrialBytes()
    {
        var (vm, dialogs) = NewVm();
        PopulateMap(vm, 4, 4);
        var tmp = Path.Combine(Path.GetTempPath(), $"p4b_sinf_{System.Guid.NewGuid():N}.sinf.internal.csv");
        dialogs.ExportPath = tmp;

        vm.ExportSinfCommand.Execute(null);

        Assert.True(File.Exists(tmp), "应写出自洽内部导出文件");
        var content = File.ReadAllText(tmp);
        // 诚实标注：明确非工业 Sinf 字节流
        Assert.Contains("非工业 Sinf 字节流", content);
        Assert.Contains("T-037", content);
        // 结构：表头 + 16 颗 Die 数据行（4x4），且含 Bin 字段
        var lines = content.Split('\n').Where(l => l.Length > 0 && !l.StartsWith('#')).ToList();
        Assert.Contains("列,行,坐标,BinKind,BinCode,等级,颜色,墨点,标签", lines[0]);
        Assert.Equal(16, lines.Count - 1);
        File.Delete(tmp);
    }

    [Fact]
    public void ExportSinf_WithoutMap_RefusesGracefully()
    {
        var (vm, dialogs) = NewVm();
        dialogs.ExportPath = Path.Combine(Path.GetTempPath(), "should_not_be_written.csv");

        vm.ExportSinfCommand.Execute(null);

        Assert.Contains("请先生成 Map 图", vm.StatusText);
        Assert.False(File.Exists(dialogs.ExportPath));
    }
}
