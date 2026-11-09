using WaferInspect.Core.Events;

namespace WaferInspect.Core.Run;

/// <summary>
/// 运行拦截信号（R-H09-06 / BR-74）：高等级报警未手动处理时禁止开始运行。
/// 由 H09 报警中心实现，H01 状态机消费；H01 不得自行判断报警等级。
/// </summary>
public interface IRunInterlock
{
    bool IsRunBlocked { get; }

    string? BlockReason { get; }
}

/// <summary>运行控制器快照：当前 Recipe / 晶圆 / 阶段 / 队列剩余。</summary>
public sealed record RunSnapshot(
    RunPhase Phase,
    string? CurrentRecipe,
    string? CurrentWaferId,
    string? CurrentLotId,
    int RemainingRecipes,
    ScanProgress Progress,
    BatchStats Stats,
    IReadOnlyList<DefectRecord> Defects);

/// <summary>
/// 运行配方状态机唯一实现（B-26 唯一 Owner；R-H01-08/09）。
/// 历史行为按原样保留：
/// - BR-06 结批：不立即停止，扫完当前批次所有晶圆后结束，不再开始后续 Recipe；
/// - BR-08 继续运行：扫描从头开始执行（禁止改成断点续扫，T-003 冲突项按手册原义实现）；
/// - BR-09 停止运行：立即中断当前晶圆并结束扫描；
/// - BR-10 Recipe 多选依次执行；
/// - BR-74 高等级错误未处理时禁止开始运行；
/// - BR-11 批次数据固定三类（正常 / 缺陷 / 检测失败），不可增删；
/// - 扫描中止（Paused）期间才允许 H04 调参。
/// 自定义 LotId / WaferId 优先级高于 OCR 结果（BR-14 / T-023）。
/// 真实设备动作（上下料、相机、EFEM）不在本类内；设备命令走 DeviceCommandLayer（T-029 搁置）。
/// 复判个数（F-3.1.3-07）在模拟数据源下按确定性规则生成；真实彩色相机复判事件接入后替换（T-029）。
/// </summary>
public sealed class WaferRunController
{
    private readonly IRunInterlock? _interlock;
    private readonly IEventSource _events;
    private readonly List<JobSelection> _queue = [];
    private readonly List<DefectRecord> _defects = [];

    private RunPhase _phase = RunPhase.Idle;
    private int _queueIndex;
    private int _waferIndex;
    private int _waferTotalDies;

    // 已提交（已完成晶圆）的累计值。
    private int _committedDies;
    private int _committedDefects;
    private int _committedFree;
    private int _committedDefective;
    private int _committedFailed;
    private int _committedReview;

    // 当前晶圆的进行中计数；BR-08 继续运行时归零重扫。
    private int _waferScanned;
    private int _waferDefects;
    private int _waferFree;
    private int _waferDefective;
    private int _waferFailed;
    private int _waferReview;
    private int _waferDefectStart;

    private double _percent;
    private TimeSpan _elapsed;
    private string? _lotId;
    private string? _waferId;

    public WaferRunController(IEventSource events, IRunInterlock? interlock = null)
    {
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _interlock = interlock;
    }

    public event EventHandler<RunSnapshot>? SnapshotChanged;

    public RunPhase Phase => _phase;

    public string? CurrentRecipe => _queueIndex < _queue.Count ? _queue[_queueIndex].RecipeName : null;
    public string? CurrentWaferId => _waferId;
    public string? CurrentLotId => _lotId;
    public IReadOnlyList<JobSelection> Queue => _queue;

    /// <summary>状态机禁用/启用规则（R-H01-09）。</summary>
    public bool CanApplyJobs => _phase is RunPhase.Idle or RunPhase.Ready or RunPhase.Finished;
    public bool CanStart => _phase is RunPhase.Idle or RunPhase.Ready && _queue.Count > 0;
    public bool CanPause => _phase is RunPhase.Running;
    public bool CanResume => _phase is RunPhase.Paused;
    public bool CanStop => _phase is RunPhase.Running or RunPhase.Paused or RunPhase.BatchEnding;
    public bool CanEndBatch => _phase is RunPhase.Running;

    /// <summary>扫描中止期间才允许算法调参（F-3.1.10-15）。</summary>
    public bool CanTuneAlgorithm => _phase is RunPhase.Paused;

    /// <summary>应用 Job 队列（F-3.1.5-06/12，BR-10 多选依次执行）。</summary>
    public void ApplyJobs(IReadOnlyList<JobSelection> jobs)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        if (jobs.Count == 0)
            throw new ArgumentException("至少选择一个待检测 Job。", nameof(jobs));
        if (!CanApplyJobs)
            throw new InvalidOperationException($"当前阶段 {_phase} 不允许选择 / 应用 Job。");

        _queue.Clear();
        _queue.AddRange(jobs);
        _queueIndex = 0;
        _phase = RunPhase.Ready;
        ResetCounters();
        Publish("Info", "WaferRun", $"已应用 {_queue.Count} 个检测 Job，可开始运行");
        RaiseSnapshot();
    }

    /// <summary>开始运行（F-3.1.10-09）。BR-74：高等级错误未处理时被拦截。</summary>
    public void Start()
    {
        if (!CanStart)
            throw new InvalidOperationException($"当前阶段 {_phase} 不允许开始运行。");
        if (_interlock is { IsRunBlocked: true })
            throw new RunBlockedException(_interlock.BlockReason ?? "存在未处理的高等级错误。");

        _phase = RunPhase.Running;
        StartWafer(0);
        Publish("Info", "WaferRun", $"开始运行：{_queue[_queueIndex].RecipeName}（队列 {_queue.Count} 个 Job 依次执行）");
        RaiseSnapshot();
    }

    /// <summary>暂停运行（F-3.1.10-14）：扫描中止。</summary>
    public void Pause()
    {
        if (!CanPause)
            throw new InvalidOperationException($"当前阶段 {_phase} 不允许暂停。");
        _phase = RunPhase.Paused;
        Publish("Warn", "WaferRun", "暂停运行：扫描中止，可执行算法调参");
        RaiseSnapshot();
    }

    /// <summary>继续运行（F-3.1.10-16）。BR-08：扫描从头开始执行，不是断点续扫。</summary>
    public void Resume()
    {
        if (!CanResume)
            throw new InvalidOperationException($"当前阶段 {_phase} 不允许继续运行。");
        _phase = RunPhase.Running;
        ResetWaferCounters();
        if (_defects.Count > _waferDefectStart)
            _defects.RemoveRange(_waferDefectStart, _defects.Count - _waferDefectStart);
        _percent = 0;
        _elapsed = TimeSpan.Zero;
        Publish("Warn", "WaferRun", $"继续运行：按手册 BR-08 当前晶圆 {_waferId} 从头开始扫描");
        RaiseSnapshot();
    }

    /// <summary>停止运行（F-3.1.10-17 / BR-09）：立即中断当前晶圆并结束。</summary>
    public void Stop()
    {
        if (!CanStop)
            throw new InvalidOperationException($"当前阶段 {_phase} 不允许停止运行。");
        _phase = RunPhase.Finished;
        Publish("Warn", "WaferRun", $"停止运行：当前晶圆 {_waferId} 被中断（BR-09），任务结束");
        RaiseSnapshot();
    }

    /// <summary>结批（F-3.1.5-05 / BR-06）：不再开始后续 Recipe，扫完当前批次后结束。</summary>
    public void EndBatch()
    {
        if (!CanEndBatch)
            throw new InvalidOperationException($"当前阶段 {_phase} 不允许结批。");
        _phase = RunPhase.BatchEnding;
        Publish("Info", "WaferRun", "结批：当前批次晶圆将全部扫描完成后结束（BR-06），不再开始后续 Job");
        RaiseSnapshot();
    }

    /// <summary>确认晶圆信息（F-3.1.10-11）。自定义 LotId/WaferId 优先于 OCR（BR-14 / T-023）。</summary>
    public void ConfirmWaferInfo(string lotId, string waferId)
    {
        if (_phase is not (RunPhase.Ready or RunPhase.Running))
            throw new InvalidOperationException($"当前阶段 {_phase} 不允许修改晶圆信息。");
        _lotId = string.IsNullOrWhiteSpace(lotId) ? _lotId : lotId;
        _waferId = string.IsNullOrWhiteSpace(waferId) ? _waferId : waferId;
        Publish("Info", "WaferRun", $"确认晶圆信息：LotId={_lotId} · WaferId={_waferId}（自定义值优先于 OCR，BR-14）");
        RaiseSnapshot();
    }

    /// <summary>1 Hz 推进（模拟数据源节拍）；仅在 Running / BatchEnding 阶段推进扫描。</summary>
    public void Advance(TimeSpan step)
    {
        if (step <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(step));
        if (_phase is not (RunPhase.Running or RunPhase.BatchEnding))
            return;

        _elapsed += step;
        for (int i = 0; i < DiesPerTick && _phase is RunPhase.Running or RunPhase.BatchEnding; i++)
            ScanOneDie();

        if (_waferScanned >= _waferTotalDies)
            CompleteWafer();
        RaiseSnapshot();
    }

    /// <summary>
    /// 每节拍扫描的 Die 数。核心逻辑默认 1（一次推进 = 一步扫描，确定且可断言）；
    /// 界面演示节拍由宿主层按模拟速率放大（真实设备接入后由采集速率决定，T-029 搁置）。
    /// </summary>
    public int DiesPerTick { get; set; } = 1;

    private void StartWafer(int waferIndex)
    {
        var job = _queue[_queueIndex];
        _waferIndex = waferIndex;
        _waferTotalDies = job.Rows * job.Columns;
        ResetWaferCounters();
        _waferDefectStart = _defects.Count;
        _percent = 0;
        _elapsed = TimeSpan.Zero;
        _lotId = $"LOT-{job.JobName}";
        _waferId = $"{job.JobName}-W{waferIndex + 1:00}";
    }

    /// <summary>清零「当前晶圆进行中」计数；已提交值保留在 _committed* 中。</summary>
    private void ResetWaferCounters()
    {
        _waferScanned = 0;
        _waferDefects = 0;
        _waferFree = 0;
        _waferDefective = 0;
        _waferFailed = 0;
        _waferReview = 0;
    }

    private void ScanOneDie()
    {
        if (_waferScanned >= _waferTotalDies)
            return;

        var job = _queue[_queueIndex];
        int row = _waferScanned / job.Columns;
        int column = _waferScanned % job.Columns;
        _waferScanned++;
        _percent = 100.0 * _waferScanned / _waferTotalDies;

        // 确定性缺陷判定：与随机无关，按 (job, wafer, row, col) 哈希稳定生成，便于断言与回归。
        int hash = Math.Abs(HashCode.Combine(_queueIndex, _waferIndex, row, column));
        int kind = hash % 11;
        if (kind == 0)
        {
            _waferDefects++;
            _waferDefective++;
            _defects.Add(new DefectRecord(
                Zone: $"Zone-{(char)('A' + row % 6)}",
                Category: hash % 2 == 0 ? "亮缺陷" : "暗缺陷",
                Row: row + 1,
                Column: column + 1,
                Length: 12.0 + hash % 900 / 10.0,
                Width: 8.0 + hash % 400 / 10.0,
                Area: (12.0 + hash % 900 / 10.0) * (8.0 + hash % 400 / 10.0)));
            // 复判个数 = 彩色相机复判的缺陷数（模拟规则：每 3 个缺陷复判 1 个；真实事件接入后替换，T-029）。
            if (hash % 3 == 0)
                _waferReview++;
        }
        else if (kind == 1)
        {
            _waferFailed++;
        }
        else
        {
            _waferFree++;
        }
    }

    private void CompleteWafer()
    {
        _committedDies += _waferScanned;
        _committedDefects += _waferDefects;
        _committedFree += _waferFree;
        _committedDefective += _waferDefective;
        _committedFailed += _waferFailed;
        _committedReview += _waferReview;
        Publish("Info", "WaferRun", $"晶圆 {_waferId} 扫描完成：缺陷 {_waferDefective} · 检测失败 {_waferFailed}");

        // 已结晶的计数不再参与实时聚合，避免提交值与本晶圆进行中值重复累计。
        ResetWaferCounters();
        _percent = 100;

        var job = _queue[_queueIndex];
        if (_waferIndex + 1 < job.WaferCount)
        {
            StartWafer(_waferIndex + 1);
            return;
        }

        // 当前 Recipe 全部晶圆完成：结批（BR-06）到此为止；否则 BR-10 依次执行下一个 Recipe。
        if (_phase == RunPhase.BatchEnding)
        {
            _phase = RunPhase.Finished;
            Publish("Info", "WaferRun", "结批完成：当前批次全部晶圆扫描结束（BR-06）");
            return;
        }
        if (_queueIndex + 1 < _queue.Count)
        {
            _queueIndex++;
            StartWafer(0);
            Publish("Info", "WaferRun", $"依次执行下一个 Job（BR-10）：{_queue[_queueIndex].RecipeName}");
            return;
        }

        _phase = RunPhase.Finished;
        Publish("Info", "WaferRun", "全部 Job 执行完成，任务结束");
    }

    private void ResetCounters()
    {
        _committedDies = 0;
        _committedDefects = 0;
        _committedFree = 0;
        _committedDefective = 0;
        _committedFailed = 0;
        _committedReview = 0;
        _waferScanned = 0;
        _waferDefects = 0;
        _waferFree = 0;
        _waferDefective = 0;
        _waferFailed = 0;
        _waferReview = 0;
        _defects.Clear();
    }

    /// <summary>扫描进度 7 项（F-3.1.3-01~07），实时含当前晶圆进行中计数。</summary>
    public ScanProgress Progress => new(
        _percent,
        _elapsed,
        _committedDies + _waferScanned,
        _committedDefects + _waferDefects,
        _committedFree + _waferFree,
        _committedDefective + _waferDefective,
        _committedReview + _waferReview);

    /// <summary>批次数据固定三类（BR-11）。</summary>
    public BatchStats Stats => new(
        _committedFree + _waferFree,
        _committedDefective + _waferDefective,
        _committedFailed + _waferFailed);

    public IReadOnlyList<DefectRecord> Defects => _defects;

    private void Publish(string level, string source, string message)
        => _events.Publish(new SystemEvent(
            $"RUN-{DateTimeOffset.Now:HHmmss-ffffff}",
            DateTimeOffset.Now, source, "Run", level, message,
            new Dictionary<string, string>()));

    private void RaiseSnapshot()
        => SnapshotChanged?.Invoke(this, new RunSnapshot(
            _phase, CurrentRecipe, _waferId, _lotId,
            Math.Max(0, _queue.Count - _queueIndex - 1),
            Progress, Stats, _defects));
}

/// <summary>BR-74 运行拦截异常；消息为拦截原因原文。</summary>
public sealed class RunBlockedException(string reason) : InvalidOperationException(reason);
