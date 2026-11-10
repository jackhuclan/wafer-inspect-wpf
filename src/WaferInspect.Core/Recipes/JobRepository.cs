using WaferInspect.Core.Maps;

namespace WaferInspect.Core.Recipes;

/// <summary>
/// Job 仓库（`R-H02-01`，B-15 的唯一 Owner）：新增 / 复制 / 重命名 / 删除 / 刷新。
/// 写操作统一走 <see cref="JobValidator"/>，校验失败抛 <see cref="JobValidationException"/>，
/// 界面据此逐字段提示（E-05：不得吞异常或把失败转成成功）。
/// </summary>
public interface IJobRepository
{
    /// <summary>当前 Job 快照（按名称排序，刷新后返回新快照）。</summary>
    IReadOnlyList<JobDefinition> Jobs { get; }

    /// <summary>内容变化通知（新增 / 复制 / 重命名 / 删除 / 刷新）。</summary>
    event EventHandler? Changed;

    JobDefinition Create(JobDefinition draft);

    JobDefinition Update(JobDefinition job);

    JobDefinition Rename(string jobId, string newName);

    void Delete(string jobId);

    JobDefinition Duplicate(string jobId);

    /// <summary>F-3.2.1-05：刷新列表（重新读取仓库快照并通知）。</summary>
    void Refresh();

    JobDefinition? Find(string jobId);
}

/// <summary>
/// 原型阶段的 Job 仓库实现：内存 + 确定性种子数据（不使用随机数，便于回归）。
/// 真实持久化（数据库 / 配置文件）随 H07 配置单点确定后替换，本类不承担文件格式职责。
/// </summary>
public sealed class InMemoryJobRepository : IJobRepository
{
    private readonly List<JobDefinition> _jobs = [];
    private readonly IMapTypeCatalog _mapTypes;
    private int _sequence = 1000;

    public InMemoryJobRepository(IMapTypeCatalog mapTypes)
    {
        _mapTypes = mapTypes;
        Seed();
        _jobs.Sort(CompareByName);
    }

    public IReadOnlyList<JobDefinition> Jobs => _jobs;

    public event EventHandler? Changed;

    public JobDefinition? Find(string jobId)
        => _jobs.FirstOrDefault(j => string.Equals(j.JobId, jobId, StringComparison.Ordinal));

    public JobDefinition Create(JobDefinition draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        draft.Name = draft.Name?.Trim() ?? string.Empty;
        EnsureNameAvailable(draft.Name, ignoreJobId: null);

        var issues = JobValidator.Validate(draft);
        if (issues.Count > 0) throw new JobValidationException(issues);

        var job = draft.Clone();
        job.JobId = $"JOB-{++_sequence}";
        _jobs.Add(job);
        _jobs.Sort(CompareByName);
        Raise();
        return job;
    }

    public JobDefinition Update(JobDefinition job)
    {
        ArgumentNullException.ThrowIfNull(job);
        var existing = Find(job.JobId)
            ?? throw new InvalidOperationException($"Job 不存在：{job.JobId}（可能已被其它会话删除，请刷新列表）。");

        job.Name = job.Name?.Trim() ?? string.Empty;
        EnsureNameAvailable(job.Name, ignoreJobId: job.JobId);

        var issues = JobValidator.Validate(job);
        if (issues.Count > 0) throw new JobValidationException(issues);

        existing.Name = job.Name;
        existing.PatternType = job.PatternType;
        existing.Size = job.Size;
        existing.ThicknessMm = job.ThicknessMm;
        existing.DieXmm = job.DieXmm;
        existing.DieYmm = job.DieYmm;
        existing.NotchAngle = job.NotchAngle;
        existing.MapTypeId = job.MapTypeId;
        existing.MapFileType = job.MapFileType;
        existing.MapFilePath = job.MapFilePath;
        existing.ColumnDirection = job.ColumnDirection;
        existing.RowDirection = job.RowDirection;
        existing.BarcodeCode = job.BarcodeCode;
        existing.OcrJob = job.OcrJob;
        existing.OcrAngle = job.OcrAngle;
        existing.OcrPosition = job.OcrPosition;
        existing.LocateDieX = job.LocateDieX;
        existing.LocateDieY = job.LocateDieY;
        existing.Orientation = job.Orientation;
        existing.ColumnOffset = job.ColumnOffset;
        existing.RowOffset = job.RowOffset;
        existing.ColRow = job.ColRow;
        existing.Xy = job.Xy;
        existing.IncludeExclude = job.IncludeExclude;
        existing.TraceImage = job.TraceImage;

        _jobs.Sort(CompareByName);
        Raise();
        return existing;
    }

    public JobDefinition Rename(string jobId, string newName)
    {
        var job = Find(jobId)
            ?? throw new InvalidOperationException($"Job 不存在：{jobId}（可能已被其它会话删除，请刷新列表）。");

        newName = newName?.Trim() ?? string.Empty;
        if (newName.Length == 0)
            throw new JobValidationException([new(nameof(JobDefinition.Name), "Job 名称必填（F-3.2.1-06）。")]);

        EnsureNameAvailable(newName, ignoreJobId: jobId);
        job.Name = newName;
        _jobs.Sort(CompareByName);
        Raise();
        return job;
    }

    public void Delete(string jobId)
    {
        var job = Find(jobId)
            ?? throw new InvalidOperationException($"Job 不存在：{jobId}（可能已被其它会话删除，请刷新列表）。");

        _jobs.Remove(job);
        Raise();
    }

    public JobDefinition Duplicate(string jobId)
    {
        var source = Find(jobId)
            ?? throw new InvalidOperationException($"Job 不存在：{jobId}（可能已被其它会话删除，请刷新列表）。");

        // F-3.2.1-02：复制所选 Job 的全部配置生成新 Job，名称追加副本后缀并保证唯一。
        var copy = source.Clone();
        copy.JobId = $"JOB-{++_sequence}";
        copy.Name = MakeCopyName(source.Name);
        _jobs.Add(copy);
        _jobs.Sort(CompareByName);
        Raise();
        return copy;
    }

    public void Refresh() => Raise();

    private void EnsureNameAvailable(string name, string? ignoreJobId)
    {
        if (name.Length == 0) return;   // 空名由必填校验统一报错，避免重复提示
        bool duplicated = _jobs.Any(j =>
            !string.Equals(j.JobId, ignoreJobId, StringComparison.Ordinal) &&
            string.Equals(j.Name, name, StringComparison.OrdinalIgnoreCase));
        if (duplicated)
            throw new JobValidationException([new(nameof(JobDefinition.Name), $"Job 名称已存在：{name}（名称需唯一）。")]);
    }

    private string MakeCopyName(string sourceName)
    {
        for (int i = 1; i < 1000; i++)
        {
            string candidate = $"{sourceName}-副本{(i == 1 ? string.Empty : i.ToString())}";
            bool exists = _jobs.Any(j => string.Equals(j.Name, candidate, StringComparison.OrdinalIgnoreCase));
            if (!exists) return candidate;
        }
        return $"{sourceName}-副本{Environment.TickCount}";
    }

    private static int CompareByName(JobDefinition x, JobDefinition y)
        => string.Compare(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);

    private void Raise() => Changed?.Invoke(this, EventArgs.Empty);

    private void Seed()
    {
        string firstMapType = _mapTypes.Types.Count > 0 ? _mapTypes.Types[0].Id : "MAP-PL-300";
        _jobs.AddRange(
        [
            new JobDefinition
            {
                JobId = "JOB-0901",
                Name = "PL-STD-300mm-R3",
                PatternType = WaferPatternType.PatternWithMark,
                Size = WaferSize.Inch12,
                DieXmm = 24.0,
                DieYmm = 25.0,
                NotchAngle = 270,
                MapTypeId = firstMapType,
                MapFileType = MapFileType.Csv,
                MapFilePath = SampleMapPath("pl-std-300mm.csv"),
                BarcodeCode = "1",
                OcrJob = "OCR-STD-R1",
                OcrAngle = 0,
                OcrPosition = OcrPosition.Front,
                TraceImage = null
            },
            new JobDefinition
            {
                JobId = "JOB-0902",
                Name = "EL-HIGHGAIN-R2",
                PatternType = WaferPatternType.PatternWithMark,
                Size = WaferSize.Inch12,
                DieXmm = 32.0,
                DieYmm = 22.0,
                NotchAngle = 270,
                MapTypeId = firstMapType,
                MapFileType = MapFileType.Txt,
                MapFilePath = SampleMapPath("el-highgain-300mm.txt"),
                BarcodeCode = "2",
                OcrJob = "OCR-STD-R1",
                OcrAngle = 90,
                OcrPosition = OcrPosition.Front,
                TraceImage = TraceImagePolicy.UploadDefectOnly
            },
            new JobDefinition
            {
                JobId = "JOB-0903",
                Name = "AOI-FINESCAN-R5",
                PatternType = WaferPatternType.PatternWithoutMark,
                Size = WaferSize.Inch8,
                DieXmm = 18.0,
                DieYmm = 18.0,
                NotchAngle = 180,
                MapTypeId = firstMapType,
                MapFileType = MapFileType.Csv,
                MapFilePath = SampleMapPath("aoi-finescan-200mm.csv"),
                BarcodeCode = "1",
                OcrJob = "OCR-FINE-R2",
                OcrAngle = 0,
                OcrPosition = OcrPosition.Back,
                TraceImage = TraceImagePolicy.DoNotUpload
            }
        ]);
    }

    /// <summary>样例 Map 文件随应用输出到 <c>samples/</c>（见 App csproj 的 Content 项），此处只拼路径。</summary>
    private static string SampleMapPath(string fileName)
        => Path.Combine(AppContext.BaseDirectory, "samples", fileName);
}
