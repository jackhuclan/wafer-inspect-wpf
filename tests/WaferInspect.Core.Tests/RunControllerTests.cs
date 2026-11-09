#nullable enable
using WaferInspect.Core.DependencyInjection;
using WaferInspect.Core.Events;
using WaferInspect.Core.Run;
using WaferInspect.Core.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace WaferInspect.Core.Tests;

/// <summary>
/// P1 检测任务主干：运行配方状态机、报警中心与自检的契约断言。
/// 设备连接相关行为（EFEM / 相机 / WDI）不在本测试范围（T-029 搁置）。
/// </summary>
public sealed class RunControllerTests
{
    private static JobSelection Job(string name, int wafers = 1, int rows = 2, int cols = 3)
        => new(name, name, 300, 24.0, 25.0, wafers, rows, cols);

    private static (WaferRunController Controller, InMemoryEventSource Events) Create(
        IRunInterlock? interlock = null)
    {
        var events = new InMemoryEventSource();
        return (new WaferRunController(events, interlock), events);
    }

    [Fact]
    public void ApplyJobs_MultiSelectQueue_ThenRunSequentially()
    {
        var (controller, _) = Create();
        controller.ApplyJobs([Job("J1"), Job("J2")]);
        Assert.Equal(2, controller.Queue.Count);
        Assert.Equal(RunPhase.Ready, controller.Phase);
        Assert.True(controller.CanStart);

        controller.Start();
        Assert.Equal(RunPhase.Running, controller.Phase);
        Assert.Equal("J1", controller.CurrentRecipe);

        // BR-10：第一个 Job 全部晶圆完成后依次执行第二个。
        var job = controller.Queue[0];
        for (int i = 0; i < job.Rows * job.Columns * job.WaferCount; i++)
            controller.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal("J2", controller.CurrentRecipe);
        Assert.Equal(RunPhase.Running, controller.Phase);
        Assert.True(controller.Progress.ScannedDies > 0);
    }

    [Fact]
    public void Resume_RestartsCurrentWaferFromBeginning()
    {
        var (controller, _) = Create();
        controller.ApplyJobs([Job("J1", rows: 4, cols: 5)]);
        controller.Start();

        for (int i = 0; i < 5; i++)
            controller.Advance(TimeSpan.FromSeconds(1));
        double before = controller.Progress.Percent;
        Assert.True(before > 0);

        controller.Pause();
        Assert.True(controller.CanTuneAlgorithm); // F-3.1.10-15：扫描中止期间允许调参
        Assert.False(controller.CanPause);

        controller.Resume();
        Assert.Equal(0, controller.Progress.Percent); // BR-08：从头开始，不是断点续扫
        Assert.Equal(TimeSpan.Zero, controller.Progress.Elapsed);
    }

    [Fact]
    public void Stop_InterruptsCurrentWaferImmediately()
    {
        var (controller, _) = Create();
        controller.ApplyJobs([Job("J1", wafers: 3, rows: 4, cols: 5)]);
        controller.Start();
        for (int i = 0; i < 3; i++)
            controller.Advance(TimeSpan.FromSeconds(1));

        controller.Stop();
        Assert.Equal(RunPhase.Finished, controller.Phase);
        Assert.False(controller.CanResume);
        Assert.False(controller.CanEndBatch);
    }

    [Fact]
    public void EndBatch_FinishesCurrentBatchOnly()
    {
        var (controller, _) = Create();
        controller.ApplyJobs([Job("J1", wafers: 2, rows: 2, cols: 3), Job("J2")]);
        controller.Start();
        controller.EndBatch();
        Assert.Equal(RunPhase.BatchEnding, controller.Phase);
        Assert.False(controller.CanEndBatch); // 结批只能停止或等待完成

        // 当前批次（J1 剩余晶圆）扫完后结束，不进入 J2。
        for (int i = 0; i < 2 * 2 * 3; i++)
            controller.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(RunPhase.Finished, controller.Phase);
        Assert.Equal("J1", controller.CurrentRecipe);
    }

    [Fact]
    public void BatchStats_KeepsFixedThreeCategories()
    {
        var (controller, _) = Create();
        controller.ApplyJobs([Job("J1", rows: 6, cols: 6)]);
        controller.Start();
        for (int i = 0; i < 36; i++)
            controller.Advance(TimeSpan.FromSeconds(1));

        var stats = controller.Stats;
        Assert.Equal(36, stats.TotalDies); // BR-11：三类固定且守恒
        Assert.Equal(stats.NormalDies + stats.DefectDies + stats.FailedDies, stats.TotalDies);
        Assert.Equal(36, controller.Progress.ScannedDies);
    }

    [Fact]
    public void Defects_AreDeterministicForSameWaferLayout()
    {
        var (first, _) = Create();
        var (second, _) = Create();
        foreach (var controller in new[] { first, second })
        {
            controller.ApplyJobs([Job("J1", rows: 5, cols: 5)]);
            controller.Start();
            for (int i = 0; i < 25; i++)
                controller.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.Equal(first.Defects, second.Defects);
        Assert.All(first.Defects, d => Assert.InRange(d.Row, 1, 5));
    }

    [Fact]
    public void Start_BlockedByHighLevelAlarm()
    {
        var events = new InMemoryEventSource();
        var alarms = new InMemoryAlarmCenter(events);
        var (controller, _) = Create(alarms);

        alarms.Raise(new SystemAlarm("ALM-1", AlarmSeverity.High, "腔体真空度超限", DateTimeOffset.Now));
        Assert.True(alarms.IsRunBlocked);

        controller.ApplyJobs([Job("J1")]);
        var ex = Assert.Throws<RunBlockedException>(() => controller.Start());
        Assert.Contains("腔体真空度超限", ex.Message); // BR-74：高等级未处理禁止开始运行
    }

    [Fact]
    public void AlarmCenter_LowAlarmClearedWithBuzzer_HighRequiresManualHandling()
    {
        var events = new InMemoryEventSource();
        var alarms = new InMemoryAlarmCenter(events);

        alarms.Raise(new SystemAlarm("ALM-L", AlarmSeverity.Low, "配方版本已更新", DateTimeOffset.Now));
        alarms.Raise(new SystemAlarm("ALM-H", AlarmSeverity.High, "检测软件启动失败", DateTimeOffset.Now));
        Assert.Equal(2, alarms.ActiveAlarms.Count);

        // BR-74：蜂鸣器解除清除低等级，保留高等级。
        Assert.Equal(1, alarms.ClearBuzzer());
        Assert.Equal("ALM-H", alarms.ActiveAlarms.Single().AlarmId);
        Assert.True(alarms.IsRunBlocked);

        // BR-75：高等级必须手动处理。
        Assert.True(alarms.Handle("ALM-H"));
        Assert.False(alarms.IsRunBlocked);
        Assert.Null(alarms.BlockReason);
        Assert.False(alarms.Handle("ALM-H"));
    }

    [Fact]
    public void EventSource_ReceivesBothRunAndAlarmEvents()
    {
        var events = new InMemoryEventSource();
        var alarms = new InMemoryAlarmCenter(events);

        var kinds = new List<string>();
        events.EventPublished += (_, e) => kinds.Add(e.Kind);

        alarms.Raise(new SystemAlarm("ALM-L", AlarmSeverity.Low, "提示", DateTimeOffset.Now));
        alarms.ClearBuzzer();
        var controller = new WaferRunController(events, alarms);
        controller.ApplyJobs([Job("J1")]);
        controller.Start();

        // AC-H09-05：报警与运行日志来自同一事件源。
        Assert.Contains("LowAlarm", kinds);
        Assert.Contains("BuzzerClear", kinds);
        Assert.Contains("Run", kinds);
    }

    [Fact]
    public void SelfCheck_HighlightsFailuresAndStaysOnPage()
    {
        var service = new SelfCheckService();
        var items = service.Run();
        Assert.Equal(8, items.Count);
        Assert.False(SelfCheckService.HasFailure(items));

        var failed = new List<SelfCheckItem> { new("检测相机", false, "通信超时", true) };
        Assert.True(SelfCheckService.HasFailure(failed)); // BR-77：停留当前页
    }

    [Fact]
    public void LogTime_UsesLegacyFormat()
    {
        var stamp = new DateTimeOffset(2026, 11, 9, 10, 41, 26, TimeSpan.Zero);
        Assert.Equal("11-09 10:41:26", LogFormat.LogTime(stamp)); // BR-18
    }

    [Fact]
    public void CoreHost_ResolvesRunServices()
    {
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services => services.AddWaferInspectCore())
            .Build();

        var controller = host.Services.GetRequiredService<WaferRunController>();
        Assert.Equal(RunPhase.Idle, controller.Phase);
        Assert.NotNull(host.Services.GetRequiredService<IAlarmCenter>());
        Assert.NotNull(host.Services.GetRequiredService<SelfCheckService>());
    }
}
