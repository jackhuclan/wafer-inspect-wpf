#nullable enable
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WaferInspect.Core.DependencyInjection;
using WaferInspect.Core.Run;
using Xunit;

namespace WaferInspect.Core.Tests;

/// <summary>
/// P1 手动上料顺序（BR-05）与 Job 清单契约断言。
/// 设备动作（Chuck 移动 / 安全门 / EFEM 上下料）不在本测试范围（T-029 搁置）。
/// </summary>
public sealed class ManualLoadAndJobCatalogTests
{
    [Fact]
    public void ManualLoad_MustFollowFixedOrder()
    {
        var seq = new ManualLoadSequencer();
        Assert.Equal(ManualLoadStep.MoveToLoadPosition, seq.Next);

        seq.Advance(ManualLoadStep.MoveToLoadPosition);
        seq.Advance(ManualLoadStep.OpenSafetyDoor);
        seq.Advance(ManualLoadStep.PlaceWafer);
        seq.Advance(ManualLoadStep.CloseSafetyDoor);
        seq.Advance(ManualLoadStep.MoveToPhotoPosition);

        Assert.True(seq.IsCompleted); // BR-05 五步顺序完成
    }

    [Fact]
    public void ManualLoad_RejectsSkippedStep()
    {
        var seq = new ManualLoadSequencer();

        // BR-05：不得跳步（先开安全门再回上料位也不行）。
        var ex = Assert.Throws<InvalidOperationException>(() => seq.Advance(ManualLoadStep.PlaceWafer));
        Assert.Contains("BR-05", ex.Message);
        Assert.Equal(ManualLoadStep.Idle, seq.Current);
    }

    [Fact]
    public void ManualLoad_ResetStartsOver()
    {
        var seq = new ManualLoadSequencer();
        seq.Advance(ManualLoadStep.MoveToLoadPosition);
        seq.Reset();

        Assert.Equal(ManualLoadStep.Idle, seq.Current);
        Assert.Equal(ManualLoadStep.MoveToLoadPosition, seq.Next);
    }

    [Fact]
    public void JobCatalog_ProvidesSelectableJobsWithCompleteInfo()
    {
        var catalog = new SimulatedJobCatalog();

        Assert.NotEmpty(catalog.Jobs);
        // F-3.1.5-07：Job 信息四项必须齐备，缺一即无法展示。
        Assert.All(catalog.Jobs, job =>
        {
            Assert.False(string.IsNullOrWhiteSpace(job.JobName));
            Assert.True(job.WaferSize > 0);
            Assert.True(job.DieSizeX > 0);
            Assert.True(job.DieSizeY > 0);
            Assert.True(job.WaferCount > 0);
            Assert.True(job.Rows > 0 && job.Columns > 0);
        });

        Assert.NotNull(catalog.Find(catalog.Jobs[0].JobName));
        Assert.Null(catalog.Find("不存在的 Job"));
    }

    [Fact]
    public void CoreHost_ResolvesP1Services()
    {
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services => services.AddWaferInspectCore())
            .Build();

        Assert.NotNull(host.Services.GetRequiredService<ManualLoadSequencer>());
        Assert.NotNull(host.Services.GetRequiredService<IJobCatalog>());
        Assert.NotNull(host.Services.GetRequiredService<WaferRunController>());
    }
}
