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
/// H02 配方与 Job 管理（P2）：Job 列表 CRUD + 公共参数表单（`F-3.2.1-01~29`）。
/// 规则口径：必填与 BR-15/16/17/78 全部由 <see cref="JobValidator"/> / <see cref="MapFileImporter"/> 判定，
/// ViewModel 只做「取表单值 → 组装 JobDefinition → 转发结果 → 呈现错误」，不复制校验逻辑。
/// 子 Recipe 分步向导（`F-3.2.2-*`）依赖 `T-005`/`T-042`，本轮不实现，入口保留在页头说明。
/// </summary>
public sealed partial class H02RecipeViewModel : ModuleViewModelBase
{
    private readonly IJobRepository _repository;
    private readonly IMapTypeCatalog _mapTypes;
    private readonly MapFileImporter _importer;
    private readonly IH02DialogService _dialogs;

    /// <summary>表单回填期间抑制「选中项变化 → 重填表单」的递归。</summary>
    private bool _loadingForm;

    public H02RecipeViewModel(
        IJobRepository repository,
        IMapTypeCatalog mapTypes,
        MapFileImporter importer,
        IH02DialogService dialogs)
        : base("H02", "配方与 Job 管理", "P2")
    {
        _repository = repository;
        _mapTypes = mapTypes;
        _importer = importer;
        _dialogs = dialogs;

        foreach (var type in _mapTypes.Types) MapTypeNames.Add(type.Name);

        _repository.Changed += (_, _) => ReloadJobs();
        ReloadJobs();
    }

    /* ---------------- 列表 ---------------- */
    public ObservableCollection<JobRow> Jobs { get; } = new();

    public ObservableCollection<string> MapTypeNames { get; } = new();

    /// <summary>枚举下拉选项：界面用中文文本，保存时映射回枚举，避免在 XAML 里硬编码枚举名。</summary>
    public IReadOnlyList<string> PatternTypeOptions { get; } = new[]
    {
        WaferPatternType.NoPattern.Text(),
        WaferPatternType.PatternWithoutMark.Text(),
        WaferPatternType.PatternWithMark.Text()
    };

    public IReadOnlyList<string> SizeOptions { get; } = new[]
    {
        WaferSize.Inch6.Text(), WaferSize.Inch8.Text(), WaferSize.Inch12.Text()
    };

    public IReadOnlyList<string> MapFileTypeOptions { get; } = new[]
    {
        MapFileType.Txt.Text(), MapFileType.Csv.Text(), MapFileType.Xml.Text(), MapFileType.Sinf.Text()
    };

    public IReadOnlyList<string> DirectionOptions { get; } = new[]
    {
        AxisDirection.Positive.Text(), AxisDirection.Negative.Text()
    };

    public IReadOnlyList<string> OcrPositionOptions { get; } = new[]
    {
        OcrPosition.Front.Text(), OcrPosition.Back.Text()
    };

    /// <summary>BR-15：首项表示「不覆盖」，其余为 Job 级覆盖值。</summary>
    public IReadOnlyList<string> TraceImageOptions { get; } = new[]
    {
        TraceImageOptionsInherit,
        TraceImagePolicy.Upload.Text(),
        TraceImagePolicy.DoNotUpload.Text(),
        TraceImagePolicy.UploadDefectOnly.Text()
    };

    private const string TraceImageOptionsInherit = "继承系统设置（不覆盖）";

    [ObservableProperty] private JobRow _selectedJob;

    [ObservableProperty] private string _listSummary = string.Empty;

    [ObservableProperty] private string _statusText = string.Empty;

    /* ---------------- 表单字段（F-3.2.1-06~28） ---------------- */
    [ObservableProperty] private string _formName = string.Empty;
    [ObservableProperty] private string _formPatternType = WaferPatternType.PatternWithMark.Text();
    [ObservableProperty] private string _formSize = WaferSize.Inch12.Text();
    [ObservableProperty] private string _formMaterial = "Si";
    [ObservableProperty] private string _formThickness = "0";
    [ObservableProperty] private string _formDieX = string.Empty;
    [ObservableProperty] private string _formDieY = string.Empty;
    [ObservableProperty] private string _formNotch = "270";
    [ObservableProperty] private string _formMapType = string.Empty;
    [ObservableProperty] private string _formMapFileType = MapFileType.Csv.Text();
    [ObservableProperty] private string _formMapPath = string.Empty;
    [ObservableProperty] private string _formColumnDirection = AxisDirection.Positive.Text();
    [ObservableProperty] private string _formRowDirection = AxisDirection.Positive.Text();
    [ObservableProperty] private string _formBarcode = string.Empty;
    [ObservableProperty] private string _formOcrJob = string.Empty;
    [ObservableProperty] private string _formOcrAngle = "0";
    [ObservableProperty] private string _formOcrPosition = OcrPosition.Front.Text();

    /* 推荐忽略项（F-3.2.1-22~27，级 C 保留；语义待 T-004） */
    [ObservableProperty] private string _formLocateDieX = string.Empty;
    [ObservableProperty] private string _formLocateDieY = string.Empty;
    [ObservableProperty] private string _formOrientation = string.Empty;
    [ObservableProperty] private string _formColumnOffset = "0";
    [ObservableProperty] private string _formRowOffset = "0";
    [ObservableProperty] private string _formColRow = string.Empty;
    [ObservableProperty] private string _formXy = string.Empty;
    [ObservableProperty] private string _formIncludeExclude = string.Empty;

    /// <summary>F-3.2.1-28 回溯图存储（BR-15）。</summary>
    [ObservableProperty] private string _formTraceImage = TraceImageOptionsInherit;

    [ObservableProperty] private bool _isNew;

    [ObservableProperty] private bool _hasValidationError;

    [ObservableProperty] private string _validationSummary = string.Empty;

    /// <summary>当前编辑对象的 JobId；新建时为空。</summary>
    private string _editingJobId = string.Empty;

    /* ---------------- 列表编排 ---------------- */

    private void ReloadJobs()
    {
        string keepId = SelectedJob?.JobId ?? _editingJobId;
        _loadingForm = true;
        Jobs.Clear();
        foreach (var job in _repository.Jobs)
            Jobs.Add(new JobRow(job, MapTypeNameOf(job.MapTypeId)));
        ListSummary = $"共 {Jobs.Count} 个 Job · 名称唯一 · 校验口径见 F-3.2.1";

        var reselect = Jobs.FirstOrDefault(j => j.JobId == keepId) ?? Jobs.FirstOrDefault();
        SelectedJob = reselect;
        _loadingForm = false;

        if (reselect != null) LoadForm(reselect); else BeginNew(draftDefaults: true);
    }

    partial void OnSelectedJobChanged(JobRow value)
    {
        if (_loadingForm) return;

        DuplicateJobCommand.NotifyCanExecuteChanged();
        RenameJobCommand.NotifyCanExecuteChanged();
        DeleteJobCommand.NotifyCanExecuteChanged();

        if (value != null) LoadForm(value);
    }

    private string MapTypeNameOf(string mapTypeId)
        => _mapTypes.Find(mapTypeId)?.Name ?? mapTypeId;

    private void LoadForm(JobRow row)
    {
        var job = row.Source;
        _editingJobId = job.JobId;
        IsNew = false;

        FormName = job.Name;
        FormPatternType = job.PatternType.Text();
        FormSize = job.Size.Text();
        FormMaterial = job.Material;
        FormThickness = job.ThicknessMm.ToString("0.###");
        FormDieX = job.DieXmm.ToString("0.###");
        FormDieY = job.DieYmm.ToString("0.###");
        FormNotch = job.NotchAngle.ToString("0.###");
        FormMapType = MapTypeNameOf(job.MapTypeId);
        FormMapFileType = job.MapFileType.Text();
        FormMapPath = job.MapFilePath;
        FormColumnDirection = job.ColumnDirection.Text();
        FormRowDirection = job.RowDirection.Text();
        FormBarcode = job.BarcodeCode;
        FormOcrJob = job.OcrJob;
        FormOcrAngle = job.OcrAngle.ToString("0.###");
        FormOcrPosition = job.OcrPosition.Text();

        FormLocateDieX = job.LocateDieX?.ToString() ?? string.Empty;
        FormLocateDieY = job.LocateDieY?.ToString() ?? string.Empty;
        FormOrientation = job.Orientation;
        FormColumnOffset = job.ColumnOffset.ToString();
        FormRowOffset = job.RowOffset.ToString();
        FormColRow = job.ColRow;
        FormXy = job.Xy;
        FormIncludeExclude = job.IncludeExclude;
        FormTraceImage = job.TraceImage is TraceImagePolicy policy
            ? policy.Text()
            : TraceImageOptionsInherit;

        ClearValidation();
        StatusText = $"已加载 Job：{job.Name}（{job.JobId}）";
    }

    private void BeginNew(bool draftDefaults)
    {
        _editingJobId = string.Empty;
        IsNew = true;
        FormName = draftDefaults ? MakeDraftName() : string.Empty;
        FormPatternType = WaferPatternType.PatternWithMark.Text();   // BR-16 默认
        FormSize = WaferSize.Inch12.Text();
        FormMaterial = "Si";                                        // F-3.2.1-09 固定值
        FormThickness = "0";                                        // F-3.2.1-10 未启用
        FormDieX = string.Empty;
        FormDieY = string.Empty;
        FormNotch = "270";                                          // BR-17 默认
        FormMapType = MapTypeNames.FirstOrDefault() ?? string.Empty;
        FormMapFileType = MapFileType.Csv.Text();
        FormMapPath = string.Empty;
        FormColumnDirection = AxisDirection.Positive.Text();
        FormRowDirection = AxisDirection.Positive.Text();
        FormBarcode = string.Empty;
        FormOcrJob = string.Empty;
        FormOcrAngle = "0";
        FormOcrPosition = OcrPosition.Front.Text();
        FormLocateDieX = string.Empty;
        FormLocateDieY = string.Empty;
        FormOrientation = string.Empty;
        FormColumnOffset = "0";
        FormRowOffset = "0";
        FormColRow = string.Empty;
        FormXy = string.Empty;
        FormIncludeExclude = string.Empty;
        FormTraceImage = TraceImageOptionsInherit;
        ClearValidation();
        StatusText = "新建 Job：填写公共参数后保存（带 * 为必填）";
    }

    private string MakeDraftName()
    {
        for (int i = 1; i < 1000; i++)
        {
            string candidate = $"新 Job {i}";
            if (!Jobs.Any(j => string.Equals(j.Name, candidate, StringComparison.OrdinalIgnoreCase))) return candidate;
        }
        return "新 Job";
    }

    /* ---------------- 命令：CRUD（F-3.2.1-01~05） ---------------- */

    [RelayCommand]
    private void NewJob() => BeginNew(draftDefaults: true);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void DuplicateJob()
    {
        try
        {
            var copy = _repository.Duplicate(SelectedJob.JobId);
            StatusText = $"已复制 Job：{SelectedJob.Name} → {copy.Name}（F-3.2.1-02）";
            SelectById(copy.JobId);
        }
        catch (InvalidOperationException ex)
        {
            _dialogs.ShowMessage("复制失败", ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void RenameJob()
    {
        string name = _dialogs.PromptJobName("重命名 Job（F-3.2.1-03）", SelectedJob.Name);
        if (string.IsNullOrWhiteSpace(name)) return;

        try
        {
            var renamed = _repository.Rename(SelectedJob.JobId, name);
            StatusText = $"已重命名 Job：{renamed.Name}";
            SelectById(renamed.JobId);
        }
        catch (JobValidationException ex)
        {
            ShowIssues("重命名失败", ex);
        }
        catch (InvalidOperationException ex)
        {
            _dialogs.ShowMessage("重命名失败", ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void DeleteJob()
    {
        var target = SelectedJob;
        if (!_dialogs.Confirm("删除 Job（F-3.2.1-04）",
                $"确认删除 Job「{target.Name}」及其全部配置？\n删除后不可恢复。")) return;

        try
        {
            _repository.Delete(target.JobId);
            StatusText = $"已删除 Job：{target.Name}";
            _editingJobId = string.Empty;
            ReloadJobs();
        }
        catch (InvalidOperationException ex)
        {
            _dialogs.ShowMessage("删除失败", ex.Message);
        }
    }

    [RelayCommand]
    private void RefreshJobs()
    {
        _repository.Refresh();   // F-3.2.1-05
        StatusText = $"已刷新 Job 列表（{Jobs.Count} 项）";
    }

    private bool HasSelection => SelectedJob != null;

    /* ---------------- 命令：Map 文件（F-3.2.1-11/17/18） ---------------- */

    [RelayCommand]
    private void BrowseMapFile()
    {
        string path = _dialogs.PickMapFile(FormMapPath);
        if (string.IsNullOrWhiteSpace(path)) return;

        FormMapPath = path;
        FormMapFileType = DeclaredTypeOf(path).Text();
        TryImportMap(silentWhenFailed: true);
    }

    /// <summary>F-3.2.1-11：导入成功自动回填 Die X/Y 与条码分类代码；失败保留手动输入路径。</summary>
    [RelayCommand]
    private void ImportMapFile() => TryImportMap(silentWhenFailed: false);

    private void TryImportMap(bool silentWhenFailed)
    {
        var declared = ParseMapFileType(FormMapFileType);
        var result = _importer.Import(FormMapPath, declared);

        if (!result.Success)
        {
            if (!silentWhenFailed) _dialogs.ShowMessage("Map 文件导入失败", result.Error);
            StatusText = $"Map 导入未完成：{result.Error}";
            HasValidationError = !silentWhenFailed;
            ValidationSummary = silentWhenFailed ? string.Empty : $"· Map 文件：{result.Error}";
            return;
        }

        FormDieX = result.DieXmm.ToString("0.###");
        FormDieY = result.DieYmm.ToString("0.###");
        if (string.IsNullOrWhiteSpace(FormBarcode) && result.BarcodeCodes.Count > 0)
            FormBarcode = result.BarcodeCodes[0];

        ClearValidation();
        StatusText = $"Map 导入成功：{result.Columns}×{result.Rows} · Die {result.DieXmm:0.###}×{result.DieYmm:0.###} mm · 分类代码 {string.Join('/', result.BarcodeCodes)}";
    }

    private static MapFileType DeclaredTypeOf(string path)
    {
        string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".txt" => MapFileType.Txt,
            ".csv" => MapFileType.Csv,
            ".xml" => MapFileType.Xml,
            ".sinf" => MapFileType.Sinf,
            _ => MapFileType.Csv
        };
    }

    /* ---------------- 命令：保存（F-3.2.1-29） ---------------- */

    [RelayCommand]
    private void SaveJob()
    {
        var draft = BuildDraft();
        try
        {
            if (IsNew)
            {
                var created = _repository.Create(draft);
                StatusText = $"已创建 Job：{created.Name}（{created.JobId}）";
                IsNew = false;
                _editingJobId = created.JobId;
                SelectById(created.JobId);
            }
            else
            {
                var updated = _repository.Update(draft);
                StatusText = $"已保存 Job：{updated.Name}（{updated.JobId}）";
                SelectById(updated.JobId);
            }
            ClearValidation();
        }
        catch (JobValidationException ex)
        {
            ShowIssues("保存失败", ex);
        }
        catch (InvalidOperationException ex)
        {
            _dialogs.ShowMessage("保存失败", ex.Message);
        }
    }

    private JobDefinition BuildDraft() => new()
    {
        JobId = _editingJobId,
        Name = FormName?.Trim() ?? string.Empty,
        PatternType = ParsePatternType(FormPatternType),
        Size = ParseSize(FormSize),
        Material = string.IsNullOrWhiteSpace(FormMaterial) ? "Si" : FormMaterial,
        ThicknessMm = ParseNumber(FormThickness),
        DieXmm = ParseNumber(FormDieX),
        DieYmm = ParseNumber(FormDieY),
        NotchAngle = ParseNumber(FormNotch),
        MapTypeId = MapTypeIdOf(FormMapType),
        MapFileType = ParseMapFileType(FormMapFileType),
        MapFilePath = FormMapPath?.Trim() ?? string.Empty,
        ColumnDirection = ParseDirection(FormColumnDirection),
        RowDirection = ParseDirection(FormRowDirection),
        BarcodeCode = FormBarcode?.Trim() ?? string.Empty,
        OcrJob = FormOcrJob?.Trim() ?? string.Empty,
        OcrAngle = ParseNumber(FormOcrAngle),
        OcrPosition = string.Equals(FormOcrPosition, OcrPosition.Back.Text(), StringComparison.Ordinal)
            ? OcrPosition.Back : OcrPosition.Front,
        LocateDieX = ParseNullableInt(FormLocateDieX),
        LocateDieY = ParseNullableInt(FormLocateDieY),
        Orientation = FormOrientation?.Trim() ?? string.Empty,
        ColumnOffset = (int)ParseNumber(FormColumnOffset),
        RowOffset = (int)ParseNumber(FormRowOffset),
        ColRow = FormColRow?.Trim() ?? string.Empty,
        Xy = FormXy?.Trim() ?? string.Empty,
        IncludeExclude = FormIncludeExclude?.Trim() ?? string.Empty,
        TraceImage = ParseTraceImage(FormTraceImage)
    };

    /* ---------------- 枚举 / 数值解析（界面文本 ↔ 领域枚举） ---------------- */

    private static WaferPatternType ParsePatternType(string text)
        => Enum.GetValues<WaferPatternType>().FirstOrDefault(v => v.Text() == text, WaferPatternType.PatternWithMark);

    private static WaferSize ParseSize(string text)
        => Enum.GetValues<WaferSize>().FirstOrDefault(v => v.Text() == text, WaferSize.Inch12);

    private static MapFileType ParseMapFileType(string text)
        => Enum.GetValues<MapFileType>().FirstOrDefault(v => v.Text() == text, MapFileType.Csv);

    private static AxisDirection ParseDirection(string text)
        => string.Equals(text, AxisDirection.Negative.Text(), StringComparison.Ordinal)
            ? AxisDirection.Negative : AxisDirection.Positive;

    private static TraceImagePolicy? ParseTraceImage(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text == TraceImageOptionsInherit) return null;
        foreach (var value in Enum.GetValues<TraceImagePolicy>())
            if (value.Text() == text) return value;
        return null;
    }

    private string MapTypeIdOf(string displayName)
        => _mapTypes.Types.FirstOrDefault(t => t.Name == displayName)?.Id ?? displayName;

    private static double ParseNumber(string text)
        => double.TryParse((text ?? string.Empty).Trim(), out double value) ? value : 0;

    private static int? ParseNullableInt(string text)
        => int.TryParse((text ?? string.Empty).Trim(), out int value) ? value : null;

    private void SelectById(string jobId)
    {
        var row = Jobs.FirstOrDefault(j => j.JobId == jobId);
        _loadingForm = true;
        SelectedJob = row;
        _loadingForm = false;
        if (row != null) LoadForm(row);
    }

    private void ShowIssues(string title, JobValidationException ex)
    {
        HasValidationError = true;
        ValidationSummary = string.Join(Environment.NewLine,
            ex.Issues.Select(i => $"· {i.Field}：{i.Message}"));
        _dialogs.ShowMessage(title, ValidationSummary);
    }

    private void ClearValidation()
    {
        HasValidationError = false;
        ValidationSummary = string.Empty;
    }
}
