using System.Windows;
using WaferInspect.Core.Run;

namespace WaferInspectHMI.Views;

/// <summary>查看 Job 弹窗（F-3.1.5-08）；明细字段与主流程来自 PRD 4.1.7 的状态机定义。</summary>
public partial class JobInfoDialog : Window
{
    public JobInfoDialog(JobSelection job)
    {
        InitializeComponent();

        TitleText.Text = $"Job：{job.JobName}";
        JobNameText.Text = job.JobName;
        RecipeText.Text = job.RecipeName;
        WaferSizeText.Text = $"{job.WaferSize:F0} mm";
        WaferCountText.Text = $"{job.WaferCount} 片";
        DieXText.Text = $"{job.DieSizeX:F1} mm";
        DieYText.Text = $"{job.DieSizeY:F1} mm";
        FlowText.Text =
            "① 放置晶圆（手动：上料位 → 开安全门 → 放片 → 关安全门 → 拍照位；或 EFEM 自动上料 4 步）\n" +
            "② 选择 Job（可多选，BR-10 依次执行）→ 应用\n" +
            "③ 确认晶圆信息（可自定义 LotId / WaferId，BR-14）\n" +
            "④ 开始运行 → 在扫描区域内执行检测\n" +
            "⑤ 分支：暂停（扫描中止，可调参）→ 继续（BR-08 从头扫描）/ 停止（BR-09 立即中断）/ 结批（BR-06 扫完当前批次）\n" +
            "⑥ 结束扫描 → 结束";
    }

    private void Close_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
