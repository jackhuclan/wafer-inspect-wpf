namespace WaferInspect.Core.Navigation;

/// <summary>
/// 页面元数据与历史模块 Owner。只描述导航和归属，不声明业务功能已经迁移完成。
/// </summary>
public sealed record ModulePageDefinition(
    string Key,
    string Title,
    string Crumb,
    string Subtitle,
    string? OwnerModule,
    bool IsMigrationSkeleton = false);

/// <summary>14 个菜单页的唯一元数据源，对应 PRD 5.2。</summary>
public static class PageConfiguration
{
    public static IReadOnlyList<ModulePageDefinition> Pages { get; } =
    [
        new("overview", "运行总览", "工作台 / 运行总览", "产线 A · FAB-03 · 设备与检测任务实时状态", null),
        new("live", "实时监控", "工作台 / 实时监控", "产线 A · 全部检测通道 1 Hz 实时数据流", "H01"),
        new("tasks", "检测任务", "检测业务 / 检测任务", "任务队列、执行进度与检测结果追溯", "H01"),
        new("wafer", "晶圆地图", "检测业务 / 晶圆地图", "多通道晶粒级缺陷密度与坐标定位", "H05"),
        new("defects", "缺陷分析", "检测业务 / 缺陷分析", "缺陷分类、趋势与帕累托分析", "H04"),
        new("analysis", "数据分析", "检测业务 / 数据分析", "历史 Review 功能承载页 · 当前为 P0 导航骨架", "H08", true),
        new("reports", "结果报表", "检测业务 / 结果报表", "检测报告生成、审核与归档", "H08"),
        new("devices", "设备管理", "设备与配置 / 设备管理", "13 台设备 · 12 台在线 · 1 台故障", "H07"),
        new("recipes", "配方管理", "设备与配置 / 配方管理", "检测参数模板、版本与下发状态", "H02"),
        new("calibration", "标定与校验", "设备与配置 / 标定与校验", "量测溯源、标定计划与合规状态", "H06"),
        new("maps", "图谱管理", "设备与配置 / 图谱管理", "MapType 编辑、8 类 Bin 与 Map 图预览 · Wafer 扫描属 P4", "H05"),
        new("alarms", "告警中心", "系统 / 告警中心", "告警分级、确认闭环与事件追溯", "H09"),
        new("users", "用户与权限", "系统 / 用户与权限", "操作员账号、角色与访问授权", "H10"),
        new("settings", "系统设置", "系统 / 系统设置", "运行参数、通知与数据策略", "H07")
    ];

    public static ModulePageDefinition? PageOf(string viewKey)
        => Pages.FirstOrDefault(page => page.Key == viewKey);
}
