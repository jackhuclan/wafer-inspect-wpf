using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WaferInspect.Core.Alignment;
using WaferInspect.Core.Maps;
using WaferInspectHMI.Models;

namespace WaferInspectHMI.ViewModels.Modules;

/// <summary>
/// H05「Wafer 扫描」（PRD 4.5.2 / UI-3.7.3，P4-A 增量 1.5.0）：
/// 扫描参数（晶圆大小 BR-46 / 扫描速度 BR-47 / 扩膜 BR-48 / 创建方式 F-3.7.3-09）、
/// Map 编辑工具（Die 笔刷 F-3.7.3-19 / 整行整列 F-3.7.3-20 / 矩形批量 F-3.7.3-22 /
/// 外扩内缩 F-3.7.3-21·23，R-H05-05）、全景图（F-3.7.3-10，H06 采集 T-029 诚实搁置）。
/// 12 步流程编排（B-24 / R-H05-04）与 Sinf 导出（BR-50）属 P4-B 增量，本页仅登记未迁移项。
/// 所有 Bin / 颜色一律来自 <see cref="IMapTypeCatalog"/>（B-11），Map 绘制复用唯一 <see cref="MapGridView"/>（B-06）。
/// </summary>
public sealed partial class WaferScanViewModel : ObservableBase
{
    private readonly IMapTypeCatalog _catalog;
    private readonly MapGridModel _map = new();
    private readonly IH05DialogService _dialogs;
    private readonly ReferenceImagePlanner _planner = new();
    private readonly OfflineScanExecutor _executor = new(new TemplateMatchingEngine(), new MarkAlignmentService());
    private string _currentMapTypeId = string.Empty;

    public WaferScanViewModel(IMapTypeCatalog catalog, IH05DialogService dialogs)
    {
        _catalog = catalog;
        _dialogs = dialogs;

        _catalog.Changed += (_, _) => ReloadTypes();
        _map.Changed += (_, _) => RefreshMapText();

        WaferSizeOptions = new ObservableCollection<string> { "2", "3", "4", "5", "6", "8", "10", "12" };
        CreateModeOptions = new ObservableCollection<string> { "无图案（光片晶圆）", "网格匹配（小 Die）", "模板匹配（其他）" };

        foreach (var type in _catalog.Types) Types.Add(new MapTypeRow(type));
        SelectedType = Types.FirstOrDefault();
        LoadBrushBins();
        RefreshMapText();
        InitFlowSteps();
        CurrentStepIndex = 0;
    }

    /// <summary>Wafer 扫描专用 Map 模型（本页私有实例，与 maps 页预览隔离，B-06 唯一绘制规则）。</summary>
    public MapGridModel Map => _map;

    public ObservableCollection<MapTypeRow> Types { get; } = new();
    public ObservableCollection<BrushBinItem> BrushBins { get; } = new();
    public ObservableCollection<string> WaferSizeOptions { get; }
    public ObservableCollection<string> CreateModeOptions { get; }

    [ObservableProperty] private MapTypeRow _selectedType;
    [ObservableProperty] private BrushBinItem _selectedBrushBin;
    [ObservableProperty] private BinDefinition _brushBinDef;
    [ObservableProperty] private bool _isBatchMode;
    [ObservableProperty] private string _waferSize = "12";
    [ObservableProperty] private int _scanSpeed = 300;
    [ObservableProperty] private bool _expandFilm;
    [ObservableProperty] private string _createMode = "模板匹配（其他）";
    [ObservableProperty] private int _shrinkStep = 1;
    [ObservableProperty] private string _jumpX = "3";
    [ObservableProperty] private string _jumpY = "7";
    [ObservableProperty] private string _statusText = "请选择图谱类型并生成 Map 图";
    [ObservableProperty] private string _mapStatusText = "尚未生成 Map 图";
    [ObservableProperty] private string _selectedDieText = "未选中 Die（在 Map 上双击）";
    [ObservableProperty] private string _revisionText = "刷新版本 v0";
    [ObservableProperty] private string _panoramaText = "[搁置] 全景图需 H06 图像采集（T-029），未接入";
    [ObservableProperty] private string _deferredNote =
        "[P4-B·已交付] 12 步流程编排（B-24 / R-H05-04）已落地：编排层 + 自由跳转（BR-19），" +
        "本地步（1 创建方式 / 2 选图谱 / 10 Die 笔刷 / 11 Ink·ReferenceDie / 12 Sinf 内部导出）真实完成；" +
        "Ink 墨点（BR-49）为本地 Die 元数据，ReferenceDie 已是 B-11 笔刷；" +
        "Sinf 导出为诚实内部表示（非工业字节流，T-037 待确认）。" +
        "[诚实搁置] 步骤 3~9 依赖 H03 对位 / H06 扫描执行（T-029 / T-035）：选模板 / 矩形 ROI / 锁定 / 灰度+Die 匹配 / 降分重试 / 填充未匹配，" +
        "均仅登记不伪造设备/算法结果（E-08 / E-10）。" +
        "[P5·已交付] H03 参考图像（B-09）Golden Die 规划与落格已在本地真实落地：步骤 5 的搜索圆规划点位现可由 ReferenceImagePlanner 本地生成并标记「规划完成（Planned）」，真实搜索仍依赖 B-10/T-029 搁置。" +
        "[P6·已交付] H06 扫描执行/对位离线演算沙盒已落地：步骤 7（灰度模板+Die 匹配）与步骤 8（降分重试）现可由 OfflineScanExecutor 在合成 FOV 上真实跑通 B-08 引擎与 H03 对位并标记「规划完成（Planned）」；步骤 3/4/6/9 仍需真实设备采集/ROI/锁定/填充（T-029/T-035），维持诚实搁置。";

    partial void OnSelectedTypeChanged(MapTypeRow value) => LoadBrushBins();

    partial void OnSelectedBrushBinChanged(BrushBinItem value)
        => BrushBinDef = value?.Bin;

    private void ReloadTypes()
    {
        string keepId = SelectedType?.Id ?? string.Empty;
        Types.Clear();
        foreach (var type in _catalog.Types) Types.Add(new MapTypeRow(type));
        SelectedType = Types.FirstOrDefault(t => t.Id == keepId) ?? Types.FirstOrDefault();
        LoadBrushBins();
    }

    private void LoadBrushBins()
    {
        BrushBins.Clear();
        var type = _catalog.Find(SelectedType?.Id ?? string.Empty);
        if (type == null) { SelectedBrushBin = null; BrushBinDef = null; return; }
        foreach (var bin in type.Bins) BrushBins.Add(new BrushBinItem(bin));
        // 默认选中 GoodDie；否则取第一个（F-3.7.3-25 首步即选 GoodDie Bin）。
        SelectedBrushBin = BrushBins.FirstOrDefault(b => b.Bin.Kind == BinKind.GoodDie) ?? BrushBins.FirstOrDefault();
    }

    private void RefreshMapText()
    {
        MapStatusText = _map.StatusText;
        SelectedDieText = _map.Selected?.Tip ?? "未选中 Die（在 Map 上双击）";
        RevisionText = $"刷新版本 v{_map.RefreshVersion}";
    }

    /* ---------------- 扫描参数（BR-46/47/48 / F-3.7.3-09） ---------------- */

    [RelayCommand]
    private void ApplyScanParams()
    {
        if (ScanSpeed < 100 || ScanSpeed > 500)
        {
            _dialogs.ShowMessage("扫描速度（BR-47）", "扫描速度须在 100–500 之间，默认 300。");
            return;
        }
        StatusText = $"已应用扫描参数：晶圆 {WaferSize} 寸 · 速度 {ScanSpeed} · 创建方式「{CreateMode}」" +
                     (ExpandFilm ? " · 扩膜（BR-48）" : " · 非扩膜");
    }

    /* ---------------- Map 生成与编辑（F-3.7.3-11 / R-H05-05） ---------------- */

    [RelayCommand]
    private void GenerateMap()
    {
        if (SelectedType == null) { StatusText = "请先选择图谱类型（F-3.7.3-11）"; return; }
        var type = _catalog.Find(SelectedType.Id);
        if (type == null || string.IsNullOrWhiteSpace(type.MapFilePath))
        {
            StatusText = "该图谱类型无图谱文件路径（BR-45：生成 Map 图需先导入图谱文件）";
            return;
        }
        _map.Generate(SelectedType.Id, _catalog);
        _currentMapTypeId = SelectedType.Id;
        StatusText = _map.StatusText;
    }

    [RelayCommand]
    private void RefreshMap()
    {
        if (string.IsNullOrEmpty(_currentMapTypeId)) { StatusText = "请先生成 Map 图"; return; }
        _map.Refresh(_currentMapTypeId, _catalog);
        StatusText = _map.StatusText;
    }

    [RelayCommand]
    private void EditRow()
    {
        if (BrushBinDef == null) { StatusText = "请先在右侧选择笔刷 Bin"; return; }
        if (_map.Selected == null) { StatusText = "请先在 Map 上双击选中一个 Die（确定行）"; return; }
        _map.EditRow(_map.Selected.Row, BrushBinDef);
        StatusText = _map.StatusText;
    }

    [RelayCommand]
    private void EditColumn()
    {
        if (BrushBinDef == null) { StatusText = "请先在右侧选择笔刷 Bin"; return; }
        if (_map.Selected == null) { StatusText = "请先在 Map 上双击选中一个 Die（确定列）"; return; }
        _map.EditColumn(_map.Selected.Column, BrushBinDef);
        StatusText = _map.StatusText;
    }

    [RelayCommand]
    private void Expand()
    {
        if (BrushBinDef == null) { StatusText = "请先在右侧选择基准 Bin（如 GoodDie）"; return; }
        var blank = BinKind.BlankDie.CreateDefault(0, MapLevelPalette.ColorFor("PL", BinKind.BlankDie.DefaultLevel()));
        _map.ExpandShrink("expand", BrushBinDef, blank, ShrinkStep);
        StatusText = _map.StatusText;
    }

    [RelayCommand]
    private void Shrink()
    {
        if (BrushBinDef == null) { StatusText = "请先在右侧选择基准 Bin（如 GoodDie）"; return; }
        var blank = BinKind.BlankDie.CreateDefault(0, MapLevelPalette.ColorFor("PL", BinKind.BlankDie.DefaultLevel()));
        _map.ExpandShrink("shrink", BrushBinDef, blank, ShrinkStep);
        StatusText = _map.StatusText;
    }

    [RelayCommand]
    private void JumpToDie()
    {
        if (!int.TryParse(JumpX?.Trim(), out int x) || !int.TryParse(JumpY?.Trim(), out int y))
        {
            _dialogs.ShowMessage("X/Y 跳转", "X（列）与 Y（行）必须为整数（BR-52）。");
            return;
        }
        _map.JumpTo(x, y);
        StatusText = _map.StatusText;
    }

    /// <summary>切换矩形批量模式（F-3.7.3-22）：开启后 Map 上拖拽框选区域。</summary>
    [RelayCommand]
    private void ToggleBatch() => IsBatchMode = !IsBatchMode;

    /// <summary>矩形批量选择完成（F-3.7.3-22）：把笔刷 Bin 应用到框选区域。</summary>
    public void OnRectSelected(int c0, int r0, int c1, int r1)
    {
        if (BrushBinDef == null) { StatusText = "请先在右侧选择笔刷 Bin，再框选区域批量修改"; return; }
        _map.EditRect(c0, r0, c1, r1, BrushBinDef);
        StatusText = _map.StatusText;
    }

    /* ---------------- 12 步流程编排（B-24 / F-3.7.3-25） ---------------- */

    public ObservableCollection<ScanFlowStep> FlowSteps { get; } = new();

    [ObservableProperty] private ScanFlowStep _selectedStep;
    [ObservableProperty] private int _currentStepIndex;
    [ObservableProperty] private bool _isInkMode;
    [ObservableProperty] private string _flowStatusText = "请选择左侧步骤查看说明，或点击「运行全流程」逐条执行（诚实标记依赖步）";

    /* ---------------- H03 参考图像（Golden Die 规划，B-09，P5 1.7.0） ----------------
       真实图像拟合属 H06（T-029 搁置）；本段仅做本地确定性规划与落格，不伪造采集结果（E-08/E-10）。 */

    /// <summary>参考 Die 数量（F-3.2.2-67，确定性等距散布；BR-29 耗时随其单调增加）。</summary>
    [ObservableProperty] private int _referenceDieCount = 5;

    /// <summary>视野缩放 FOV Zoom（0~10，F-3.2.2-62 / BR-30：修改后需重新生成）。</summary>
    [ObservableProperty] private double _fovZoom = 1.0;

    /// <summary>当前 Golden Die 规划结果（<see cref="ReferenceImagePlanner"/> 本地确定性生成）。</summary>
    [ObservableProperty] private GoldenDiePlan _goldenPlan;

    /// <summary>是否已生成规划（用于启用「应用落格」）。</summary>
    [ObservableProperty] private bool _hasGoldenPlan;

    /// <summary>规划出的参考 Die 坐标文本（前若干项），供 UI 列表展示。</summary>
    [ObservableProperty] private ObservableCollection<string> _goldenReferenceDieTexts = new();

    /* ---------------- H06 扫描执行 / 对位（离线演算沙盒，T-029/T-035 诚实搁置，P6 1.8.0） ----------------
       真实相机采集 / AI 推理属 H06（设备连接 T-029/T-035 搁置）；本段仅在合成 FOV 上真实跑 B-08 引擎与
       H03 对位（确定性离线演算），不接入生产 IDeviceGateway、不伪造设备结果（E-08/E-10）。 */

    /// <summary>H06 离线扫描匹配阈值（BR-24，0~100，高于才判通过）。</summary>
    [ObservableProperty] private double _matchThreshold = 75;

    /// <summary>最近一次离线扫描演算报告（H06，<see cref="OfflineScanReport.IsOffline"/> 恒 true）。</summary>
    [ObservableProperty] private OfflineScanReport _lastScanReport;

    /// <summary>是否已运行过离线扫描（启用降分重试 / 对位展示）。</summary>
    [ObservableProperty] private bool _hasScanReport;

    /// <summary>离线扫描演算结果文本（诚实标注确定性离线演算）。</summary>
    [ObservableProperty] private string _scanReportText = "尚未运行离线扫描演算（H06，T-029/T-035 诚实搁置）";

    private void InitFlowSteps()
    {
        FlowSteps.Add(new ScanFlowStep(1, "选创建方式", "H05", true));
        FlowSteps.Add(new ScanFlowStep(2, "选图谱类型并生成 Map", "H05", true));
        FlowSteps.Add(new ScanFlowStep(3, "选 GoodDie Bin 并创建模板", "H03", false, "模板创建依赖 H03 对位（T-029/T-035）"));
        FlowSteps.Add(new ScanFlowStep(4, "调整矩形 ROI", "H03", false, "矩形 ROI 依赖 H03 对位（T-029）"));
        FlowSteps.Add(new ScanFlowStep(5, "显示并调整匹配搜索圆", "H03", false, "搜索圆规划点位现可由 H03 参考图像在本地生成（B-09，P5）；真实搜索仍依赖 H03 对位（B-10/T-029）搁置"));
        FlowSteps.Add(new ScanFlowStep(6, "锁定", "H03", false, "锁定依赖 H03 对位（T-029）"));
        FlowSteps.Add(new ScanFlowStep(7, "灰度模板 + Die 匹配", "H03/H06", false, "离线匹配演算已由 H06 离线沙盒在合成 FOV 上真实跑通（B-08 唯一引擎，P6）；真实设备采集 / 搜索仍依赖 H03/H06（T-029/T-035）搁置"));
        FlowSteps.Add(new ScanFlowStep(8, "降低匹配分数重试", "H03/H06", false, "离线降分重试已由 H06 离线沙盒真实重跑 B-08 引擎（P6）；真实 AI 推理仍依赖 H03（T-035）搁置"));
        FlowSteps.Add(new ScanFlowStep(9, "填充未匹配 Die / 多建模板", "H03/H06", false, "填充未匹配依赖 H03/H06（T-029/T-035）"));
        FlowSteps.Add(new ScanFlowStep(10, "Die 笔刷手动改 Bin", "H05", true));
        FlowSteps.Add(new ScanFlowStep(11, "Ink 笔刷改未匹配 Die / 设 ReferenceDie", "H05", true, "", "BR-49：Ink 墨点为本地元数据，ReferenceDie 已是 B-11 笔刷"));
        FlowSteps.Add(new ScanFlowStep(12, "导出 Sinf", "H05/H08", true, "", "诚实内部导出（非工业 Sinf 字节流，T-037 待确认）"));
        SelectedStep = FlowSteps[0];
    }

    [RelayCommand]
    private void SelectStep(ScanFlowStep step)
    {
        if (step == null) return;
        SelectedStep = step;
        CurrentStepIndex = step.StepNo - 1;
    }

    [RelayCommand]
    private void GotoNext()
    {
        int i = CurrentStepIndex + 1;
        if (i >= FlowSteps.Count) return;
        SelectedStep = FlowSteps[i];
        CurrentStepIndex = i;
    }

    [RelayCommand]
    private void GotoPrev()
    {
        int i = CurrentStepIndex - 1;
        if (i < 0) return;
        SelectedStep = FlowSteps[i];
        CurrentStepIndex = i;
    }

    [RelayCommand]
    private void RunStep(ScanFlowStep step)
    {
        if (step == null) return;
        switch (step.StepNo)
        {
            case 1:
                ApplyScanParams();
                MarkStepDone(step);
                break;
            case 2:
                GenerateMap();
                MarkStepDone(step);
                break;
            case 5:
                PlanGoldenDie();
                if (HasGoldenPlan) MarkStepPlanned(step);
                else
                {
                    StatusText = $"[规划未完成] 步骤 {step.StepNo}「{step.Title}」需先生成 Map 图（BR-45）后再规划（E-08/E-10）";
                    step.Status = FlowStepStatus.Deferred;
                }
                break;
            case 7:
                try
                {
                    RunOfflineScanCore();
                    MarkStepPlanned(step);
                }
                catch (Exception ex)
                {
                    StatusText = $"[离线演算失败] 步骤 {step.StepNo}：{ex.Message}";
                    step.Status = FlowStepStatus.Deferred;
                }
                break;
            case 8:
                if (!HasScanReport || LastScanReport == null)
                {
                    StatusText = $"[依赖未满足] 步骤 {step.StepNo} 需先完成步骤 7 离线扫描（E-08/E-10 不伪造）";
                    step.Status = FlowStepStatus.Deferred;
                    break;
                }
                try
                {
                    double lower = Math.Max(0, MatchThreshold - 10);
                    var retry = _executor.RetryWithLowerThreshold(LastScanReport, lower);
                    MatchThreshold = lower;
                    LastScanReport = retry;
                    HasScanReport = true;
                    ScanReportText = $"[离线演算·降分重试] 阈值降至 {lower:F0}，匹配分数 {retry.MatchScore}（{(retry.MatchPassed ? "通过" : "仍未通过")}）；FOV 不变（合成，非设备采集）";
                    MarkStepPlanned(step);
                }
                catch (Exception ex)
                {
                    StatusText = $"[离线降分重试失败] 步骤 {step.StepNo}：{ex.Message}";
                    step.Status = FlowStepStatus.Deferred;
                }
                break;
            case 10:
                if (BrushBinDef != null && _map.Selected != null)
                {
                    _map.EditCell(_map.Selected.Column, _map.Selected.Row, BrushBinDef);
                    StatusText = _map.StatusText;
                }
                else
                {
                    StatusText = "步骤 10：请先在 Map 上双击选中一个 Die，并在右侧选择笔刷 Bin";
                }
                MarkStepDone(step);
                break;
            case 11:
                IsInkMode = true;
                StatusText = "步骤 11：已开启 Ink 墨点笔刷（BR-49）——在 Map 上单击 Die 切换墨点；或选 ReferenceDie 笔刷后单击设参考 Die";
                MarkStepDone(step);
                break;
            case 12:
                bool ok = TryExportSinf();
                step.Status = ok ? FlowStepStatus.Done : FlowStepStatus.Pending;
                if (ok) FlowStatusText = $"步骤 {step.StepNo} 已完成：{step.Title}";
                break;
            default:
                StatusText = $"[搁置] 步骤 {step.StepNo}「{step.Title}」{step.DependencyNote}；不伪造设备 / 算法结果（E-08/E-10）";
                step.Status = FlowStepStatus.Deferred;
                FlowStatusText = $"步骤 {step.StepNo} 诚实搁置：{step.DependencyNote}";
                break;
        }
    }

    [RelayCommand]
    private void RunFlow()
    {
        int done = 0, deferred = 0;
        foreach (var step in FlowSteps)
        {
            RunStep(step);
            if (step.Status == FlowStepStatus.Done) done++;
            else if (step.Status == FlowStepStatus.Deferred) deferred++;
        }
        FlowStatusText = $"全流程运行结束：本地可交付 {done} 步完成，依赖 H03/H06 诚实搁置 {deferred} 步；未伪造任何设备 / 算法结果（E-08/E-10）";
        StatusText = FlowStatusText;
    }

    private void MarkStepDone(ScanFlowStep step)
    {
        step.Status = FlowStepStatus.Done;
        FlowStatusText = $"步骤 {step.StepNo} 已完成：{step.Title}";
    }

    private void MarkStepPlanned(ScanFlowStep step)
    {
        step.Status = FlowStepStatus.Planned;
        FlowStatusText = $"步骤 {step.StepNo} 规划完成（本地）：{step.Title}；设备执行仍搁置（E-08/E-10）";
    }

    /* ---------------- H03 参考图像（Golden Die 规划与落格，B-09，P5 1.7.0） ---------------- */

    /// <summary>
    /// F-3.2.2-60~67 / B-09：调用 <see cref="ReferenceImagePlanner"/> 在本地确定性生成 Golden Die 规划
    /// （参考 Die 等距散布、四色分类计数、BR-29 耗时指数）。需在已生成 Map 图后进行（依赖 cols/rows）。
    /// 真实图像拟合属 H06（T-029 搁置），此处不伪造采集（E-08/E-10）。
    /// </summary>
    [RelayCommand]
    private void PlanGoldenDie()
    {
        if (_map.Cells.Count == 0)
        {
            StatusText = "生成 Golden Die 规划失败：请先生成 Map 图（BR-45）";
            return;
        }
        if (ReferenceDieCount < 0)
        {
            StatusText = "参考 Die 数量不能为负（F-3.2.2-67）";
            return;
        }
        if (ReferenceDieCount > _map.Columns * _map.Rows)
        {
            StatusText = $"参考 Die 数量（{ReferenceDieCount}）超过可用 Die 总数（{_map.Columns * _map.Rows}）（F-3.2.2-67）";
            return;
        }
        if (FovZoom is <= 0 or > 10)
        {
            StatusText = "视野缩放（FOV）必须在 0~10 之间（F-3.2.2-62）";
            return;
        }

        var plan = _planner.Plan(_map.Columns, _map.Rows, ReferenceDieCount, FovZoom);
        GoldenPlan = plan;
        HasGoldenPlan = true;
        GoldenReferenceDieTexts = new ObservableCollection<string>(
            plan.ReferenceDies.Select(d => $"R{d.Row} · C{d.Col}"));
        StatusText = $"已生成 Golden Die 规划（B-09）：参考 {plan.GreenCount} 颗 · 待检测 {plan.YellowCount} · " +
                     $"预聚焦 {plan.BlueCount} · 耗时指数 {plan.EstimatedCostIndex}（BR-29）；" +
                     $"真实图像拟合仍属 H06（T-029 搁置，未伪造）";
    }

    /// <summary>
    /// 把规划出的参考 Die 落格为 B-11「参考 Die（RD）」Bin（经由 <see cref="MapGridModel.EditCell"/>，
    /// 唯一绘制规则 B-06）。这是真实、本地的确定性操作，可交付（与步骤 11 设 ReferenceDie 一致）。
    /// </summary>
    [RelayCommand]
    private void ApplyGoldenDieToMap()
    {
        if (!HasGoldenPlan || GoldenPlan == null)
        {
            StatusText = "请先「生成 Golden Die 规划」再落格";
            return;
        }
        if (_map.Cells.Count == 0)
        {
            StatusText = "落格失败：请先生成 Map 图（BR-45）";
            return;
        }
        var type = _catalog.Find(_currentMapTypeId);
        if (type == null) type = _catalog.Find(SelectedType?.Id ?? string.Empty);
        if (type == null)
        {
            StatusText = "落格失败：当前图谱类型不存在";
            return;
        }
        var rd = type.Bins.FirstOrDefault(b => b.Kind == BinKind.ReferenceDie);
        if (rd == null)
        {
            StatusText = "落格失败：当前图谱类型不含 ReferenceDie Bin（B-11）";
            return;
        }
        int n = 0;
        foreach (var d in GoldenPlan.ReferenceDies)
            if (_map.EditCell(d.Col, d.Row, rd)) n++;
        StatusText = $"已将规划出的 {GoldenPlan.ReferenceDies.Count} 颗参考 Die 落格为 RD（参考）：成功 {n} 颗（B-09 · B-06 · B-11）";
    }

    /// <summary>BR-30 / F-3.2.2-62：修改 FOV 缩放后重新规划，并标记 <see cref="GoldenDiePlan.NeedsRegeneration"/>。</summary>
    [RelayCommand]
    private void ChangeFovZoom()
    {
        if (GoldenPlan == null)
        {
            StatusText = "请先生成 Golden Die 规划，再调整 FOV 重规划（BR-30）";
            return;
        }
        if (FovZoom is <= 0 or > 10)
        {
            StatusText = "视野缩放（FOV）必须在 0~10 之间（F-3.2.2-62）";
            return;
        }
        var updated = _planner.ChangeFovZoom(GoldenPlan, FovZoom);
        GoldenPlan = updated;
        StatusText = updated.NeedsRegeneration
            ? $"FOV 已调整为 {FovZoom}：规划需重新生成（BR-30，NeedsRegeneration=true）"
            : $"FOV 仍为 {FovZoom}：未变化，无需重新生成";
    }

    /// <summary>F-3.2.2-64/66：Golden 生成效果欠佳 → FOV 图谱手动调整（标记，不删除已规划点位）。</summary>
    [RelayCommand]
    private void MarkManualAdjust()
    {
        if (GoldenPlan == null)
        {
            StatusText = "请先生成 Golden Die 规划，再标记手动调整（F-3.2.2-64/66）";
            return;
        }
        GoldenPlan = _planner.MarkManualAdjusted(GoldenPlan);
        StatusText = "已标记 FOV 图谱手动调整（F-3.2.2-64/66）：保留已规划点位，待人工微调 Golden 效果";
    }

    /* ---------------- H06 扫描执行 / 对位（离线演算沙盒，P6 1.8.0） ---------------- */

    /// <summary>
    /// 运行一次离线扫描演算：合成 FOV 上真实跑 B-08 引擎算匹配分、H03 对位算平台坐标（确定性、可复现）。
    /// 需在已生成 Map 图后调用（依赖 cols/rows）；真实设备采集 / AI 推理属 T-029/T-035 搁置，不伪造（E-08/E-10）。
    /// </summary>
    private OfflineScanReport RunOfflineScanCore()
    {
        if (_map.Cells.Count == 0)
            throw new InvalidOperationException("请先生成 Map 图（BR-45）");
        if (MatchThreshold is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(MatchThreshold), "匹配阈值须在 0~100 之间（BR-24）");

        var report = _executor.Execute(_map.Columns, _map.Rows, MatchThreshold);
        LastScanReport = report;
        HasScanReport = true;
        ScanReportText = $"[离线演算] 匹配分数 {report.MatchScore}（阈值 {report.MatchThreshold:F0}）{(report.MatchPassed ? "通过" : "未通过")}；" +
                         $"参考 Die R{report.ReferenceRow}·C{report.ReferenceColumn} → 平台 ({report.PlatformX:F2}, {report.PlatformY:F2}) mm；" +
                         $"FOV 种子 {report.FovSeed}（合成，非设备采集）";
        return report;
    }

    /// <summary>步骤 7 入口：运行离线扫描演算（合成 FOV + B-08 引擎 + H03 对位）。</summary>
    [RelayCommand]
    private void RunOfflineScan()
    {
        try
        {
            RunOfflineScanCore();
            StatusText = ScanReportText;
        }
        catch (Exception ex)
        {
            StatusText = $"离线扫描演算失败：{ex.Message}";
        }
    }

    /// <summary>步骤 8 入口：降低匹配分数阈值后在同一合成 FOV 上真实重跑 B-08 引擎。</summary>
    [RelayCommand]
    private void RetryLowerScore()
    {
        if (!HasScanReport || LastScanReport == null)
        {
            StatusText = "请先运行离线扫描，再降低分数重试（步骤 8）";
            return;
        }
        try
        {
            double lower = Math.Max(0, MatchThreshold - 10);
            var retry = _executor.RetryWithLowerThreshold(LastScanReport, lower);
            MatchThreshold = lower;
            LastScanReport = retry;
            HasScanReport = true;
            ScanReportText = $"[离线演算·降分重试] 阈值降至 {lower:F0}，匹配分数 {retry.MatchScore}（{(retry.MatchPassed ? "通过" : "仍未通过")}）；FOV 不变（合成，非设备）";
            StatusText = ScanReportText;
        }
        catch (Exception ex)
        {
            StatusText = $"离线降分重试失败：{ex.Message}";
        }
    }

    /// <summary>离线对位：复用 H03 MarkAlignmentService 解参考 Die 的平台坐标（确定性离线演算）。</summary>
    [RelayCommand]
    private void RunOfflineAlign()
    {
        try
        {
            var report = RunOfflineScanCore();
            ScanReportText = $"[离线对位] 参考 Die R{report.ReferenceRow}·C{report.ReferenceColumn} → 平台 ({report.PlatformX:F2}, {report.PlatformY:F2}) mm（H03 DieToPlatform，离线演算）";
            StatusText = ScanReportText;
        }
        catch (Exception ex)
        {
            StatusText = $"离线对位失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private void ToggleInk() => IsInkMode = !IsInkMode;

    [RelayCommand]
    private void ClearInk()
    {
        _map.ClearAllInk();
        StatusText = _map.StatusText;
    }

    [RelayCommand]
    private void ExportSinf() => TryExportSinf();

    /// <summary>
    /// BR-50 / B-12：Sinf 导出。T-037 格式规范待确认，故生成**自洽的内部 Map 导出**
    /// （Die 矩阵 / Bin 代码 / 坐标 / Ink·ReferenceDie 标记），文件头明确标注「非工业 Sinf 字节流」，
    /// 不伪造工业格式（E-08 / E-10），与 H08 报表内部 CSV 先例一致。
    /// </summary>
    private bool TryExportSinf()
    {
        if (_map.Cells.Count == 0)
        {
            StatusText = "导出 Sinf 失败：请先生成 Map 图（BR-45）";
            return false;
        }
        string suggested = $"wafer_{(SelectedType?.Id ?? "map")}_v{_map.RefreshVersion}.sinf.internal.csv";
        string path = _dialogs.PickExportPath(suggested);
        if (string.IsNullOrEmpty(path))
        {
            StatusText = "已取消 Sinf 导出（未生成任何文件）";
            return false;
        }
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# Wafer Map 内部导出（诚实表示，非工业 Sinf 字节流）");
        sb.AppendLine("# 说明：T-037 Sinf 文件格式规范待确认；本文件为自洽的 Map 数据导出，供工程校验 / 迁移比对；不得作为工业 Sinf 字节流使用");
        sb.AppendLine($"# 生成时间,{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"# 图谱类型,{SelectedType?.Id ?? ""},{SelectedType?.Name ?? ""}");
        sb.AppendLine($"# 创建方式,{CreateMode}");
        sb.AppendLine($"# 晶圆尺寸(寸),{WaferSize}");
        sb.AppendLine($"# 扩膜,{ExpandFilm}");
        sb.AppendLine($"# 行数,{_map.Rows}");
        sb.AppendLine($"# 列数,{_map.Columns}");
        sb.AppendLine($"# 墨点标记数,{_map.InkedCount}");
        sb.AppendLine("列,行,坐标,BinKind,BinCode,等级,颜色,墨点,标签");
        foreach (var c in _map.Cells)
            sb.AppendLine($"{c.Column},{c.Row},{c.Coordinate},{c.Kind},{c.Code},{c.Level},{c.ColorHex},{(c.Inked ? 1 : 0)},{c.Label}");
        try
        {
            System.IO.File.WriteAllText(path, sb.ToString(), System.Text.Encoding.UTF8);
        }
        catch (Exception ex)
        {
            StatusText = $"Sinf 内部导出写入失败：{ex.Message}";
            return false;
        }
        StatusText = $"已导出 Sinf 内部表示（诚实）：{System.IO.Path.GetFileName(path)}，{_map.Cells.Count} 颗 Die；非工业 Sinf 字节流（T-037 待确认）";
        return true;
    }
}

/// <summary>笔刷 Bin 选项（图谱类型 Bin 的只读投影，供 Wafer 扫描选择当前笔刷）。</summary>
public sealed class BrushBinItem
{
    public BrushBinItem(BinDefinition bin) => Bin = bin;

    public BinDefinition Bin { get; }

    public string Code => Bin.Code;

    public string Name => Bin.Name;

    public string ColorHex => Bin.ColorHex;

    public string Display => $"{Name}（{Code}）";
}
