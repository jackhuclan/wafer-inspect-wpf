namespace WaferInspect.Core.Run;

/// <summary>
/// 可选 Job / Recipe 清单（F-3.1.5-06 选择 Job 浮窗、F-3.1.5-12 应用）。
/// 真实配方库属 H02（P2，`T-004`/`T-005` 未闭环），本阶段只提供**明确标注为模拟**的固定清单，
/// 供 P1 主流程走通；接入 H02 后替换本实现即可，调用方只认 <see cref="IJobCatalog"/>。
/// </summary>
public interface IJobCatalog
{
    IReadOnlyList<JobSelection> Jobs { get; }

    JobSelection? Find(string jobName);
}

/// <summary>模拟 Job 清单：数值固定可回归，不使用随机数据。</summary>
public sealed class SimulatedJobCatalog : IJobCatalog
{
    public SimulatedJobCatalog()
    {
        Jobs =
        [
            new JobSelection("PL-STD-300mm-R3", "PL-STD-300mm-R3", 300, 24.0, 25.0, 2, 60, 60),
            new JobSelection("EL-HIGHGAIN-R2", "EL-HIGHGAIN-R2", 300, 32.0, 22.0, 1, 48, 40),
            new JobSelection("AOI-FINESCAN-R5", "AOI-FINESCAN-R5", 300, 18.0, 18.0, 2, 80, 80),
            new JobSelection("PL-HR-200mm-R1", "PL-HR-200mm-R1", 200, 16.0, 16.0, 1, 40, 40),
            new JobSelection("EL-LOWDOSE-R4", "EL-LOWDOSE-R4", 200, 28.0, 28.0, 1, 36, 36)
        ];
    }

    public IReadOnlyList<JobSelection> Jobs { get; }

    public JobSelection? Find(string jobName)
        => Jobs.FirstOrDefault(j => string.Equals(j.JobName, jobName, StringComparison.Ordinal));
}
