using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WaferInspect.Core.Alignment;
using WaferInspect.Core.Inspection;
using WaferInspect.Core.Maps;
using WaferInspect.Core.Recipes;

namespace WaferInspectHMI.ViewModels.Modules;

/// <summary>
/// 子配方分步向导 ViewModel（R-H02-05/06，B-25 由 H02 统一编排）：
/// · 步骤顺序固定为 T-042 确认的 7 步，禁止跳步（第一步未成功不得进入下一步）；
/// · BR-19：已完成步骤随时可点击重新进入执行；
/// · 步骤 1 基础信息为 T-005 确认的全字段表单（5 分区 33 字段）；
/// · 步骤 2~4 的图形化交互由 H03 提供（0.9.0）；步骤 5/6 由 H04 提供（1.0.0），
///   与「缺陷分析 · 参数验证」共用 <see cref="IRoiScanAreaStore"/> 同一单例（B-01/B-02、AC-P2-03）；
///   图像采集与设备动作仍按用户裁决搁置（T-029），界面显式标注不伪造执行成功；
/// · 步骤 7 复判设置（R-H02-07，BR-20）写入子配方，运行时供 H08 读取。
/// </summary>
public sealed partial class SubRecipeWizardViewModel : ObservableObject, IMapRegionCanvasHost
{
    private readonly ISubRecipeRepository _subs;
    private readonly IJobRepository _jobs;
    private readonly IMapTypeCatalog _mapTypes;
    private readonly IH02DialogService _dialogs;
    private readonly IRoiScanAreaStore _store;
    private readonly string _parentJobId;

    // H03 对位能力（B-08 唯一实现；AC-P2-04：H05 Wafer 扫描后续复用同一引擎单例）。
    private readonly TemplateMatchingEngine _matcher;
    private readonly LightCalibrationAnalyzer _lightAnalyzer;
    private readonly MarkAlignmentService _markService;
    private readonly ReferenceImagePlanner _planner;
    private readonly MapFileImporter _mapImporter;

    /// <summary>模拟视窗图像（H06 采集随 T-029 搁置；引擎不区分图像来源，真实采集落地后走同一计算路径）。</summary>
    private GrayImage _fovImage = new(4, 4, new byte[16]);

    /// <summary>Mark 步骤的 Map 定位网格状态（F-3.2.2-45/46/47）。</summary>
    private readonly MapGridState _grid = new();

    /// <summary>向导正在编辑的子配方工作副本；完成一步即经仓库持久化。</summary>
    private SubRecipe _recipe = new();
    private SubRecipeWizard _wizard;

    /// <summary>表单回填期间抑制「选中项变化 → 重填表单」的递归。</summary>
    private bool _loading;

    /// <summary>H04 数据源回填期间抑制属性 setter 反向写回 store。</summary>
    private bool _syncingRoi;

    /// <summary>批量写 store 期间抑制逐条 Changed 触发的重载（避免覆盖正在编辑的表单）。</summary>
    private bool _suppressStoreSync;

    public SubRecipeWizardViewModel(
        string parentJobId,
        IJobRepository jobs,
        ISubRecipeRepository subs,
        IMapTypeCatalog mapTypes,
        IH02DialogService dialogs,
        TemplateMatchingEngine matcher,
        LightCalibrationAnalyzer lightAnalyzer,
        MarkAlignmentService markService,
        ReferenceImagePlanner planner,
        MapFileImporter mapImporter,
        IRoiScanAreaStore store)
    {
        _parentJobId = parentJobId;
        _jobs = jobs;
        _subs = subs;
        _mapTypes = mapTypes;
        _dialogs = dialogs;
        _store = store;
        _matcher = matcher;
        _lightAnalyzer = lightAnalyzer;
        _markService = markService;
        _planner = planner;
        _mapImporter = mapImporter;

        var parent = _jobs.Find(parentJobId);
        HeaderText = $"子配方向导 · Job：{parent?.Name ?? parentJobId}（7 步，T-042 顺序）";

        // AC-H05-01：子配方步骤 1「图谱选择」与 H02 表单同样消费 H05 唯一目录。
        ReloadMapTypeNames();
        _mapTypes.Changed += (_, _) => ReloadMapTypeNames();

        _subs.Changed += (_, _) => ReloadSubs();
        // H04 唯一数据源：参数验证页 / 缺陷分析页改动后，向导步骤 5/6 立即看到同一份数据。
        _store.Changed += (_, recipeId) =>
        {
            if (!_suppressStoreSync && HasRecipe && string.Equals(recipeId, _recipe.Id, StringComparison.Ordinal))
                SyncRoiFromStore();
        };
        ReloadSubs();
    }

    /* ---------------- 页头：子配方 CRUD（F-3.2.2-01~05） ---------------- */

    public ObservableCollection<SubRecipeRow> SubRecipes { get; } = new();

    public ObservableCollection<string> MapTypeNames { get; } = new();

    public IReadOnlyList<string> PatternTypeOptions { get; } = new[]
    {
        WaferPatternType.NoPattern.Text(),
        WaferPatternType.PatternWithoutMark.Text(),
        WaferPatternType.PatternWithMark.Text()
    };

    public IReadOnlyList<string> SizeOptions { get; }
        = new[] { WaferSize.Inch6.Text(), WaferSize.Inch8.Text(), WaferSize.Inch12.Text() };

    public IReadOnlyList<string> MapFileTypeOptions { get; }
        = new[] { MapFileType.Txt.Text(), MapFileType.Csv.Text(), MapFileType.Xml.Text(), MapFileType.Sinf.Text() };

    public IReadOnlyList<string> DirectionOptions { get; }
        = new[] { AxisDirection.Positive.Text(), AxisDirection.Negative.Text() };

    public IReadOnlyList<string> OcrPositionOptions { get; }
        = new[] { OcrPosition.Front.Text(), OcrPosition.Back.Text() };

    public IReadOnlyList<string> LevelingModeOptions { get; }
        = new[] { LevelingMode.Die.Text(), LevelingMode.FieldOfView.Text() };

    public IReadOnlyList<string> TemplateTypeOptions { get; }
        = new[] { TemplateType.Shape.Text(), TemplateType.Gray.Text() };

    public IReadOnlyList<string> RecheckModeOptions { get; }
        = new[] { RecheckMode.OfflineReview.Text(), RecheckMode.OnlineVerify.Text() };

    /// <summary>BR-15：首项表示「不覆盖」，其余为子配方级覆盖值。</summary>
    public IReadOnlyList<string> TraceImageOptions { get; } = new[]
    {
        TraceImageInherit,
        TraceImagePolicy.Upload.Text(),
        TraceImagePolicy.DoNotUpload.Text(),
        TraceImagePolicy.UploadDefectOnly.Text()
    };

    private const string TraceImageInherit = "继承系统设置（不覆盖）";

    [ObservableProperty] private string _headerText = string.Empty;

    [ObservableProperty] private SubRecipeRow _selectedSubRecipe;

    [ObservableProperty] private string _statusText = "请先新增或选择一个子配方";

    [ObservableProperty] private bool _hasValidationError;

    [ObservableProperty] private string _validationSummary = string.Empty;

    [ObservableProperty] private bool _hasRecipe;

    /* ---------------- 步骤列表（T-042 顺序） ---------------- */

    public ObservableCollection<WizardStepRow> Steps { get; } = new();

    [ObservableProperty] private bool _isBasicInfo;
    [ObservableProperty] private bool _isLeveling;
    [ObservableProperty] private bool _isMark;
    [ObservableProperty] private bool _isReferenceImage;
    [ObservableProperty] private bool _isRoi;
    [ObservableProperty] private bool _isScanArea;
    [ObservableProperty] private bool _isRecheck;

    /// <summary>当前步骤说明（含能力接入标注），显示在步骤内容区顶部。</summary>
    [ObservableProperty] private string _stepNote = string.Empty;

    /* ---------------- 步骤 1：基础信息表单（T-005 全字段） ---------------- */

    [ObservableProperty] private string _biName = string.Empty;
    [ObservableProperty] private string _biPatternType = WaferPatternType.PatternWithMark.Text();
    [ObservableProperty] private string _biSize = WaferSize.Inch12.Text();
    [ObservableProperty] private string _biMaterial = "Si";
    [ObservableProperty] private string _biThickness = "0";
    [ObservableProperty] private string _biDieX = string.Empty;
    [ObservableProperty] private string _biDieY = string.Empty;
    [ObservableProperty] private string _biNotch = "270";
    [ObservableProperty] private string _biMapType = string.Empty;
    [ObservableProperty] private string _biMapFileType = MapFileType.Csv.Text();
    [ObservableProperty] private string _biBinGrade = "0";
    [ObservableProperty] private string _biColumnDirection = AxisDirection.Positive.Text();
    [ObservableProperty] private string _biRowDirection = AxisDirection.Positive.Text();
    [ObservableProperty] private string _biMapPath = string.Empty;
    [ObservableProperty] private string _biMapNotch = "270";
    [ObservableProperty] private string _biParseScript = string.Empty;
    [ObservableProperty] private string _biBarcode = string.Empty;
    [ObservableProperty] private string _biOcrJob = string.Empty;
    [ObservableProperty] private string _biOcrAngle = "0";
    [ObservableProperty] private string _biOcrPosition = OcrPosition.Front.Text();
    [ObservableProperty] private string _biLocateDieX = string.Empty;
    [ObservableProperty] private string _biLocateDieY = string.Empty;
    [ObservableProperty] private string _biOrientation = string.Empty;
    [ObservableProperty] private string _biColumnOffset = "0";
    [ObservableProperty] private string _biRowOffset = "0";
    [ObservableProperty] private string _biColRow = string.Empty;
    [ObservableProperty] private string _biXy = string.Empty;
    [ObservableProperty] private string _biIncludeExclude = string.Empty;
    [ObservableProperty] private bool _biUseKlarCenterTool;
    [ObservableProperty] private string _biTraceImage = TraceImageInherit;
    [ObservableProperty] private string _biMappingThickness = "0";
    [ObservableProperty] private string _biMappingSlotNum = "0";
    [ObservableProperty] private bool _biSamplingZoneEnabled;

    /* ---------------- 步骤 2：拉平（BR-22/23/24，H03 引擎执行） ---------------- */

    [ObservableProperty] private string _lvMode = LevelingMode.Die.Text();
    [ObservableProperty] private string _lvTemplateType = TemplateType.Gray.Text();
    [ObservableProperty] private string _lvScore = "70";

    public const int FovWidth = 480;
    public const int FovHeight = 360;

    /// <summary>视窗图像预览（模拟来源显式标注，F-3.2.2-38 图像采集属 H06，T-029 搁置）。</summary>
    [ObservableProperty] private ImageSource _fovPreview;

    /// <summary>图形框选结果（像素坐标，F-3.2.2-38 自动执行）。</summary>
    [ObservableProperty] private double _fovSelX;
    [ObservableProperty] private double _fovSelY;
    [ObservableProperty] private double _fovSelW;
    [ObservableProperty] private double _fovSelH;
    [ObservableProperty] private bool _hasFovSelection;

    /// <summary>键盘 / UIA 输入路径的框选区域（与图形框选等价，F-3.2.2-38）。</summary>
    [ObservableProperty] private string _lvRegionX = "56";
    [ObservableProperty] private string _lvRegionY = "56";
    [ObservableProperty] private string _lvRegionW = "24";
    [ObservableProperty] private string _lvRegionH = "24";

    [ObservableProperty] private bool _lvPassed;
    [ObservableProperty] private bool _lvExecuted;
    [ObservableProperty] private string _lvResultText = "尚未执行拉平：在视窗图像上拖拽框选特征区域（自动执行），或填写区域坐标后点击「执行拉平」（F-3.2.2-38/39）";
    [ObservableProperty] private string _lvScoreText = string.Empty;
    [ObservableProperty] private string _lvAdvisories = string.Empty;
    [ObservableProperty] private string _lvLightText = string.Empty;

    /* ---------------- 步骤 3：Mark（BR-25/26，H03 图形化交互） ---------------- */

    public ObservableCollection<MarkRow> MarkRows { get; } = new();

    public IReadOnlyList<string> MarkMethodOptions { get; } = new[]
    {
        MarkAlignmentMethod.Horizontal.Text(),
        MarkAlignmentMethod.Vertical.Text(),
        MarkAlignmentMethod.ThreePoint.Text(),
        MarkAlignmentMethod.TwoPlusOne.Text()
    };

    public IReadOnlyList<string> MarkIndexOptions { get; } = new[] { "Mark 1", "Mark 2", "Mark 3" };

    [ObservableProperty] private string _mkMethod = MarkAlignmentMethod.Horizontal.Text();
    [ObservableProperty] private int _selectedMarkIndex;
    [ObservableProperty] private string _mkThumbRatio = "0.25";
    [ObservableProperty] private string _mkMagnifier = "默认";
    [ObservableProperty] private string _mkSpeed = "100";
    [ObservableProperty] private string _mkGridStatusText = "尚未创建 Map 定位网格";

    /// <summary>Map 定位网格 cells（F-3.2.2-45：导入 Map 图自动生成；未导入时按 Die 尺寸生成默认网格）。</summary>
    public ObservableCollection<MarkCellRow> MarkGridCells { get; } = new();

    /* ---------------- 步骤 4：参考图像（F-3.2.2-60~67，H03 规划器） ---------------- */

    [ObservableProperty] private string _riImagePath = string.Empty;
    [ObservableProperty] private string _riCount = "5";
    [ObservableProperty] private string _riFovZoom = "1";
    [ObservableProperty] private string _riBlock = string.Empty;
    [ObservableProperty] private string _riGoldenParam = string.Empty;
    [ObservableProperty] private bool _riGenerated;
    [ObservableProperty] private string _riSummaryText = "尚未生成参考图像（F-3.2.2-67：生成成功后才允许下一步）";

    /* ---------------- 步骤 5：ROI（AC-P2-03，H04 唯一数据源） ---------------- */

    public ObservableCollection<RoiZoneRow> RoiRows { get; } = new();

    /// <summary>检测设置列表（F-3.2.2-69：每项为独立检测单元，保存后才算完成）。</summary>
    public ObservableCollection<RoiItemRow> RoiItemRows { get; } = new();

    /// <summary>共享 Map 圈选画布状态（与缺陷分析 / 参数验证页同一构建规则）。</summary>
    public MapRegionCanvasState Map { get; } = new();

    public IReadOnlyList<string> TargetKindOptions { get; } = new[] { RoiTargetKind.Zone.Text(), RoiTargetKind.Pad.Text() };

    public IReadOnlyList<string> ExtractionOptions { get; } = new[]
    {
        RegionExtraction.TemplateMatching.Text(), RegionExtraction.Binarization.Text()
    };

    public IReadOnlyList<string> GoldenToneOptions { get; } = new[]
    {
        GoldenImageTone.Bright.Text(), GoldenImageTone.Dark.Text()
    };

    /// <summary>F-3.2.2-75：7 步 ROI 操作流程（顺序固定，界面显式呈现，不静默省略）。</summary>
    public const string RoiWorkflowText =
        "① 选 Zone/Pad → ② 选检测项 → ③ 命名 → ④ 绘制/提取区域 → " +
        "⑤ 参数类型选 Surface（常用，BR-35）或其他 → ⑥ 右下角保存 → ⑦ 下一步";

    [ObservableProperty] private string _roiSourceText = "H04 唯一数据源尚未载入";

    [ObservableProperty] private string _roiItemStatusText = "尚未添加检测设置项";

    /* ---------------- 步骤 6：扫描区域（AC-P2-03） ---------------- */

    [ObservableProperty] private bool _scanFullWafer = true;

    [ObservableProperty] private bool _samplingEnabled;

    partial void OnScanFullWaferChanged(bool value)
    {
        if (_loading || _syncingRoi || !HasRecipe) return;
        _store.SetFullWafer(_recipe.Id, value);
    }

    partial void OnSamplingEnabledChanged(bool value)
    {
        if (_loading || _syncingRoi || !HasRecipe) return;
        _store.SetSamplingEnabled(_recipe.Id, value);
    }

    /* ---------------- 步骤 7：复判设置（R-H02-07，BR-20） ---------------- */

    [ObservableProperty] private string _rcMode = RecheckMode.OfflineReview.Text();

    [ObservableProperty] private bool _rcUseReviewCamera = true;

    /* ---------------- 子配方 CRUD ---------------- */

    private void ReloadSubs()
    {
        string keepId = SelectedSubRecipe?.Id;
        _loading = true;
        SubRecipes.Clear();
        foreach (var recipe in _subs.ListOf(_parentJobId))
            SubRecipes.Add(new SubRecipeRow(recipe));

        var reselect = SubRecipes.FirstOrDefault(r => r.Id == keepId) ?? SubRecipes.FirstOrDefault();
        SelectedSubRecipe = reselect;
        _loading = false;

        if (reselect != null) LoadRecipe(reselect.Source);
        else
        {
            HasRecipe = false;
            _recipe = new SubRecipe { ParentJobId = _parentJobId };
            _wizard = new SubRecipeWizard(_recipe);
            StatusText = SubRecipes.Count == 0
                ? "该 Job 尚无子配方：点击「新增」开始七步向导（F-3.2.2-01）"
                : "请选择左侧子配方";
            RefreshSteps();   // 空态也展示 7 步框架（禁用态），避免左侧列表空白
        }
    }

    partial void OnSelectedSubRecipeChanged(SubRecipeRow value)
    {
        if (_loading || value == null) return;
        LoadRecipe(value.Source);
    }

    private void LoadRecipe(SubRecipe source)
    {
        _recipe = source.Clone();
        _wizard = new SubRecipeWizard(_recipe);
        _wizard.Focus(_wizard.FirstIncomplete ?? WizardStepKey.BasicInfo);   // 重开向导恢复到未完成步骤
        HasRecipe = true;

        // 步骤 1 表单回填（T-005 全字段）。
        var info = _recipe.BasicInfo;
        BiName = _recipe.Name;
        BiPatternType = info.PatternType.Text();
        BiSize = info.Size.Text();
        BiMaterial = info.Material;
        BiThickness = info.ThicknessMm.ToString("0.###");
        BiDieX = info.DieXmm.ToString("0.###");
        BiDieY = info.DieYmm.ToString("0.###");
        BiNotch = info.NotchAngle.ToString("0.###");
        BiMapType = MapTypeNameOf(info.MapTypeId);
        BiMapFileType = info.MapFileType.Text();
        BiBinGrade = info.BinGradeLevel.ToString();
        BiColumnDirection = info.ColumnDirection.Text();
        BiRowDirection = info.RowDirection.Text();
        BiMapPath = info.MapFilePath;
        BiMapNotch = info.MapNotchAngle.ToString("0.###");
        BiParseScript = info.ParseScriptPath;
        BiBarcode = info.BarcodeCode;
        BiOcrJob = info.OcrJob;
        BiOcrAngle = info.OcrAngle.ToString("0.###");
        BiOcrPosition = info.OcrPosition.Text();
        BiLocateDieX = info.LocateDieX?.ToString() ?? string.Empty;
        BiLocateDieY = info.LocateDieY?.ToString() ?? string.Empty;
        BiOrientation = info.Orientation;
        BiColumnOffset = info.ColumnOffset.ToString();
        BiRowOffset = info.RowOffset.ToString();
        BiColRow = info.ColRow;
        BiXy = info.Xy;
        BiIncludeExclude = info.IncludeExclude;
        BiUseKlarCenterTool = info.UseKlarCenterTool;
        BiTraceImage = info.TraceImage is TraceImagePolicy policy ? policy.Text() : TraceImageInherit;
        BiMappingThickness = info.MappingThicknessMm.ToString("0.###");
        BiMappingSlotNum = info.MappingSlotNum.ToString();
        BiSamplingZoneEnabled = info.SamplingZoneEnabled;

        // 步骤 2 拉平（H03：含最近一次执行结果回填，F-3.2.2-38/39）。
        LvMode = _recipe.Leveling.Mode.Text();
        LvTemplateType = _recipe.Leveling.TemplateType.Text();
        LvScore = _recipe.Leveling.MatchScore.ToString("0.###");
        LvExecuted = _recipe.Leveling.LastExecutionPassed != null;
        LvPassed = _recipe.Leveling.LastExecutionPassed == true;
        LvScoreText = _recipe.Leveling.LastExecutionPassed == null
            ? string.Empty
            : $"最近一次匹配分数：{_recipe.Leveling.LastExecutionScore:F1}（阈值 {_recipe.Leveling.MatchScore}）";
        LvResultText = _recipe.Leveling.LastExecutionPassed switch
        {
            null => "尚未执行拉平：在视窗图像上拖拽框选特征区域（自动执行），或填写区域坐标后点击「执行拉平」（F-3.2.2-38/39）",
            true => "拉平执行成功：可完成本步（F-3.2.2-39）",
            false => "拉平执行失败：请调整框选区域（含特征的 Die 区域）后重新执行（BR-24）"
        };
        LvAdvisories = string.Empty;

        // 步骤 3 Mark（3 行固定）与 Mark 设置（F-3.2.2-41~49）。
        MarkRows.Clear();
        for (int i = 0; i < 3; i++)
        {
            var point = _recipe.Marks.FirstOrDefault(m => m.Index == i);
            MarkRows.Add(new MarkRow(i)
            {
                Row = point?.Row.ToString() ?? string.Empty,
                Col = point?.Col.ToString() ?? string.Empty,
                PlatformX = point?.PlatformX.ToString() ?? string.Empty,
                PlatformY = point?.PlatformY.ToString() ?? string.Empty
            });
        }
        MkMethod = _recipe.MarkSettings.Method.Text();
        MkThumbRatio = _recipe.MarkSettings.ThumbnailRatio.ToString("0.####");
        MkMagnifier = _recipe.MarkSettings.ThumbnailMagnifier;
        MkSpeed = _recipe.MarkSettings.ScanAxisSpeed.ToString("0.###");
        SelectedMarkIndex = 0;

        // 步骤 4 参考图像（F-3.2.2-60~67，含 H03 规划结果回填）。
        RiImagePath = _recipe.ReferenceImage.ImagePath;
        RiCount = _recipe.ReferenceImage.ReferenceDieCount.ToString();
        RiFovZoom = _recipe.ReferenceImage.FovZoom.ToString("0.###");
        RiBlock = _recipe.ReferenceImage.BlockParam;
        RiGoldenParam = _recipe.ReferenceImage.GoldenParam;
        RiGenerated = _recipe.ReferenceImage.Generated;
        RiSummaryText = _recipe.ReferenceImage.Generated
            ? SummarizeGoldenDie(_recipe.ReferenceImage.GoldenDie)
            : "尚未生成参考图像（F-3.2.2-67：生成成功后才允许下一步）";

        // 步骤 5/6：ROI 与扫描区域统一从 H04 唯一数据源回填（AC-P2-03，不读工作副本的旧投影）。
        SyncRoiFromStore();

        // 步骤 7 复判设置（BR-20）。
        RcMode = _recipe.Recheck.Mode.Text();
        RcUseReviewCamera = _recipe.Recheck.UseReviewCamera;

        ClearValidation();
        StatusText = _wizard.IsFinished
            ? $"已载入子配方：{_recipe.Name}（7/7 步已完成）"
            : $"已载入子配方：{_recipe.Name}（向导进度已恢复）";
        RefreshSteps();
    }

    [RelayCommand]
    private void NewSubRecipe()
    {
        string name = _dialogs.PromptJobName("新增子配方（F-3.2.2-01，十字新增）", MakeDraftName());
        if (string.IsNullOrWhiteSpace(name)) return;

        try
        {
            var created = _subs.Create(_parentJobId, name);
            StatusText = $"已新增子配方：{created.Name}（{created.Id}），从步骤 1「基础信息」开始";
            ReloadAndSelect(created.Id);
        }
        catch (InvalidOperationException ex)
        {
            _dialogs.ShowMessage("新增失败", ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(HasRecipe))]
    private void DuplicateSubRecipe()
    {
        try
        {
            var copy = _subs.Duplicate(_recipe.Id);
            StatusText = $"已复制子配方：{copy.Name}（F-3.2.2-04，含 7 步进度）";
            ReloadAndSelect(copy.Id);
        }
        catch (InvalidOperationException ex)
        {
            _dialogs.ShowMessage("复制失败", ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(HasRecipe))]
    private void RenameSubRecipe()
    {
        string name = _dialogs.PromptJobName("重命名子配方（F-3.2.2-03）", _recipe.Name);
        if (string.IsNullOrWhiteSpace(name)) return;

        try
        {
            var renamed = _subs.Rename(_recipe.Id, name);
            StatusText = $"已重命名子配方：{renamed.Name}";
            ReloadAndSelect(renamed.Id);
        }
        catch (InvalidOperationException ex)
        {
            _dialogs.ShowMessage("重命名失败", ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(HasRecipe))]
    private void DeleteSubRecipe()
    {
        if (!_dialogs.Confirm("删除子配方（F-3.2.2-02）",
                $"确认删除子配方「{_recipe.Name}」及其 7 步配置？\n删除后不可恢复。")) return;

        try
        {
            _subs.Delete(_recipe.Id);
            StatusText = $"已删除子配方：{_recipe.Name}";
            _loading = true;
            SelectedSubRecipe = null;
            _loading = false;
            ReloadSubs();
        }
        catch (InvalidOperationException ex)
        {
            _dialogs.ShowMessage("删除失败", ex.Message);
        }
    }

    private string MakeDraftName()
    {
        var names = SubRecipes.Select(r => r.Name).ToList();
        for (int i = 1; i < 1000; i++)
        {
            string candidate = $"子配方 {i}";
            if (!names.Any(n => string.Equals(n, candidate, StringComparison.OrdinalIgnoreCase))) return candidate;
        }
        return "子配方";
    }

    private void ReloadAndSelect(string subRecipeId)
    {
        ReloadSubs();
        var row = SubRecipes.FirstOrDefault(r => r.Id == subRecipeId);
        if (row == null) return;
        _loading = true;
        SelectedSubRecipe = row;
        _loading = false;
        LoadRecipe(row.Source);
    }

    /* ---------------- 步骤导航（BR-19 可重入 + B-25 门禁） ---------------- */

    private void RefreshSteps()
    {
        Steps.Clear();
        for (int i = 0; i < SubRecipeWizard.OrderedSteps.Count; i++)
        {
            var key = SubRecipeWizard.OrderedSteps[i];
            var state = _wizard.StateOf(key);
            Steps.Add(new WizardStepRow(i, key)
            {
                StatusText = state.Status.Text(),
                IsCurrent = _wizard.Current.Key == key,
                IsEnabled = HasRecipe && _wizard.CanEnter(key),
                FailureNote = state.FailureReason
            });
        }

        IsBasicInfo = _wizard.Current.Key == WizardStepKey.BasicInfo;
        IsLeveling = _wizard.Current.Key == WizardStepKey.Leveling;
        IsMark = _wizard.Current.Key == WizardStepKey.Mark;
        IsReferenceImage = _wizard.Current.Key == WizardStepKey.ReferenceImage;
        IsRoi = _wizard.Current.Key == WizardStepKey.Roi;
        IsScanArea = _wizard.Current.Key == WizardStepKey.ScanArea;
        IsRecheck = _wizard.Current.Key == WizardStepKey.Recheck;

        StepNote = _wizard.Current.Key switch
        {
            WizardStepKey.BasicInfo =>
                "T-005 已确认全字段（5 分区 33 字段）：带 * 为必填；图谱 Notch 方向为截图红色 * 项。",
            WizardStepKey.Leveling =>
                "在视窗图像上拖拽框选含特征的 Die 区域（自动执行拉平，F-3.2.2-38）；模板类型与匹配分数为全系统共用配置（B-08，AC-P2-04）。图像为模拟视窗（H06 采集随 T-029 搁置），匹配计算真实执行。",
            WizardStepKey.Mark =>
                "拖动 Map 网格对位后「确定 Map 定位」锁定（F-3.2.2-46）；双击 Die 网格自动填入行列（F-3.2.2-59）；「指定 Die 左上角」写入平台 X/Y（F-3.2.2-50）；点位重置需二次确认（T-024）。",
            WizardStepKey.ReferenceImage =>
                "设置参考 Die 数量并「创建参考图像」（F-3.2.2-67）；分类口径：黄=待检测、蓝=预聚焦、红=参考 Die（可能没有）、绿=随机选中参考 Die。GoldenDie 由 H03 唯一生成（B-09），H08 只读展示。",
            WizardStepKey.Roi =>
                "ROI（H04 已接入）：在 Map 画布拖拽圈选区域（F-3.2.2-70），按 7 步流程编辑检测设置项并逐项保存（F-3.2.2-75）；" +
                "Pad 检测选 Pad、其他选 Zone（F-3.2.2-68），参数类型默认 Surface（BR-35）。与「缺陷分析 · 参数验证」共用同一数据源（AC-P2-03）。",
            WizardStepKey.ScanArea =>
                "扫描区域（H04 已接入，BR-37~39）：默认全片扫描；拖拽 Map 画布添加局部区域（可多个），「清除扫描区域」清空全部；" +
                "抽检模式可双击选单个 Die（可多个）。本页的区域与参数验证页是同一份数据（AC-P2-03）。",
            WizardStepKey.Recheck =>
                "复判设置（F-3.2.2-80~82，BR-20：默认离线复判 + 复查相机）。保存后双击 Map 定位 Die 的校验动作由 H03 图形能力支持（下一增量）；本配置写入子配方，运行时由 H08 读取。",
            _ => string.Empty
        };
    }

    /// <summary>进入拉平步骤时准备模拟视窗图像与光照校准报告（BR-21）。</summary>
    partial void OnIsLevelingChanged(bool value)
    {
        if (!value || !HasRecipe) return;
        EnsureFovPreview();
    }

    /// <summary>进入 Mark 步骤时准备 Map 定位网格（F-3.2.2-45：优先导入 Map 文件，缺省按 Die 尺寸生成默认网格）。</summary>
    partial void OnIsMarkChanged(bool value)
    {
        if (!value || !HasRecipe) return;
        EnsureMarkGrid();
    }

    private void EnsureFovPreview()
    {
        _fovImage = SyntheticFovImage.Generate(FovWidth, FovHeight);
        FovPreview = ToBitmapSource(_fovImage);

        var report = _lightAnalyzer.Analyze(_fovImage);
        LvLightText = report.IsOk
            ? $"光照校准：正常（均值 {report.MeanBrightness:F1}，饱和占比 {report.SaturatedFraction:P1}，BR-21）"
            : $"光照校准建议（BR-21）：{string.Join("；", report.Advisories)}";
    }

    private static BitmapSource ToBitmapSource(GrayImage image)
    {
        var bitmap = new WriteableBitmap(image.Width, image.Height, 96, 96, PixelFormats.Gray8, null);
        bitmap.WritePixels(new System.Windows.Int32Rect(0, 0, image.Width, image.Height), image.Pixels, image.Width, 0);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>点击步骤行进入（BR-19：可重入；被门禁拦截时给出 B-25 原因，不抛异常）。</summary>
    [RelayCommand]
    private void EnterStep(WizardStepRow row)
    {
        if (row == null || !HasRecipe) return;

        if (!_wizard.TryEnter(row.Key))
        {
            var first = _wizard.FirstIncomplete;
            StatusText = first is { } key && key != row.Key
                ? $"步骤进入被门禁拦截：必须先完成「{key.Text()}」（B-25：成功后才允许下一步）"
                : "步骤进入被门禁拦截：前置步骤失败，请先重新执行并成功（B-25）";
            return;
        }

        ClearValidation();
        RefreshSteps();
        StatusText = $"已进入步骤 {row.Index + 1}/7「{row.Key.Text()}」（BR-19：已完成步骤可重新执行）";
    }

    [RelayCommand]
    private void NextStep()
    {
        if (!HasRecipe) return;

        if (_wizard.Current.Status != WizardStepStatus.Completed)
        {
            StatusText = "请先点击「保存并完成本步」，成功后才能进入下一步（B-25）。";
            return;
        }

        if (!_wizard.TryEnterNext())
        {
            StatusText = _wizard.IsFinished ? "7 步全部完成，向导已收口。" : "已是最后一步。";
            return;
        }

        ClearValidation();
        RefreshSteps();
        StatusText = $"已进入「{_wizard.Current.Key.Text()}」";
    }

    [RelayCommand]
    private void PreviousStep()
    {
        if (!HasRecipe) return;

        if (!_wizard.TryEnterPrevious())
        {
            StatusText = "已是最早步骤（基础信息）。";
            return;
        }

        ClearValidation();
        RefreshSteps();
        StatusText = $"已回退到「{_wizard.Current.Key.Text()}」（BR-19：回退不改变已完成状态）";
    }

    /* ---------------- 完成当前步骤（各步门禁） ---------------- */

    [RelayCommand]
    private void CompleteStep()
    {
        if (!HasRecipe) return;

        var currentKey = _wizard.Current.Key;
        var issues = currentKey switch
        {
            WizardStepKey.BasicInfo => EvaluateBasicInfo(),
            WizardStepKey.Leveling => EvaluateLeveling(),
            WizardStepKey.Mark => EvaluateMarks(),
            WizardStepKey.ReferenceImage => EvaluateReferenceImage(),
            WizardStepKey.Roi => EvaluateRoi(),
            WizardStepKey.ScanArea => EvaluateScanArea(),
            WizardStepKey.Recheck => EvaluateRecheck(),
            _ => []
        };

        if (issues.Count > 0)
        {
            // B-25：失败必须显式可见，并锁住后续步骤。
            string reason = string.Join("；", issues.Select(i => $"{i.Field}：{i.Message}"));
            string summary = string.Join(Environment.NewLine, issues.Select(i => $"· {i.Field}：{i.Message}"));
            _wizard.FailCurrent(reason);
            Persist();
            // Persist 触发的重载会清空校验面板（LoadRecipe → ClearValidation），
            // 失败呈现必须在其后恢复，保证 B-25 失败原因始终可见。
            HasValidationError = true;
            ValidationSummary = summary;
            _wizard.Focus(currentKey);   // 停留在失败步骤，保持 Failed 状态可见
            RefreshSteps();
            StatusText = $"步骤「{currentKey.Text()}」未通过：已按失败处理（B-25），修正后请重新完成本步。";
            return;
        }

        _wizard.CompleteCurrent();
        ClearValidation();
        Persist();

        if (_wizard.IsFinished)
        {
            _wizard.Focus(currentKey);
            RefreshSteps();
            StatusText = "7 步全部完成：子配方创建流程收口（B-25）。";
            return;
        }

        // 成功后进入下一步（T-042 顺序）。
        int currentIndex = 0;
        for (int i = 0; i < SubRecipeWizard.OrderedSteps.Count; i++)
            if (SubRecipeWizard.OrderedSteps[i] == currentKey) { currentIndex = i; break; }
        _wizard.Focus(currentKey);
        _wizard.Enter(SubRecipeWizard.OrderedSteps[currentIndex + 1]);
        RefreshSteps();
        StatusText = $"步骤「{currentKey.Text()}」完成，已进入「{_wizard.Current.Key.Text()}」";
    }

    private List<SubRecipeValidationIssue> EvaluateBasicInfo()
    {
        _recipe.Name = BiName?.Trim() ?? string.Empty;
        _recipe.BasicInfo = BuildBasicInfo();
        var issues = SubRecipeValidator.Validate(_recipe.BasicInfo).ToList();

        // 同名检查交给仓库在持久化时把关；此处先把名称同步到工作副本。
        return issues;
    }

    private List<SubRecipeValidationIssue> EvaluateLeveling()
    {
        _recipe.Leveling = BuildLevelingConfig();
        var issues = SubRecipeValidator.ValidateLeveling(_recipe.Leveling).ToList();

        // F-3.2.2-39：拉平成功后才允许下一步（B-25 门禁的步骤级口径）。
        if (_recipe.Leveling.LastExecutionPassed != true)
            issues.Add(new("Leveling", "必须先「执行拉平」并成功后才能完成本步（F-3.2.2-39）：请在视窗图像上框选含特征的 Die 区域"));

        return issues;
    }

    private List<SubRecipeValidationIssue> EvaluateMarks()
    {
        var marks = new List<MarkPoint>();
        foreach (var row in MarkRows)
        {
            if (!int.TryParse(row.Row?.Trim(), out int r) || !int.TryParse(row.Col?.Trim(), out int c))
                return [new("Marks", $"Mark {row.Index + 1} 行/列必须为整数（双击 Die 网格自动填入，F-3.2.2-59）")];

            marks.Add(new MarkPoint(row.Index, r, c,
                ParseDouble(row.PlatformX, 0), ParseDouble(row.PlatformY, 0)));
        }
        _recipe.Marks = marks;

        var method = ParseMarkMethod(MkMethod);
        _recipe.MarkSettings = new MarkSettingsConfig
        {
            ThumbnailRatio = ParseDouble(MkThumbRatio, 0.25),
            ThumbnailMagnifier = MkMagnifier?.Trim() ?? "默认",
            ScanAxisSpeed = ParseDouble(MkSpeed, 100),
            Method = method,
            GridLocked = _grid.IsLocked,
            GridOffsetX = _grid.OffsetX,
            GridOffsetY = _grid.OffsetY
        };

        var issues = _markService.Validate(marks, method).ToList();

        // BR-27：缩略图比例必须为 1/2ⁿ。
        double ratio = ParseDouble(MkThumbRatio, 0);
        if (!ThumbnailRules.IsPowerOfTwoRatio(ratio))
            issues.Add(new("MarkSettings", $"缩略图比例必须为 1/2ⁿ（BR-27），当前为 {MkThumbRatio}"));

        // F-3.2.2-46：确定 Map 定位（锁定）后才能双击填点，为完成本步前置。
        if (!_grid.IsLocked)
            issues.Add(new("MarkSettings", "必须先「确定 Map 定位」锁定网格后才能完成本步（F-3.2.2-46，必做）"));

        return issues;
    }

    private List<SubRecipeValidationIssue> EvaluateReferenceImage()
    {
        // F-3.2.2-67：创建参考图像成功后才允许下一步；本地图像路径可选（采集属 H06，T-029 搁置）。
        _recipe.ReferenceImage = new ReferenceImageConfig
        {
            ImagePath = RiImagePath?.Trim() ?? string.Empty,
            ReferenceDieCount = (int)ParseDouble(RiCount, 0),
            FovZoom = ParseDouble(RiFovZoom, 1),
            BlockParam = RiBlock?.Trim() ?? string.Empty,
            GoldenParam = RiGoldenParam?.Trim() ?? string.Empty,
            Generated = RiGenerated,
            ManualAdjusted = _recipe.ReferenceImage.ManualAdjusted,
            GoldenDie = _recipe.ReferenceImage.GoldenDie
        };

        if (!RiGenerated)
            return [new("ReferenceImage", "必须先「创建参考图像」并成功后才能完成本步（F-3.2.2-67，必做）")];

        if (_recipe.ReferenceImage.GoldenDie is { NeedsRegeneration: true })
            return [new("ReferenceImage", "视野缩放已修改，需重新生成参考图像（BR-30 / F-3.2.2-62）")];

        return [];
    }

    private List<SubRecipeValidationIssue> EvaluateRoi()
    {
        if (!ApplyRoiRowEdits(out string error))
            return [new("RoiZones", error)];

        // AC-P2-03：校验直接针对 H04 唯一数据源的快照，不针对界面副本。
        var snapshot = _store.Snapshot(_recipe.Id);
        return InspectionValidator.ValidateRoi(snapshot).ToList();
    }

    private List<SubRecipeValidationIssue> EvaluateScanArea()
    {
        var snapshot = _store.Snapshot(_recipe.Id);
        return InspectionValidator.ValidateScanArea(snapshot).ToList();
    }

    /* ---------------- H04：步骤 5/6 与唯一数据源（B-01/B-02、AC-P2-03） ---------------- */

    /// <summary>从 H04 唯一数据源回填 ROI 区域、检测项、Map 画布与扫描区域状态。</summary>
    private void SyncRoiFromStore()
    {
        if (!HasRecipe || string.IsNullOrEmpty(_recipe.Id)) return;

        _syncingRoi = true;
        try
        {
            var snapshot = _store.Snapshot(_recipe.Id);
            Map.Rebuild(snapshot);

            RoiRows.Clear();
            foreach (var zone in snapshot.Zones)
                RoiRows.Add(new RoiZoneRow(zone.Index)
                {
                    Name = zone.Name,
                    X = zone.X.ToString("0.###"), Y = zone.Y.ToString("0.###"),
                    Width = zone.Width.ToString("0.###"), Height = zone.Height.ToString("0.###")
                });

            RoiItemRows.Clear();
            foreach (var item in snapshot.DetectionItems) RoiItemRows.Add(ToRoiItemRow(item));

            ScanFullWafer = snapshot.FullWafer;
            SamplingEnabled = snapshot.SamplingEnabled;
            RoiSourceText = $"H04 唯一数据源 · ROI {snapshot.Zones.Count} 个 / 检测项 {snapshot.DetectionItems.Count} 项 / " +
                            $"扫描区域 {snapshot.ScanRegions.Count} 个 / 抽检 Die {snapshot.SamplingDies.Count} 颗 · " +
                            $"参数 {snapshot.Parameters.Count} 项（T-014 可扩展）· 自动保存 v{snapshot.SavedVersion}";
            RoiItemStatusText = snapshot.DetectionItems.Count == 0
                ? "尚未添加检测设置项（F-3.2.2-69：每项为独立检测单元）"
                : $"检测设置项 {snapshot.DetectionItems.Count} 项，已保存 {snapshot.DetectionItems.Count(i => i.Saved)} 项";
        }
        finally
        {
            _syncingRoi = false;
        }
    }

    private static RoiItemRow ToRoiItemRow(DetectionItem item) => new(item.Id)
    {
        Name = item.Name,
        Kind = item.Kind.Text(),
        AlgorithmType = item.AlgorithmType,
        ParameterType = item.ParameterType,
        Extraction = item.Extraction.Text(),
        GoldenTone = item.GoldenTone.Text(),
        RegionX = item.GoldenTemplateRegion.X.ToString("0.###"),
        RegionY = item.GoldenTemplateRegion.Y.ToString("0.###"),
        RegionW = item.GoldenTemplateRegion.Width.ToString("0.###"),
        RegionH = item.GoldenTemplateRegion.Height.ToString("0.###"),
        RoiZoneIndex = item.RoiZoneIndex,
        Saved = item.Saved,
        StatusText = item.Saved ? "已保存" : "未保存"
    };

    /// <summary>把区域表的文本编辑批量写回唯一数据源；解析失败整体回滚前不写。</summary>
    private bool ApplyRoiRowEdits(out string error)
    {
        error = string.Empty;
        if (!HasRecipe) return true;

        var parsed = new List<RoiZone>();
        foreach (var row in RoiRows)
        {
            double x = ParseDouble(row.X, 0), y = ParseDouble(row.Y, 0);
            double w = ParseDouble(row.Width, 0), h = ParseDouble(row.Height, 0);
            if (x < 1 || y < 1 || w <= 0 || h <= 0)
            {
                error = $"ROI「{row.Name}」坐标必须为 1-based Die 网格且宽高大于 0（F-3.2.2-70）；当前 X={row.X} Y={row.Y} W={row.Width} H={row.Height}";
                return false;
            }
            parsed.Add(new RoiZone(row.Index, row.Name?.Trim() ?? string.Empty, x, y, w, h));
        }

        _suppressStoreSync = true;
        try
        {
            foreach (var zone in parsed)
                _store.UpdateRoiZone(_recipe.Id, zone.Index,
                    new MapRect(zone.X, zone.Y, zone.Width, zone.Height), zone.Name);
        }
        finally
        {
            _suppressStoreSync = false;
        }
        return true;
    }

    /// <summary>共享画布拖拽：步骤 5 生成 ROI 区域，步骤 6 生成扫描区域（同一数据源，不复制实现）。</summary>
    public void AddRegion(MapRect rect)
    {
        if (!HasRecipe) return;
        if (_wizard.Current.Key == WizardStepKey.Roi)
        {
            _store.AddRoiZone(_recipe.Id, rect, null, ParseTargetKind(RoiItemRows.FirstOrDefault()?.Kind));
            StatusText = $"已在 Map 画布圈选 ROI 区域（F-3.2.2-70，列 {rect.X:0} 起 {rect.Width:0} 列）";
        }
        else
        {
            _store.AddScanRegion(_recipe.Id, rect, "向导步骤 6 · Map 圈选");
            StatusText = $"已添加扫描区域（F-3.2.2-77，列 {rect.X:0} 起 {rect.Width:0} 列）";
        }
    }

    /// <summary>共享画布双击：抽检 Die（BR-38，可多个；扫描区域步骤生效）。</summary>
    public void ToggleDie(int row, int column)
    {
        if (!HasRecipe) return;
        _store.ToggleSamplingDie(_recipe.Id, row, column);
        StatusText = $"抽检 Die（行 {row} / 列 {column}）已切换（BR-38）";
    }

    [RelayCommand]
    private void AddRoiZone()
    {
        if (!HasRecipe) return;
        _store.AddRoiZone(_recipe.Id, new MapRect(1, 1, 4, 4));
        StatusText = "已添加 ROI 区域（默认 4×4 Die，可在区域表或 Map 画布调整）";
    }

    [RelayCommand]
    private void RemoveRoiZone(RoiZoneRow row)
    {
        if (row == null || !HasRecipe) return;
        _store.RemoveRoiZone(_recipe.Id, row.Index);
        StatusText = "已移除 ROI 区域（索引连续化，检测项引用同步更新）";
    }

    [RelayCommand]
    private void ApplyRoiRows()
    {
        if (!ApplyRoiRowEdits(out string error))
        {
            _dialogs.ShowMessage("应用 ROI 区域编辑失败", error);
            return;
        }
        SyncRoiFromStore();
        StatusText = "已把区域表编辑写入 H04 唯一数据源（AC-P2-03）";
    }

    [RelayCommand]
    private void AddDetectionItem()
    {
        if (!HasRecipe) return;
        RoiItemRows.Add(new RoiItemRow(string.Empty)
        {
            Name = $"检测项 {RoiItemRows.Count + 1}",
            StatusText = "未保存（需点击「保存本项」）"
        });
        RoiItemStatusText = "已新增检测设置项：填写名称/算法类型/区域后点击「保存本项」（F-3.2.2-75）";
    }

    [RelayCommand]
    private void SaveDetectionItem(RoiItemRow row)
    {
        if (row == null || !HasRecipe) return;
        double x = ParseDouble(row.RegionX, 1), y = ParseDouble(row.RegionY, 1);
        double w = ParseDouble(row.RegionW, 1), h = ParseDouble(row.RegionH, 1);
        if (x < 1 || y < 1 || w <= 0 || h <= 0)
        {
            _dialogs.ShowMessage("保存检测项失败", "Golden Die 模板中的检测区域必须为 1-based Die 网格且宽高 > 0。");
            return;
        }

        var item = new DetectionItem
        {
            Id = row.Id,
            Name = row.Name?.Trim() ?? string.Empty,
            Kind = ParseTargetKind(row.Kind),
            AlgorithmType = row.AlgorithmType?.Trim() ?? string.Empty,
            GoldenTemplateRegion = new MapRect(x, y, w, h),
            RoiZoneIndex = row.RoiZoneIndex,
            ParameterType = string.IsNullOrWhiteSpace(row.ParameterType)
                ? InspectionText.DefaultParameterType : row.ParameterType.Trim(),
            Extraction = ParseExtraction(row.Extraction),
            GoldenTone = ParseGoldenTone(row.GoldenTone),
            // BR-36：有效/屏蔽区域仅限制区域提取算法生效范围，不影响检测范围；此处保留配置位。
            EffectiveRegion = null,
            MaskedRegion = null,
            Parameters = _store.Snapshot(_recipe.Id).Parameters
        };

        try
        {
            var saved = _store.SaveDetectionItem(_recipe.Id, item);
            row.Id = saved.Id;
            row.Saved = true;
            row.StatusText = "已保存";
            SyncRoiFromStore();
            StatusText = $"检测项「{saved.Name}」已保存（F-3.2.2-75 第 6 步；右下角保存后才算完成）";
        }
        catch (InvalidOperationException ex)
        {
            _dialogs.ShowMessage("保存检测项失败", ex.Message);
        }
    }

    [RelayCommand]
    private void RemoveDetectionItem(RoiItemRow row)
    {
        if (row == null || !HasRecipe) return;
        if (!string.IsNullOrEmpty(row.Id)) _store.RemoveDetectionItem(_recipe.Id, row.Id);
        RoiItemRows.Remove(row);
        StatusText = "已移除检测设置项";
    }

    /// <summary>步骤 6 键盘 / UIA 等价路径：引用步骤 5 已定义的 ROI 区域作为扫描区域。</summary>
    [RelayCommand]
    private void AddScanRegionsFromRoi()
    {
        if (!HasRecipe) return;
        var zones = _store.Snapshot(_recipe.Id).Zones;
        if (zones.Count == 0)
        {
            _dialogs.ShowMessage("添加扫描区域", "请先在步骤 5 定义 ROI 区域（或在 Map 画布拖拽圈选）。");
            return;
        }

        _suppressStoreSync = true;
        try
        {
            foreach (var zone in zones)
                _store.AddScanRegion(_recipe.Id, MapRect.From(zone), "向导步骤 6 · 引用 ROI");
        }
        finally
        {
            _suppressStoreSync = false;
        }
        SyncRoiFromStore();
        StatusText = $"已引用 {zones.Count} 个 ROI 区域作为扫描区域（BR-39 支持多个）";
    }

    [RelayCommand]
    private void ClearScanRegions()
    {
        if (!HasRecipe) return;
        _store.ClearScanRegions(_recipe.Id);
        StatusText = "已清除全部扫描区域并恢复默认全片扫描（BR-37/39）";
    }

    [RelayCommand]
    private void ClearSamplingDies()
    {
        if (!HasRecipe) return;
        _store.ClearSamplingDies(_recipe.Id);
        StatusText = "已清空抽检 Die";
    }

    private static RoiTargetKind ParseTargetKind(string text)
        => text == RoiTargetKind.Pad.Text() ? RoiTargetKind.Pad : RoiTargetKind.Zone;

    private static RegionExtraction ParseExtraction(string text)
        => text == RegionExtraction.Binarization.Text() ? RegionExtraction.Binarization : RegionExtraction.TemplateMatching;

    private static GoldenImageTone ParseGoldenTone(string text)
        => text == GoldenImageTone.Dark.Text() ? GoldenImageTone.Dark : GoldenImageTone.Bright;

    private List<SubRecipeValidationIssue> EvaluateRecheck()
    {
        _recipe.Recheck = new RecheckConfig
        {
            Mode = ParseRecheckMode(RcMode),
            UseReviewCamera = RcUseReviewCamera   // BR-20：默认复查相机
        };
        return [];
    }

    /// <summary>完成/失败后持久化（步骤状态随子配方保存，重开向导不丢进度）。</summary>
    private void Persist()
    {
        // Update 触发的 Changed → ReloadSubs → LoadRecipe 会重建 _wizard 并重定位焦点；
        // 此处恢复保存前的当前步骤，避免命令级持久化（执行拉平 / 创建参考图像等）把视图焦点重置回步骤 1。
        var keepCurrent = _wizard.Current.Key;
        try
        {
            // AC-P2-03：持久化前把 H04 唯一数据源的状态写回子配方（这是唯一写回出口，界面不另写副本）。
            if (HasRecipe && !string.IsNullOrEmpty(_recipe.Id)) _store.ApplyTo(_recipe.Id, _recipe);
            _recipe = _subs.Update(_recipe);
            _wizard = new SubRecipeWizard(_recipe);
            _wizard.Focus(keepCurrent);
        }
        catch (InvalidOperationException ex)
        {
            // 基础信息未完成的中间步骤保存失败：向导状态仍保留在内存，提示用户回步骤 1 补齐。
            HasValidationError = true;
            ValidationSummary = ex.Message;
            StatusText = "保存子配方失败：请回步骤 1「基础信息」补齐必填项后再完成后续步骤。";
        }
    }
    /* ---------------- 步骤 1 字段组装（T-005 口径） ---------------- */

    private SubRecipeBasicInfo BuildBasicInfo() => new()
    {
        Name = BiName?.Trim() ?? string.Empty,
        PatternType = ParsePatternType(BiPatternType),
        Size = ParseSize(BiSize),
        Material = string.IsNullOrWhiteSpace(BiMaterial) ? "Si" : BiMaterial,
        ThicknessMm = ParseDouble(BiThickness, 0),
        DieXmm = ParseDouble(BiDieX, 0),
        DieYmm = ParseDouble(BiDieY, 0),
        NotchAngle = ParseDouble(BiNotch, 270),
        MapTypeId = MapTypeIdOf(BiMapType),
        MapFileType = ParseMapFileType(BiMapFileType),
        BinGradeLevel = (int)ParseDouble(BiBinGrade, 0),
        ColumnDirection = ParseDirection(BiColumnDirection),
        RowDirection = ParseDirection(BiRowDirection),
        MapFilePath = BiMapPath?.Trim() ?? string.Empty,
        MapNotchAngle = ParseDouble(BiMapNotch, 270),
        ParseScriptPath = BiParseScript?.Trim() ?? string.Empty,
        BarcodeCode = BiBarcode?.Trim() ?? string.Empty,
        OcrJob = BiOcrJob?.Trim() ?? string.Empty,
        OcrAngle = ParseDouble(BiOcrAngle, 0),
        OcrPosition = string.Equals(BiOcrPosition, OcrPosition.Back.Text(), StringComparison.Ordinal)
            ? OcrPosition.Back : OcrPosition.Front,
        LocateDieX = ParseNullableInt(BiLocateDieX),
        LocateDieY = ParseNullableInt(BiLocateDieY),
        Orientation = BiOrientation?.Trim() ?? string.Empty,
        ColumnOffset = (int)ParseDouble(BiColumnOffset, 0),
        RowOffset = (int)ParseDouble(BiRowOffset, 0),
        ColRow = BiColRow?.Trim() ?? string.Empty,
        Xy = BiXy?.Trim() ?? string.Empty,
        IncludeExclude = BiIncludeExclude?.Trim() ?? string.Empty,
        UseKlarCenterTool = BiUseKlarCenterTool,
        TraceImage = ParseTraceImage(BiTraceImage),
        MappingThicknessMm = ParseDouble(BiMappingThickness, 0),
        MappingSlotNum = (int)ParseDouble(BiMappingSlotNum, 0),
        SamplingZoneEnabled = BiSamplingZoneEnabled
    };

    /* ---------------- Map / 参考图像文件选择 ---------------- */

    [RelayCommand]
    private void BrowseMapFile()
    {
        string path = _dialogs.PickMapFile(BiMapPath);
        if (string.IsNullOrWhiteSpace(path)) return;
        BiMapPath = path;
        StatusText = $"已选择 Map 文件：{path}（导入回填在 Job 公共参数表单同样可用）";
    }

    [RelayCommand]
    private void BrowseReferenceImage()
    {
        string path = _dialogs.PickImageFile(RiImagePath);
        if (string.IsNullOrWhiteSpace(path)) return;
        RiImagePath = path;
        StatusText = $"已选择参考图像：{path}";
    }

    /* ---------------- H03：拉平执行（F-3.2.2-38/39，B-08 引擎） ---------------- */

    /// <summary>图形框选路径：拖拽结束自动执行（F-3.2.2-38），由向导窗口 code-behind 调用。</summary>
    public void ExecuteLeveling(int x, int y, int width, int height)
    {
        FovSelX = x; FovSelY = y; FovSelW = width; FovSelH = height;
        HasFovSelection = true;
        LvRegionX = x.ToString(); LvRegionY = y.ToString();
        LvRegionW = width.ToString(); LvRegionH = height.ToString();
        RunLeveling(new TemplateRegion(x, y, width, height));
    }

    /// <summary>键盘 / UIA 路径：按输入框区域执行（与图形框选等价）。</summary>
    [RelayCommand]
    private void ExecuteLevelingFromFields()
    {
        var region = new TemplateRegion(
            (int)ParseDouble(LvRegionX, 0), (int)ParseDouble(LvRegionY, 0),
            (int)ParseDouble(LvRegionW, 0), (int)ParseDouble(LvRegionH, 0));
        RunLeveling(region);
    }

    private void RunLeveling(TemplateRegion region)
    {
        var built = BuildLevelingConfig();
        var outcome = _matcher.Match(_fovImage, region, built);   // B-08：全系统唯一匹配实现

        _recipe.Leveling = new LevelingConfig
        {
            Mode = built.Mode,
            TemplateType = built.TemplateType,
            MatchScore = built.MatchScore,
            LastExecutionPassed = outcome.Passed,
            LastExecutionScore = outcome.Score,
            LastExecutionImageSource = "模拟视窗图像（SyntheticFovImage，确定性；H06 真实采集随 T-029 搁置）"
        };

        LvExecuted = true;
        LvPassed = outcome.Passed;
        LvScoreText = $"匹配分数：{outcome.Score:F1}（阈值 {built.MatchScore:F0}，BR-24）";
        LvAdvisories = outcome.Advisories.Count > 0
            ? "建议：" + string.Join("；", outcome.Advisories)
            : string.Empty;
        LvResultText = outcome.Passed
            ? $"拉平成功（{built.TemplateType.Text()}，B-08 引擎）：可完成本步（F-3.2.2-39）"
            : $"拉平失败：分数 {outcome.Score:F1} 未高于阈值 {built.MatchScore:F0}（BR-24），请框选含特征的 Die 区域后重新执行";

        if (outcome.Passed)
        {
            Persist();
            StatusText = $"拉平执行成功：分数 {outcome.Score:F1}（B-08 唯一实现，AC-P2-04 取值口径）";
        }
        else
        {
            StatusText = "拉平执行失败：修正框选区域后重新执行（B-25：失败不影响后续进入，但本步完成前须成功）";
        }
    }

    private LevelingConfig BuildLevelingConfig() => new()
    {
        Mode = ParseLevelingMode(LvMode),
        TemplateType = ParseTemplateType(LvTemplateType),
        MatchScore = ParseDouble(LvScore, 70),
        LastExecutionPassed = _recipe.Leveling.LastExecutionPassed,
        LastExecutionScore = _recipe.Leveling.LastExecutionScore,
        LastExecutionImageSource = _recipe.Leveling.LastExecutionImageSource
    };

    /* ---------------- H03：Mark 网格与点位操作（F-3.2.2-40~59） ---------------- */

    private const double CellSize = 16;
    private const double CellGap = 2;

    /// <summary>创建 / 恢复 Map 定位网格：优先按导入的 Map 文件，缺失时按晶圆尺寸生成默认网格并显式标注。</summary>
    private void EnsureMarkGrid()
    {
        if (_grid.IsLoaded) { RebuildMarkCells(); return; }

        int cols = 12, rows = 12;
        string source = "默认网格（未导入 Map 文件，按 12×12 生成；F-3.2.2-45 要求「必做」导入 Map，请回步骤 1 导入）";

        if (!string.IsNullOrWhiteSpace(BiMapPath))
        {
            var import = _mapImporter.Import(BiMapPath, ParseMapFileType(BiMapFileType));
            if (import.Success)
            {
                cols = import.Columns;
                rows = import.Rows;
                source = $"已导入 Map 文件：{System.IO.Path.GetFileName(BiMapPath)}（{cols}×{rows}）";
            }
            else
            {
                source = $"Map 文件导入失败（{import.Error}）；已按 12×12 默认网格显示，请回步骤 1 修正";
            }
        }

        _grid.Load(cols, rows);

        // 已持久化的网格定位状态恢复（重开向导不丢，BR-19）。
        if (_recipe.MarkSettings.GridLocked)
        {
            try
            {
                // 锁定状态恢复：直接回到锁定（偏移已在 Load 中清零，按持久化值补回）。
                _grid.Move(_recipe.MarkSettings.GridOffsetX, _recipe.MarkSettings.GridOffsetY);
                _grid.ConfirmLocation();
                source += "；已恢复确定的 Map 定位（锁定）";
            }
            catch (InvalidOperationException)
            {
                // 恢复失败按未锁定处理。
            }
        }

        RebuildMarkCells();
        RefreshGridStatusText(source);
    }

    private void RebuildMarkCells()
    {
        MarkGridCells.Clear();
        for (int row = 0; row < _grid.Rows; row++)
        for (int col = 0; col < _grid.Columns; col++)
        {
            MarkGridCells.Add(new MarkCellRow(row, col)
            {
                X = col * (CellSize + CellGap) + _grid.OffsetX,
                Y = row * (CellSize + CellGap) + _grid.OffsetY,
                Fill = "#F5C518"   // 黄色 = 待检测 Die（F-3.2.2-60）
            });
        }
    }

    private void RefreshGridStatusText(string source)
    {
        MkGridStatusText = _grid.IsLocked
            ? $"{source} · 定位已确定并锁定（F-3.2.2-46，网格不可再移动，双击 Die 填入行列）"
            : $"{source} · 未锁定：可拖动网格对位，「确定 Map 定位」后锁定";
    }

    /// <summary>图形拖动路径：网格移动（锁定前），由向导窗口 code-behind 在拖拽中调用。</summary>
    public void MoveGrid(double dx, double dy)
    {
        try
        {
            _grid.Move(dx, dy);
            RebuildMarkCells();
            RefreshGridStatusText($"网格已拖动（偏移 {_grid.OffsetX:F0}, {_grid.OffsetY:F0}）");
        }
        catch (InvalidOperationException ex)
        {
            StatusText = ex.Message;   // F-3.2.2-46：锁定后不可再移动
        }
    }

    /// <summary>双击 Die 网格 → 自动填入当前 Mark 的行列（F-3.2.2-59 步骤 5）。</summary>
    public void PickMarkDie(MarkCellRow cell)
    {
        if (cell == null) return;

        if (!_grid.IsLocked)
        {
            StatusText = "请先「确定 Map 定位」锁定网格，再双击 Die 填入行列（F-3.2.2-46 → 59 流程顺序）";
            return;
        }

        var target = MarkRows.FirstOrDefault(r => r.Index == SelectedMarkIndex);
        if (target == null) return;
        target.Row = cell.Row.ToString();
        target.Col = cell.Col.ToString();
        StatusText = $"Mark {SelectedMarkIndex + 1}：双击 Die 网格自动填入 行 {cell.Row} / 列 {cell.Col}（F-3.2.2-59）";
    }

    [RelayCommand]
    private void ConfirmMapLocation()
    {
        try
        {
            _grid.ConfirmLocation();
            RefreshGridStatusText("Map 定位已确定");
            StatusText = "Map 定位已确定并锁定：网格不可再移动（F-3.2.2-46）；现在可双击 Die 网格填入 Mark 行列";
        }
        catch (InvalidOperationException ex)
        {
            StatusText = ex.Message;
        }
    }

    [RelayCommand]
    private void ResetMapLocation()
    {
        _grid.Reset();
        MkGridStatusText = "Map 定位已重置（F-3.2.2-47）：请重新导入 / 创建网格";
        StatusText = "已重置 Map 定位（F-3.2.2-47，删除 Map 图）";
        EnsureMarkGrid();
    }

    /// <summary>F-3.2.2-50：指定 Die 左上角 → 十字光标移至定位 Die 左上角，平台 X/Y 写入当前 Mark。</summary>
    [RelayCommand]
    private void SpecifyDieTopLeft()
    {
        var target = MarkRows.FirstOrDefault(r => r.Index == SelectedMarkIndex);
        if (target == null) return;

        if (!int.TryParse(target.Row?.Trim(), out int r) || !int.TryParse(target.Col?.Trim(), out int c))
        {
            StatusText = $"Mark {SelectedMarkIndex + 1} 行列尚未填入：请先双击 Die 网格（F-3.2.2-59），再指定左上角";
            return;
        }

        double dieX = ParseDouble(BiDieX, 0) > 0 ? ParseDouble(BiDieX, 0) : 10;
        double dieY = ParseDouble(BiDieY, 0) > 0 ? ParseDouble(BiDieY, 0) : 10;

        try
        {
            var point = _markService.DieToPlatform(r, c, dieX, dieY, _grid);
            target.PlatformX = point.Xmm.ToString("0.###");
            target.PlatformY = point.Ymm.ToString("0.###");
            StatusText = $"Mark {SelectedMarkIndex + 1}：已指定 Die 左上角，平台 X={point.Xmm:F3} / Y={point.Ymm:F3}（F-3.2.2-50）";
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentOutOfRangeException)
        {
            StatusText = ex.Message;
        }
    }

    /// <summary>F-3.2.2-56 点位重置（慎用）；T-024：必须经二次确认弹窗。</summary>
    [RelayCommand]
    private void ResetMarkPoints()
    {
        if (!_dialogs.Confirm("点位重置（F-3.2.2-56，慎用）",
                "确认清空全部 3 个 Mark 点（行/列/平台坐标）？\n该操作不可撤销（手册标注「慎用」，T-024 二次确认）。"))
        {
            StatusText = "点位重置已取消（T-024 二次确认）";
            return;
        }

        _markService.ResetMarks(_recipe.Marks, confirmed: true);
        foreach (var row in MarkRows)
        {
            row.Row = string.Empty;
            row.Col = string.Empty;
            row.PlatformX = string.Empty;
            row.PlatformY = string.Empty;
        }
        StatusText = "点位重置完成：Mark 列表已清空（F-3.2.2-56，T-024 二次确认通过）";
    }

    /// <summary>F-3.2.2-54：移动到中心（Chuck 台运动 + 实时图像属设备动作）。</summary>
    [RelayCommand]
    private void MoveToCenter()
        => StatusText = "[搁置] Chuck 台「移动到中心」未下发（F-3.2.2-54，设备连接搁置 T-029；实时图像随 H06 采集接入）";

    /// <summary>F-3.2.2-55：移动到模板（同上，设备动作）。</summary>
    [RelayCommand]
    private void MoveToTemplate()
        => StatusText = "[搁置] Chuck 台「移动到模板」未下发（F-3.2.2-55，设备连接搁置 T-029）";

    /* ---------------- H03：参考图像生成（F-3.2.2-60~67，BR-29/30） ---------------- */

    [RelayCommand]
    private void CreateReferenceImage()
    {
        int count = (int)ParseDouble(RiCount, 5);
        double zoom = ParseDouble(RiFovZoom, 1);

        if (!_grid.IsLoaded) EnsureMarkGrid();

        try
        {
            var plan = _planner.Plan(_grid.Columns, _grid.Rows, count, zoom);

            _recipe.ReferenceImage = new ReferenceImageConfig
            {
                ImagePath = RiImagePath?.Trim() ?? string.Empty,
                ReferenceDieCount = count,
                FovZoom = zoom,
                BlockParam = RiBlock?.Trim() ?? string.Empty,
                GoldenParam = RiGoldenParam?.Trim() ?? string.Empty,
                Generated = true,
                ManualAdjusted = _recipe.ReferenceImage.ManualAdjusted,
                GoldenDie = plan
            };

            RiGenerated = true;
            RiSummaryText = SummarizeGoldenDie(plan);
            StatusText = $"参考图像生成成功：{count} 个参考 Die（F-3.2.2-67）；GoldenDie 由 H03 唯一生成（B-09）";
            Persist();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentOutOfRangeException)
        {
            RiSummaryText = "生成失败：" + ex.Message;
            StatusText = "参考图像生成失败：" + ex.Message;
        }
    }

    /// <summary>BR-30：视野缩放修改后需重新生成参考图像。</summary>
    partial void OnRiFovZoomChanged(string value)
    {
        if (!RiGenerated || _recipe.ReferenceImage.GoldenDie is not { } plan) return;
        double zoom = ParseDouble(value, 1);
        if (Math.Abs(zoom - plan.FovZoom) < 1e-9) return;

        _recipe.ReferenceImage.GoldenDie = _planner.ChangeFovZoom(plan, zoom);
        RiSummaryText = SummarizeGoldenDie(_recipe.ReferenceImage.GoldenDie)
                      + " — 视野缩放已修改，请重新生成（BR-30 / F-3.2.2-62）";
    }

    /// <summary>F-3.2.2-64/66：FOV 图谱手动调整入口（不常用；不删除已规划点位）。</summary>
    [RelayCommand]
    private void RiManualAdjust()
    {
        if (_recipe.ReferenceImage.GoldenDie is not { } plan)
        {
            StatusText = "请先「创建参考图像」生成 GoldenDie，再进入 FOV 图谱手动调整（F-3.2.2-64）";
            return;
        }

        _recipe.ReferenceImage.GoldenDie = _planner.MarkManualAdjusted(plan);
        _recipe.ReferenceImage.ManualAdjusted = true;
        RiSummaryText = SummarizeGoldenDie(_recipe.ReferenceImage.GoldenDie) + " · 已进入 FOV 图谱手动调整（F-3.2.2-64/66）";
        StatusText = "已标记 FOV 图谱手动调整（F-3.2.2-64/66；图形化调整视图随 H05 Map 能力接入）";
        Persist();
    }

    private static string SummarizeGoldenDie(GoldenDiePlan plan)
    {
        if (plan == null) return "尚未生成参考图像（F-3.2.2-67：生成成功后才允许下一步）";
        return $"参考 Die {plan.ReferenceDieCount} 个 · 分类：黄 {plan.YellowCount} / 蓝 {plan.BlueCount} / " +
               $"红 {plan.RedCount}（可能没有）/ 绿 {plan.GreenCount}（F-3.2.2-60）· 耗时指数 {plan.EstimatedCostIndex}（BR-29）";
    }

    /* ---------------- 解析辅助（界面文本 ↔ 领域值） ---------------- */

    private string MapTypeNameOf(string mapTypeId)
        => _mapTypes.Find(mapTypeId)?.Name ?? (string.IsNullOrWhiteSpace(mapTypeId) ? string.Empty : mapTypeId);

    private string MapTypeIdOf(string displayName)
        => _mapTypes.Types.FirstOrDefault(t => t.Name == displayName)?.Id
           ?? (string.IsNullOrWhiteSpace(displayName) ? string.Empty : displayName);

    /// <summary>B-11：目录变更后同步刷新「图谱选择」下拉，不复制 MapType 定义。</summary>
    private void ReloadMapTypeNames()
    {
        string keep = BiMapType;
        MapTypeNames.Clear();
        foreach (var type in _mapTypes.Types) MapTypeNames.Add(type.Name);
        BiMapType = MapTypeNames.FirstOrDefault(n => n == keep) ?? MapTypeNames.FirstOrDefault() ?? string.Empty;
    }

    private static TraceImagePolicy? ParseTraceImage(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text == TraceImageInherit) return null;
        foreach (var value in Enum.GetValues<TraceImagePolicy>())
            if (value.Text() == text) return value;
        return null;   // 未勾选 = 继承全局（BR-15）
    }

    private static WaferPatternType ParsePatternType(string text)
        => Enum.GetValues<WaferPatternType>().FirstOrDefault(v => v.Text() == text, WaferPatternType.PatternWithMark);

    private static WaferSize ParseSize(string text)
        => Enum.GetValues<WaferSize>().FirstOrDefault(v => v.Text() == text, WaferSize.Inch12);

    private static MapFileType ParseMapFileType(string text)
        => Enum.GetValues<MapFileType>().FirstOrDefault(v => v.Text() == text, MapFileType.Csv);

    private static AxisDirection ParseDirection(string text)
        => string.Equals(text, AxisDirection.Negative.Text(), StringComparison.Ordinal)
            ? AxisDirection.Negative : AxisDirection.Positive;

    private static LevelingMode ParseLevelingMode(string text)
        => string.Equals(text, LevelingMode.FieldOfView.Text(), StringComparison.Ordinal)
            ? LevelingMode.FieldOfView : LevelingMode.Die;

    private static TemplateType ParseTemplateType(string text)
        => string.Equals(text, TemplateType.Shape.Text(), StringComparison.Ordinal)
            ? TemplateType.Shape : TemplateType.Gray;

    private static RecheckMode ParseRecheckMode(string text)
        => string.Equals(text, RecheckMode.OnlineVerify.Text(), StringComparison.Ordinal)
            ? RecheckMode.OnlineVerify : RecheckMode.OfflineReview;

    private static MarkAlignmentMethod ParseMarkMethod(string text)
        => text switch
        {
            var t when string.Equals(t, MarkAlignmentMethod.Vertical.Text(), StringComparison.Ordinal)
                => MarkAlignmentMethod.Vertical,
            var t when string.Equals(t, MarkAlignmentMethod.ThreePoint.Text(), StringComparison.Ordinal)
                => MarkAlignmentMethod.ThreePoint,
            var t when string.Equals(t, MarkAlignmentMethod.TwoPlusOne.Text(), StringComparison.Ordinal)
                => MarkAlignmentMethod.TwoPlusOne,
            _ => MarkAlignmentMethod.Horizontal
        };

    private static double ParseDouble(string text, double fallback)
        => double.TryParse((text ?? string.Empty).Trim(), out double v) ? v : fallback;

    private static int? ParseNullableInt(string text)
        => int.TryParse((text ?? string.Empty).Trim(), out int v) ? v : null;

    private void ClearValidation()
    {
        HasValidationError = false;
        ValidationSummary = string.Empty;
    }
}
