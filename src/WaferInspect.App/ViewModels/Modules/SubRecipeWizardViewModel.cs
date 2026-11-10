using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WaferInspect.Core.Maps;
using WaferInspect.Core.Recipes;

namespace WaferInspectHMI.ViewModels.Modules;

/// <summary>
/// 子配方分步向导 ViewModel（R-H02-05/06，B-25 由 H02 统一编排）：
/// · 步骤顺序固定为 T-042 确认的 7 步，禁止跳步（第一步未成功不得进入下一步）；
/// · BR-19：已完成步骤随时可点击重新进入执行；
/// · 步骤 1 基础信息为 T-005 确认的全字段表单（5 分区 33 字段）；
/// · 步骤 2~6 的图形化交互（框选拉平、全景图 Mark、ROI 圈选）由 H03/H04/H06 提供
///   （下一增量，用户已确认按 H03 → H04 → H05 顺序），当前步骤允许以数据编辑方式完成，
///   并在界面明确标注能力接入状态，不伪造设备执行成功；
/// · 步骤 7 复判设置（R-H02-07，BR-20）写入子配方，运行时供 H08 读取。
/// </summary>
public sealed partial class SubRecipeWizardViewModel : ObservableObject
{
    private readonly ISubRecipeRepository _subs;
    private readonly IJobRepository _jobs;
    private readonly IMapTypeCatalog _mapTypes;
    private readonly IH02DialogService _dialogs;
    private readonly string _parentJobId;

    /// <summary>向导正在编辑的子配方工作副本；完成一步即经仓库持久化。</summary>
    private SubRecipe _recipe = new();
    private SubRecipeWizard _wizard;

    /// <summary>表单回填期间抑制「选中项变化 → 重填表单」的递归。</summary>
    private bool _loading;

    public SubRecipeWizardViewModel(
        string parentJobId,
        IJobRepository jobs,
        ISubRecipeRepository subs,
        IMapTypeCatalog mapTypes,
        IH02DialogService dialogs)
    {
        _parentJobId = parentJobId;
        _jobs = jobs;
        _subs = subs;
        _mapTypes = mapTypes;
        _dialogs = dialogs;

        var parent = _jobs.Find(parentJobId);
        HeaderText = $"子配方向导 · Job：{parent?.Name ?? parentJobId}（7 步，T-042 顺序）";

        foreach (var type in mapTypes.Types) MapTypeNames.Add(type.Name);

        _subs.Changed += (_, _) => ReloadSubs();
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

    /* ---------------- 步骤 2：拉平（BR-22/23/24） ---------------- */

    [ObservableProperty] private string _lvMode = LevelingMode.Die.Text();
    [ObservableProperty] private string _lvTemplateType = TemplateType.Gray.Text();
    [ObservableProperty] private string _lvScore = "70";

    /* ---------------- 步骤 3：Mark（BR-25/26） ---------------- */

    public ObservableCollection<MarkRow> MarkRows { get; } = new();

    /* ---------------- 步骤 4：参考图像 ---------------- */

    [ObservableProperty] private string _riImagePath = string.Empty;

    /* ---------------- 步骤 5：ROI（AC-P2-03） ---------------- */

    public ObservableCollection<RoiZoneRow> RoiRows { get; } = new();

    /* ---------------- 步骤 6：扫描区域（AC-P2-03） ---------------- */

    [ObservableProperty] private bool _scanFullWafer = true;

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

        // 步骤 2 拉平。
        LvMode = _recipe.Leveling.Mode.Text();
        LvTemplateType = _recipe.Leveling.TemplateType.Text();
        LvScore = _recipe.Leveling.MatchScore.ToString("0.###");

        // 步骤 3 Mark（3 行固定）。
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

        // 步骤 4 参考图像。
        RiImagePath = _recipe.ReferenceImage.ImagePath;

        // 步骤 5 ROI。
        RoiRows.Clear();
        foreach (var zone in _recipe.RoiZones)
            RoiRows.Add(new RoiZoneRow(zone.Index)
            {
                Name = zone.Name, X = zone.X.ToString("0.###"), Y = zone.Y.ToString("0.###"),
                Width = zone.Width.ToString("0.###"), Height = zone.Height.ToString("0.###")
            });

        // 步骤 6 扫描区域。
        ScanFullWafer = _recipe.ScanArea.FullWafer;

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
                "拉平模式（BR-22）、模板类型（BR-23）与匹配分数（BR-24）为全系统共用配置（B-08）。框选区域后自动执行的图形化交互由 H03 提供（下一增量），当前以配置数据完成本步。",
            WizardStepKey.Mark =>
                "三个 Mark 点（BR-25：至少 2 个同行或同列）。全景图加载、Map 网格拖动与双击定位由 H03/H05 提供（下一增量），当前以行列坐标数据完成本步。",
            WizardStepKey.ReferenceImage =>
                "参考图像路径（图像采集由 H06 提供，设备连接搁置 T-029，当前支持选择本地文件）。",
            WizardStepKey.Roi =>
                "ROI 区域定义（AC-P2-03：ROI 与扫描区域共用 H04 单一数据源）。图形化圈选由 H04 提供（下一增量），当前以区域数据完成本步。",
            WizardStepKey.ScanArea =>
                "扫描区域（AC-P2-03）：勾选「全片扫描」或引用已定义的 ROI 区域。",
            WizardStepKey.Recheck =>
                "复判设置（F-3.2.2-80~82，BR-20：默认离线复判 + 复查相机）。保存后双击 Map 定位 Die 的校验动作由 H03 图形能力支持（下一增量）；本配置写入子配方，运行时由 H08 读取。",
            _ => string.Empty
        };
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
            _wizard.FailCurrent(reason);
            HasValidationError = true;
            ValidationSummary = string.Join(Environment.NewLine, issues.Select(i => $"· {i.Field}：{i.Message}"));
            Persist();
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
        _recipe.Leveling = new LevelingConfig
        {
            Mode = ParseLevelingMode(LvMode),
            TemplateType = ParseTemplateType(LvTemplateType),
            MatchScore = ParseDouble(LvScore, 70)
        };
        return SubRecipeValidator.ValidateLeveling(_recipe.Leveling).ToList();
    }

    private List<SubRecipeValidationIssue> EvaluateMarks()
    {
        var marks = new List<MarkPoint>();
        foreach (var row in MarkRows)
        {
            if (!int.TryParse(row.Row?.Trim(), out int r) || !int.TryParse(row.Col?.Trim(), out int c))
                return [new("Marks", $"Mark {row.Index + 1} 行/列必须为整数（双击 Die 网格自动填入，H03 接入后支持）")];

            marks.Add(new MarkPoint(row.Index, r, c,
                ParseDouble(row.PlatformX, 0), ParseDouble(row.PlatformY, 0)));
        }
        _recipe.Marks = marks;
        return SubRecipeValidator.ValidateMarks(marks).ToList();
    }

    private List<SubRecipeValidationIssue> EvaluateReferenceImage()
    {
        string path = RiImagePath?.Trim() ?? string.Empty;
        if (path.Length == 0)
            return [new("ImagePath", "参考图像路径必填（F-3.2.2 参考图像步骤；图像采集属 H06，当前可选择本地文件）")];

        _recipe.ReferenceImage = new ReferenceImageConfig { ImagePath = path };
        return [];
    }

    private List<SubRecipeValidationIssue> EvaluateRoi()
    {
        var zones = new List<RoiZone>();
        foreach (var row in RoiRows)
        {
            zones.Add(new RoiZone(row.Index,
                row.Name?.Trim() ?? string.Empty,
                ParseDouble(row.X, 0), ParseDouble(row.Y, 0),
                ParseDouble(row.Width, 0), ParseDouble(row.Height, 0)));
        }
        _recipe.RoiZones = zones;
        return SubRecipeValidator.ValidateRoiZones(zones).ToList();
    }

    private List<SubRecipeValidationIssue> EvaluateScanArea()
    {
        _recipe.ScanArea = new ScanAreaConfig
        {
            FullWafer = ScanFullWafer,
            RoiIndexes = ScanFullWafer ? [] : _recipe.RoiZones.Select(z => z.Index).ToList()
        };

        if (!ScanFullWafer && _recipe.RoiZones.Count == 0)
            return [new("ScanArea", "未勾选全片扫描时，必须先在「ROI」步骤定义至少 1 个区域（AC-P2-03）")];

        return [];
    }

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
        try
        {
            _recipe = _subs.Update(_recipe);
            _wizard = new SubRecipeWizard(_recipe);
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

    [RelayCommand]
    private void AddRoiZone()
    {
        RoiRows.Add(new RoiZoneRow(RoiRows.Count) { Name = $"ROI {RoiRows.Count + 1}", Width = "1", Height = "1" });
        StatusText = $"已添加 ROI 区域（共 {RoiRows.Count} 个，AC-P2-03）";
    }

    [RelayCommand]
    private void RemoveRoiZone(RoiZoneRow row)
    {
        if (row == null) return;
        RoiRows.Remove(row);
        StatusText = $"已移除 ROI 区域（剩 {RoiRows.Count} 个）";
    }

    /* ---------------- 解析辅助（界面文本 ↔ 领域值） ---------------- */

    private string MapTypeNameOf(string mapTypeId)
        => _mapTypes.Find(mapTypeId)?.Name ?? (string.IsNullOrWhiteSpace(mapTypeId) ? string.Empty : mapTypeId);

    private string MapTypeIdOf(string displayName)
        => _mapTypes.Types.FirstOrDefault(t => t.Name == displayName)?.Id
           ?? (string.IsNullOrWhiteSpace(displayName) ? string.Empty : displayName);

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
