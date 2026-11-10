using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WaferInspect.Core.Data;
using WaferInspect.Core.Events;
using WaferInspect.Core.Maps;
using WaferInspect.Core.Run;
using WaferInspect.Core.Security;
using WaferInspectHMI.Services;

namespace WaferInspectHMI.ViewModels.Modules;

/// <summary>视图区单元（内容由 H05 <see cref="IMapRenderer"/> 提供，本类只做画刷映射）。</summary>
public sealed record MapCell(double X, double Y, Brush Fill, string Tip);

/// <summary>
/// H01 检测运行与任务控制（P1 主干）。承载 PRD 4.1 的 9 栏全部内容：
/// 料盒信息 / 扫描状态 / 视图区 / 快捷操作 / 晶圆信息 / 批次信息 / 缺陷列表 / 日志信息
/// （导航菜单栏由主窗口外壳提供，`F-3.1.1-03`）。
/// 状态机唯一实现在 <see cref="WaferRunController"/>（B-26），报警等级判定唯一在 <see cref="IAlarmCenter"/>。
/// 设备相关动作（EFEM 上下料、Chuck 移动、安全门、OCR、WDI、Clear、翻面、蜂鸣器/四色灯）
/// 一律**不下发、不伪造成功**，只给出「已搁置 T-029」的明确提示，见 <c>docs/P1-Migration-Status.md</c>。
/// </summary>
public sealed partial class H01RunViewModel : ModuleViewModelBase, IDisposable
{
    private static readonly BrushConverter Brushes = new();

    private readonly WaferRunController _run;

    /// <summary>F-3.1.2-27 翻面次数（T-002 已确认：正/反面互换后重新扫描该面）。</summary>
    public int FlipCount { get; private set; }
    private readonly IAlarmCenter _alarms;
    private readonly IJobCatalog _jobs;
    private readonly IMapRenderer _map;
    private readonly ManualLoadSequencer _manual;
    private readonly ILiveDataPump _pump;
    private readonly IEventSource _events;
    private readonly IAuthorizationService _authorization;
    private readonly IH01DialogService _dialogs;
    private readonly ILogger<H01RunViewModel> _logger;

    private readonly List<JobSelection> _selected = [];
    private bool _disposed;
    private int _lastDefectCount;
    private string _lastWaferId;

    public H01RunViewModel(
        WaferRunController run,
        IAlarmCenter alarms,
        IJobCatalog jobs,
        IMapRenderer map,
        ManualLoadSequencer manual,
        ILiveDataPump pump,
        IEventSource events,
        IAuthorizationService authorization,
        IH01DialogService dialogs,
        ILogger<H01RunViewModel> logger)
        : base("H01", "检测运行与任务控制", "P1")
    {
        _run = run;
        _alarms = alarms;
        _jobs = jobs;
        _map = map;
        _manual = manual;
        _pump = pump;
        _events = events;
        _authorization = authorization;
        _dialogs = dialogs;
        _logger = logger;

        Ports =
        [
            new PortRow("Port1", SlotCount),
            new PortRow("Port2", SlotCount)
        ];
        Ports[0].IsCurrent = true;
        CurrentPort = Ports[0];

        // 演示节拍：1 Hz 下每拍推进 30 个 Die，使 3600 Die 的配方在可接受时间内走完。
        // 真实设备接入后由采集速率决定（T-029 搁置），核心默认值是 1。
        _run.DiesPerTick = 30;
        _run.SnapshotChanged += OnSnapshotChanged;
        _events.EventPublished += OnEventPublished;
        _alarms.AlarmRaised += OnAlarmRaised;
        _pump.Tick += OnTick;

        BuildMap();
        RefreshInterlock();
        RefreshCommands();
        AppendLog(new SystemEvent("SYS-BOOT", DateTimeOffset.Now, "H01", "Run", "Info",
            "检测任务页就绪：未选择 Job；设备连接相关动作已搁置（T-029）", new Dictionary<string, string>()));
    }

    /// <summary>料盒槽位数（`F-3.1.2` 双列 25 层；编号规则见【待确认 T-021】）。</summary>
    public const int SlotCount = 25;

    /// <summary>请求主窗口导航到其它模块（配方管理 `F-3.1.5-10`）。</summary>
    public event EventHandler<string> NavigationRequested;

    public ObservableCollection<PortRow> Ports { get; }
    public ObservableCollection<DefectRow> Defects { get; } = [];
    public ObservableCollection<LogRow> Logs { get; } = [];
    public ObservableCollection<MapCell> MapCells { get; } = [];
    public ObservableCollection<JobSelection> SelectedJobs { get; } = [];

    /* ---------------- 扫描状态栏（F-3.1.1-02） ---------------- */

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PhaseText))]
    [NotifyPropertyChangedFor(nameof(IsRunning))]
    [NotifyPropertyChangedFor(nameof(StartButtonText))]
    private RunPhase _phase = RunPhase.Idle;

    public bool IsRunning => Phase is RunPhase.Running or RunPhase.BatchEnding;

    public string PhaseText => Phase switch
    {
        RunPhase.Idle => "待机 · 未选择 Job",
        RunPhase.Ready => "就绪 · 已应用 Job，可开始运行",
        RunPhase.Running => "运行中 · 正在扫描",
        RunPhase.Paused => "已暂停 · 扫描中止（可调参）",
        RunPhase.BatchEnding => "结批中 · 扫完当前批次后结束",
        _ => "已结束"
    };

    /// <summary>暂停后「开始运行」按钮变为「继续运行」（F-3.1.5-03，不是新增按钮）。</summary>
    public string StartButtonText => Phase == RunPhase.Paused ? "继续运行" : "开始运行";

    [ObservableProperty] private string _scanStatusText = "系统扫描状态：待机";
    [ObservableProperty] private string _interlockText = string.Empty;
    [ObservableProperty] private bool _hasInterlock;

    /* ---------------- 料盒信息（F-3.1.2） ---------------- */

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentPortId))]
    private PortRow _currentPort;

    public string CurrentPortId => CurrentPort.PortId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModeText))]
    [NotifyPropertyChangedFor(nameof(IsManual))]
    [NotifyPropertyChangedFor(nameof(IsAuto))]
    [NotifyPropertyChangedFor(nameof(IsSemiAuto))]
    private LoadMode _mode = LoadMode.Manual;

    public bool IsManual => Mode == LoadMode.Manual;
    public bool IsAuto => Mode == LoadMode.Auto;
    public bool IsSemiAuto => Mode == LoadMode.SemiAuto;

    public string ModeText => Mode switch
    {
        LoadMode.Manual => "手动",
        LoadMode.Auto => "自动",
        _ => "半自动"
    };

    /// <summary>F-3.1.2-08/09/10：三种模式的流程说明（同屏可见，不折叠）。</summary>
    public string ModeHint => Mode switch
    {
        LoadMode.Manual => "手动：上料位 → 开安全门 → 放片 → 关安全门 → 拍照位（BR-05 顺序固定）",
        LoadMode.Auto => "自动：EFEM 全程控制上下料，无需人工干预（设备动作搁置 T-029）",
        _ => "半自动：自动识别料盒晶圆后，由操作员手动勾选待检晶圆并确认"
    };

    [ObservableProperty] private string _manualPrompt = "手动上料未开始";

    [ObservableProperty] private string _checkedText = "已勾选 0 片";

    /* ---------------- 扫描进度（F-3.1.3-01~07） ---------------- */

    [ObservableProperty] private double _progressPercent;
    [ObservableProperty] private string _progressText = "0.0%";
    [ObservableProperty] private string _elapsedText = "00:00:00";
    [ObservableProperty] private int _scannedDies;
    [ObservableProperty] private int _totalDefectCount;
    [ObservableProperty] private int _defectFreeDies;
    [ObservableProperty] private int _defectiveDies;
    [ObservableProperty] private int _reviewCount;

    /* ---------------- 晶圆信息（F-3.1.6-01~04） ---------------- */

    [ObservableProperty] private string _lotId = "—";
    [ObservableProperty] private string _waferId = "—";
    [ObservableProperty] private string _dieSizeXText = "—";
    [ObservableProperty] private string _dieSizeYText = "—";

    /* ---------------- 批次信息（F-3.1.7-01~03，BR-11 固定三类） ---------------- */

    [ObservableProperty] private int _normalDies;
    [ObservableProperty] private string _normalPct = "0.0%";
    [ObservableProperty] private double _normalPctValue;
    [ObservableProperty] private int _defectDies;
    [ObservableProperty] private string _defectPct = "0.0%";
    [ObservableProperty] private double _defectPctValue;
    [ObservableProperty] private int _failedDies;
    [ObservableProperty] private string _failedPct = "0.0%";
    [ObservableProperty] private double _failedPctValue;
    [ObservableProperty] private string _batchTotalText = "0";

    /* ---------------- 快捷操作栏（F-3.1.5-01~12） ---------------- */

    [ObservableProperty] private string _jobNameText = "未选择";
    [ObservableProperty] private string _waferSizeText = "—";
    [ObservableProperty] private string _jobDieSizeXText = "—";
    [ObservableProperty] private string _jobDieSizeYText = "—";
    [ObservableProperty] private string _queueText = "队列为空";
    [ObservableProperty] private string _recipeNameText = "—";

    /* ---------------- 视图区（F-3.1.4-01~04） ---------------- */

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ViewModeText))]
    private bool _isImageView;

    public string ViewModeText => IsImageView ? "图片视窗（双击切回图谱）" : "图谱视窗（双击切换）";

    [ObservableProperty] private int _mapNormal;
    [ObservableProperty] private int _mapLight;
    [ObservableProperty] private int _mapWarn;
    [ObservableProperty] private int _mapBad;
    [ObservableProperty] private string _mapYieldText = "—";

    /* ---------------- 1 Hz 节拍 ---------------- */

    private void OnTick(object sender, EventArgs e)
    {
        if (!IsRunning) return;
        try
        {
            _run.Advance(TimeSpan.FromSeconds(1));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "运行推进失败");
        }
    }

    private void OnSnapshotChanged(object sender, RunSnapshot snapshot)
    {
        if (!Application.Current.Dispatcher.CheckAccess())
        {
            Application.Current.Dispatcher.Invoke(() => Apply(snapshot));
            return;
        }
        Apply(snapshot);
    }

    private void Apply(RunSnapshot snapshot)
    {
        Phase = snapshot.Phase;
        ProgressPercent = snapshot.Progress.Percent;
        ProgressText = $"{snapshot.Progress.Percent:F1}%";
        ElapsedText = snapshot.Progress.Elapsed.ToString(@"hh\:mm\:ss");
        ScannedDies = snapshot.Progress.ScannedDies;
        TotalDefectCount = snapshot.Progress.TotalDefects;
        DefectFreeDies = snapshot.Progress.DefectFreeDies;
        DefectiveDies = snapshot.Progress.DefectiveDies;
        ReviewCount = snapshot.Progress.ReviewCount;

        LotId = snapshot.CurrentLotId ?? "—";
        WaferId = snapshot.CurrentWaferId ?? "—";
        RecipeNameText = snapshot.CurrentRecipe ?? "—";
        QueueText = snapshot.RemainingRecipes > 0
            ? $"队列剩余 {snapshot.RemainingRecipes} 个 Job"
            : "队列已到最后一个 Job";

        ApplyStats(snapshot.Stats);
        ApplyDefects(snapshot.Defects);
        UpdateSlotStates(snapshot.CurrentWaferId);
        ScanStatusText = $"系统扫描状态：{PhaseText} · 通道 PL · 1 Hz";
        RefreshInterlock();
        RefreshCommands();
    }

    private void ApplyStats(BatchStats stats)
    {
        NormalDies = stats.NormalDies;
        DefectDies = stats.DefectDies;
        FailedDies = stats.FailedDies;
        BatchTotalText = stats.TotalDies.ToString("N0");
        double total = stats.TotalDies == 0 ? 1 : stats.TotalDies;
        NormalPctValue = stats.NormalDies / total * 100;
        DefectPctValue = stats.DefectDies / total * 100;
        FailedPctValue = stats.FailedDies / total * 100;
        NormalPct = $"{NormalPctValue:F1}%";
        DefectPct = $"{DefectPctValue:F1}%";
        FailedPct = $"{FailedPctValue:F1}%";
    }

    /// <summary>实时缺陷列表（F-3.1.8-01~07）：增量追加，界面侧只保留最近 200 行。</summary>
    private void ApplyDefects(IReadOnlyList<DefectRecord> records)
    {
        if (records.Count < _lastDefectCount)
        {
            Defects.Clear();
            _lastDefectCount = 0;
        }

        for (int i = _lastDefectCount; i < records.Count; i++)
            Defects.Add(DefectRow.From(records[i]));
        _lastDefectCount = records.Count;

        while (Defects.Count > 200)
            Defects.RemoveAt(0);
    }

    /// <summary>Wafer 状态（F-3.1.2-04）：按当前 WaferId 的序号映射到勾选槽位。</summary>
    private void UpdateSlotStates(string waferId)
    {
        if (waferId is null || waferId == _lastWaferId) return;
        _lastWaferId = waferId;

        var checkedSlots = CurrentPort.Slots.Where(s => s.IsChecked && s.HasWafer).ToList();
        int dash = waferId.LastIndexOf("-W", StringComparison.Ordinal);
        int index = dash >= 0 && int.TryParse(waferId[(dash + 2)..], out int n) ? n - 1 : 0;

        for (int i = 0; i < checkedSlots.Count; i++)
            checkedSlots[i].StateText = i < index ? "已完成" : i == index ? "检测中" : "待检";
    }

    private void RefreshInterlock()
    {
        HasInterlock = _alarms.IsRunBlocked;
        InterlockText = _alarms.BlockReason ?? string.Empty;
    }

    private void RefreshCommands()
    {
        StartOrResumeCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        EndBatchCommand.NotifyCanExecuteChanged();
        SelectJobCommand.NotifyCanExecuteChanged();
        ConfirmSelectionCommand.NotifyCanExecuteChanged();
    }

    /* ---------------- 料盒与模式（F-3.1.2-01~16） ---------------- */

    [RelayCommand]
    private void SelectPort(string portId)
    {
        var port = Ports.FirstOrDefault(p => p.PortId == portId);
        if (port is null) return;
        foreach (var p in Ports) p.IsCurrent = ReferenceEquals(p, port);
        CurrentPort = port;
        UpdateSlotStates(_lastWaferId);
        UpdateCheckedText();
    }

    [RelayCommand]
    private void SetMode(string mode)
    {
        Mode = mode switch
        {
            "Auto" => LoadMode.Auto,
            "SemiAuto" => LoadMode.SemiAuto,
            _ => LoadMode.Manual
        };
        OnPropertyChanged(nameof(ModeHint));
        RefreshCommands();
        AppendLog(Info("WaferRun", $"上料模式切换为：{ModeText}（三态互斥，F-3.1.2-07）"));
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var slot in CurrentPort.Slots) slot.IsChecked = slot.HasWafer;
        UpdateCheckedText();
    }

    [RelayCommand]
    private void InvertAll()
    {
        foreach (var slot in CurrentPort.Slots) slot.IsChecked = slot.HasWafer && !slot.IsChecked;
        UpdateCheckedText();
    }

    private void UpdateCheckedText()
        => CheckedText = $"已勾选 {CurrentPort.CheckedCount} 片（{CurrentPort.PortId}）";

    /// <summary>F-3.1.2-16：确认通常置灰，半自动模式下勾选晶圆后可用。</summary>
    private bool CanConfirmSelection => Mode == LoadMode.SemiAuto && CurrentPort.CheckedCount > 0;

    [RelayCommand(CanExecute = nameof(CanConfirmSelection))]
    private void ConfirmSelection()
    {
        AppendLog(Info("WaferRun",
            $"半自动确认：{CurrentPort.PortId} 勾选 {CurrentPort.CheckedCount} 片作为本次待检晶圆（F-3.1.2-16）"));
        _dialogs.ShowMessage("确认完成",
            $"已确认 {CurrentPort.PortId} 的 {CurrentPort.CheckedCount} 片晶圆为本次待检对象。\n" +
            "EFEM 上料动作未下发（设备连接搁置 T-029）。");
    }

    /// <summary>F-3.1.2-11 上料：设备动作搁置，仅提交指令意图，不伪造成功。</summary>
    [RelayCommand]
    private void Load()
    {
        if (CurrentPort.CheckedCount == 0)
        {
            _dialogs.ShowMessage("上料", "请先勾选待检测晶圆（F-3.1.2-05）。");
            return;
        }

        AppendLog(Info("EFEM", $"[搁置] 上料指令未下发：{CurrentPort.PortId} × {CurrentPort.CheckedCount} 片（T-029）"));
        _dialogs.ShowMessage("上料已搁置",
            $"已记录上料意图（{CurrentPort.PortId}，{CurrentPort.CheckedCount} 片），" +
            "但 EFEM 设备连接未接入（T-029），指令未下发、载物台未动作。");
    }

    [RelayCommand]
    private void Unload()
    {
        AppendLog(Info("EFEM", $"[搁置] 下料指令未下发：{CurrentPort.PortId}（T-029）"));
        _dialogs.ShowMessage("下料已搁置", "已记录下料意图，EFEM 设备连接未接入（T-029），指令未下发。");
    }

    /// <summary>F-3.1.2-12：手动模式上料位（BR-05 第一步）。</summary>
    [RelayCommand]
    private void MoveToLoadPosition() => AdvanceManual(ManualLoadStep.MoveToLoadPosition);

    [RelayCommand]
    private void OpenSafetyDoor() => AdvanceManual(ManualLoadStep.OpenSafetyDoor);

    [RelayCommand]
    private void PlaceWafer() => AdvanceManual(ManualLoadStep.PlaceWafer);

    [RelayCommand]
    private void CloseSafetyDoor() => AdvanceManual(ManualLoadStep.CloseSafetyDoor);

    [RelayCommand]
    private void MoveToPhotoPosition() => AdvanceManual(ManualLoadStep.MoveToPhotoPosition);

    private void AdvanceManual(ManualLoadStep step)
    {
        if (!IsManual)
        {
            _dialogs.ShowMessage("手动上料", "仅手动模式下执行上料位 / 拍照位 / 安全门操作（F-3.1.2-08）。");
            return;
        }

        try
        {
            _manual.Advance(step);
        }
        catch (InvalidOperationException ex)
        {
            // BR-05：跳步被明确拒绝，不静默纠正顺序。
            _dialogs.ShowMessage("操作顺序错误", ex.Message);
            return;
        }

        ManualPrompt = _manual.Prompt;
        AppendLog(Info("WaferRun", $"手动上料步骤完成：{step}（BR-05）→ {_manual.Prompt}"));
    }

    [RelayCommand]
    private void ResetManual()
    {
        _manual.Reset();
        ManualPrompt = _manual.Prompt;
        AppendLog(Info("WaferRun", "手动上料流程已重置，可重新放片"));
    }

    /// <summary>F-3.1.2-13 / 17~25 / 27：EFEM 设置弹窗。</summary>
    [RelayCommand]
    private void OpenEfemSettings()
    {
        var current = new EfemSettingsResult(
            CurrentPort.PortId, CurrentPort.IsLocked, CurrentPort.BarcodeSide, CurrentPort.BarcodeAngle, false);
        var result = _dialogs.EditEfemSettings(current);
        if (result is null) return;

        var port = Ports.FirstOrDefault(p => p.PortId == result.PortId);
        if (port is null) return;

        if (result.PortId != CurrentPort.PortId)
        {
            foreach (var p in Ports) p.IsCurrent = ReferenceEquals(p, port);
            CurrentPort = port;
        }

        port.IsLocked = result.IsLocked;
        port.BarcodeSide = result.BarcodeSide;
        port.BarcodeAngle = result.BarcodeAngle;

        AppendLog(Info("EFEM",
            $"EFEM 设置已更新：{port.PortId} 锁定={port.IsLocked} · 条码={port.BarcodeSideText} · 角度={port.BarcodeAngle}°（BR-02/BR-03）"));

        if (result.FlipRequested)
        {
            // F-3.1.2-27（T-002 已确认）：翻面 = 正/反面互换后重新扫描该面；物理动作随 T-029 搁置。
            FlipCount++;
            AppendLog(Info("EFEM",
                $"翻面请求已记录：第 {FlipCount} 次（F-3.1.2-27，T-002）· 正/反面互换，设备执行后需重新扫描该面"));
            AppendLog(Info("EFEM", "[搁置] 物理翻面动作未下发（设备连接搁置 T-029）"));
        }

        // F-3.1.2-25：弹窗内的「扫描料盒」等价于料盒信息栏的扫描动作。
        if (result.ScanRequested)
            ScanCassette();
    }

    /// <summary>F-3.1.2-20/21：上料盒 / 下料盒互斥切换并控制仓门。</summary>
    [RelayCommand]
    private void ToggleLock()
    {
        CurrentPort.IsLocked = !CurrentPort.IsLocked;
        AppendLog(Info("EFEM",
            $"{CurrentPort.PortId} {(CurrentPort.IsLocked ? "上料盒：锁定料盒，仓门禁止打开" : "下料盒：解除锁定，仓门可开")}（F-3.1.2-20/21）"));
        AppendLog(Info("EFEM", "[搁置] 料盒锁定 / 仓门控制为 EFEM 设备动作，未下发（T-029）"));
    }

    /// <summary>F-3.1.2-22：Unlock 只解锁仓门，不改变料盒状态。</summary>
    [RelayCommand]
    private void UnlockDoor()
    {
        AppendLog(Info("EFEM", $"{CurrentPort.PortId} Unlock：请求解锁仓门（不改变料盒状态，F-3.1.2-22）；设备动作搁置 T-029"));
        _dialogs.ShowMessage("Unlock 已搁置", "Unlock 需驱动 EFEM 仓门，设备连接未接入（T-029），指令未下发。");
    }

    /// <summary>F-3.1.2-25：扫描料盒（真实扫描为设备动作，此处为明确标注的模拟填充）。</summary>
    [RelayCommand]
    private void ScanCassette()
    {
        for (int i = 1; i <= SlotCount; i++)
        {
            var slot = CurrentPort.Slots[i - 1];
            bool has = i is not (7 or 19);
            slot.HasWafer = has;
            slot.WaferId = has ? $"{i:000}.{CurrentPort.PortId}_{i:00}" : string.Empty;
            slot.StateText = "待机";
            slot.IsChecked = false;
        }

        CurrentPort.CassettePresent = true;
        UpdateCheckedText();
        AppendLog(Info("EFEM",
            $"{CurrentPort.PortId} 料盒扫描完成：{CurrentPort.Slots.Count(s => s.HasWafer)} 片在位（模拟数据源，真实扫描搁置 T-029）"));
    }

    /* ---------------- 运行控制（F-3.1.5-01~12） ---------------- */

    private bool CanSelectJob => _run.CanApplyJobs;

    [RelayCommand(CanExecute = nameof(CanSelectJob))]
    private void SelectJob()
    {
        var picked = _dialogs.SelectJobs(_jobs.Jobs, _selected);
        if (picked is null || picked.Count == 0) return;

        _selected.Clear();
        _selected.AddRange(picked);
        SelectedJobs.Clear();
        foreach (var job in picked) SelectedJobs.Add(job);

        RecipeNameText = string.Join(" → ", picked.Select(j => j.RecipeName));
        AppendLog(Info("WaferRun", $"已选择 {picked.Count} 个 Job：{string.Join("、", picked.Select(j => j.JobName))}（可多选，BR-10 依次执行）"));
        ApplyCommand.NotifyCanExecuteChanged();
    }

    private bool CanApply => _selected.Count > 0 && _run.CanApplyJobs;

    /// <summary>F-3.1.5-12 应用：把选中 Recipe 设为待检测 Job。</summary>
    [RelayCommand(CanExecute = nameof(CanApply))]
    private void Apply()
    {
        try
        {
            _run.ApplyJobs(_selected.ToArray());
        }
        catch (InvalidOperationException ex)
        {
            _dialogs.ShowMessage("应用失败", ex.Message);
            return;
        }

        var job = _selected[0];
        JobNameText = job.JobName;
        WaferSizeText = $"{job.WaferSize:F0} mm";
        JobDieSizeXText = $"{job.DieSizeX:F1} mm";
        JobDieSizeYText = $"{job.DieSizeY:F1} mm";
        DieSizeXText = $"{job.DieSizeX:F1} mm";
        DieSizeYText = $"{job.DieSizeY:F1} mm";
    }

    /// <summary>F-3.1.5-08 查看：查看 JobName 对应配方的全流程信息。</summary>
    [RelayCommand]
    private void ViewJob()
    {
        var job = _selected.FirstOrDefault();
        if (job is null)
        {
            _dialogs.ShowMessage("查看", "请先选择 Job（F-3.1.5-06）。");
            return;
        }
        _dialogs.ShowJobInfo(job);
    }

    /// <summary>F-3.1.5-09 Clear：设备上所有位置晶圆回归原始料盒（设备动作，搁置）。</summary>
    [RelayCommand]
    private void Clear()
    {
        AppendLog(Info("EFEM", "[搁置] Clear 指令未下发：设备上所有位置的晶圆应回归原始料盒（F-3.1.5-09 / T-029）"));
        _dialogs.ShowMessage("Clear 已搁置",
            "Clear（所有位置晶圆回归原始料盒）需驱动 EFEM 机械手，设备连接未接入（T-029），指令未下发。");
    }

    /// <summary>F-3.1.5-10 配方管理：跳转 H02 配方模块。</summary>
    [RelayCommand]
    private void OpenRecipeManagement() => NavigationRequested?.Invoke(this, "recipes");

    /// <summary>F-3.1.5-11 WDI 初始化：与激光引导自动聚焦控制器建立通信（设备动作，搁置）。</summary>
    [RelayCommand]
    private void WdiInit()
    {
        AppendLog(Info("WDI", "[搁置] WDI 初始化未执行：激光引导自动聚焦控制器通信未接入（F-3.1.5-11 / T-029）"));
        _dialogs.ShowMessage("WDI 初始化已搁置", "需与激光引导自动聚焦控制器建立通信，设备连接未接入（T-029），未执行。");
    }

    /// <summary>F-3.1.5-01 / F-3.1.5-03：开始运行；暂停后同一按钮变为「继续运行」。</summary>
    [RelayCommand(CanExecute = nameof(CanStartOrResume))]
    private void StartOrResume()
    {
        try
        {
            if (_run.Phase == RunPhase.Paused)
            {
                _run.Resume();       // BR-08：从头开始扫描，不是断点续扫
            }
            else
            {
                _run.Start();
                _manual.Reset();
                ManualPrompt = _manual.Prompt;
            }
        }
        catch (RunBlockedException ex)
        {
            _dialogs.ShowMessage("禁止开始运行", ex.Message);  // BR-74
        }
        catch (InvalidOperationException ex)
        {
            _dialogs.ShowMessage("操作被拒绝", ex.Message);
        }
    }

    private bool CanStartOrResume => _run.CanStart || _run.CanResume;

    [RelayCommand(CanExecute = nameof(CanPauseRun))]
    private void PauseRun()
    {
        _run.Pause();
        AppendLog(Info("WaferRun", "扫描已中止：本阶段允许算法调参（F-3.1.10-15，调参入口归 H04）"));
    }

    private bool CanPauseRun => _run.CanPause;

    /// <summary>F-3.1.10-11：确认晶圆信息，可自定义 LotId / WaferId（BR-14 优先于 OCR）。</summary>
    [RelayCommand]
    private void ConfirmWaferInfo()
    {
        var result = _dialogs.ConfirmWafer(LotId, WaferId);
        if (result is null) return;
        try
        {
            _run.ConfirmWaferInfo(result.LotId, result.WaferId);
        }
        catch (InvalidOperationException ex)
        {
            _dialogs.ShowMessage("确认被拒绝", ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(CanStopRun))]
    private void Stop() => _run.Stop();       // BR-09：立即中断当前晶圆

    private bool CanStopRun => _run.CanStop;

    [RelayCommand(CanExecute = nameof(CanEndBatchRun))]
    private void EndBatch() => _run.EndBatch();  // BR-06：扫完当前批次后结束

    private bool CanEndBatchRun => _run.CanEndBatch;

    /// <summary>F-3.1.4-03：双击切换图谱 / 图片视窗。</summary>
    [RelayCommand]
    private void ToggleViewMode()
    {
        IsImageView = !IsImageView;
        AppendLog(Info("H01", $"视图区切换为：{ViewModeText}（F-3.1.4-03）"));
    }

    /* ---------------- 图谱（H05 唯一渲染） ---------------- */

    private void BuildMap()
    {
        string[] labels = ["正常", "轻微", "警告", "缺陷"];
        var colors = new[] { "#E9F1FD", "#9CC4F8", "#F5B942", "#E5484D" }
            .Select(c => (Brush)Brushes.ConvertFromString(c)!).ToArray();

        var map = _map.Render(new MapRenderRequest(0, "PL"));
        foreach (var cell in map.Cells)
        {
            MapCells.Add(new MapCell(cell.X, cell.Y, colors[cell.Level],
                $"R{cell.Row + 1:00} · C{cell.Column + 1:00}   {labels[cell.Level]}"));
        }

        MapNormal = map.Normal;
        MapLight = map.Light;
        MapWarn = map.Warning;
        MapBad = map.Bad;
        MapYieldText = $"{map.YieldPercentage:F2}%";
    }

    /* ---------------- 日志与报警（F-3.1.9 / H09 事件源） ---------------- */

    private void OnEventPublished(object sender, SystemEvent e) => AppendLog(e);

    private void AppendLog(SystemEvent e)
    {
        if (Application.Current is { } app && !app.Dispatcher.CheckAccess())
        {
            app.Dispatcher.Invoke(() => AppendLog(e));
            return;
        }

        var brush = (Brush)Brushes.ConvertFromString(e.Level switch
        {
            "Error" or "Fatal" => "#C62828",
            "Warn" => "#B45309",
            "HighAlarm" => "#C62828",
            "LowAlarm" => "#B45309",
            _ => "#6B7C93"
        })!;

        Logs.Add(new LogRow(LogFormat.LogTime(e.Timestamp), e.Level, $"[{e.Source}] {e.Message}", brush));
        while (Logs.Count > 300)
            Logs.RemoveAt(0);
    }

    private static SystemEvent Info(string source, string message)
        => new($"H01-{DateTimeOffset.Now:HHmmss-ffffff}", DateTimeOffset.Now, source, "Run", "Info",
            message, new Dictionary<string, string>());

    private void OnAlarmRaised(object sender, SystemAlarm alarm)
    {
        if (Application.Current is { } app && !app.Dispatcher.CheckAccess())
        {
            app.Dispatcher.Invoke(() => OnAlarmRaised(sender, alarm));
            return;
        }

        RefreshInterlock();
        AppendLog(Info("H09", $"报警同步至检测任务页：{alarm.Message}（{alarm.Severity}）"));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _run.SnapshotChanged -= OnSnapshotChanged;
        _events.EventPublished -= OnEventPublished;
        _alarms.AlarmRaised -= OnAlarmRaised;
        _pump.Tick -= OnTick;
    }
}
