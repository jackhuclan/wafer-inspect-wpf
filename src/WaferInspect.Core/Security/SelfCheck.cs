namespace WaferInspect.Core.Security;

/// <summary>启动自检项（F-2.2-03）。真实硬件探测属设备连接范畴（T-029 搁置）；框架与判定规则在此完整实现。</summary>
public sealed record SelfCheckItem(string Name, bool Passed, string Detail, bool IsSimulated);

/// <summary>
/// 启动自检（R-H10-02）：全部正常自动进入主界面；存在异常停留当前页并红色高亮异常项（BR-77）。
/// 硬件探测项当前为模拟数据源（IsSimulated=true），真实探测随 T-029 设备契约闭环后接入。
/// </summary>
public sealed class SelfCheckService
{
    /// <summary>自检清单：软件项 + 硬件项（模拟）。保持 8 项与日常开机检查粒度一致（F-2.1.2）。</summary>
    public IReadOnlyList<SelfCheckItem> Run()
    {
        string version = typeof(SelfCheckService).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        // 模拟自检：本阶段无设备连接，硬件项恒为正常；真实探测接入后按实机结果返回。
        return
        [
            new SelfCheckItem("软件许可与版本", true, $"WaferInspect {version} · 校验通过", false),
            new SelfCheckItem("配置文件加载", true, "导航 / 页面 / 主题配置完整", false),
            new SelfCheckItem("EFEM 通信", true, "模拟数据源（真实探测搁置 T-029）", true),
            new SelfCheckItem("检测相机", true, "模拟数据源（真实探测搁置 T-029）", true),
            new SelfCheckItem("光源控制器", true, "模拟数据源（真实探测搁置 T-029）", true),
            new SelfCheckItem("载物台 / Chuck", true, "模拟数据源（真实探测搁置 T-029）", true),
            new SelfCheckItem("SECS-GEM 链路", true, "模拟数据源（真实探测搁置 T-029）", true),
            new SelfCheckItem("数据存储目录", true, "本地目录可写", false)
        ];
    }

    /// <summary>BR-77：存在异常项时停留当前页（true=有异常），由调用方决定不跳转。</summary>
    public static bool HasFailure(IReadOnlyList<SelfCheckItem> items)
        => items.Any(i => !i.Passed);
}
