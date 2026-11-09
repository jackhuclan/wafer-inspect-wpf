using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using WaferInspect.Core.Run;

namespace WaferInspectHMI.Views;

/// <summary>选择 Job 弹窗（F-3.1.5-06 / F-3.1.10-10）：多选，确定后由调用方应用（F-3.1.5-12）。</summary>
public partial class SelectJobDialog : Window
{
    public SelectJobDialog(IReadOnlyList<JobSelection> available, IReadOnlyList<JobSelection> selected)
    {
        InitializeComponent();
        var chosen = selected.Select(j => j.JobName).ToHashSet();
        Options = new ObservableCollection<JobOption>(
            available.Select(j => new JobOption(j) { IsSelected = chosen.Contains(j.JobName) }));
        DataContext = this;
        Loaded += (_, _) =>
        {
            // 为冒烟 / 自动化测试提供稳定定位（JobOption0、JobOption1…）。
            var boxes = FindVisualChildren<CheckBox>(JobList).ToArray();
            for (int i = 0; i < boxes.Length; i++)
                boxes[i].SetValue(AutomationProperties.AutomationIdProperty, $"JobOption{i}");
        };
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed) yield return typed;
            foreach (var descendant in FindVisualChildren<T>(child)) yield return descendant;
        }
    }

    public ObservableCollection<JobOption> Options { get; }

    public IReadOnlyList<JobSelection> SelectedJobs { get; private set; } = [];

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        // 顺序按清单原始顺序（BR-10 依次执行）；不勾选任何 Job 视为取消。
        SelectedJobs = Options.Where(o => o.IsSelected).Select(o => o.Job).ToArray();
        if (SelectedJobs.Count == 0)
        {
            MessageBox.Show(this, "请至少选择一个 Job。", "选择 Job", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

/// <summary>弹窗行：Job 与勾选状态。</summary>
public sealed class JobOption(JobSelection job)
{
    public JobSelection Job { get; } = job;

    public bool IsSelected { get; set; }

    public string WaferSizeText => $"{Job.WaferSize:F0} mm";

    public string DieSizeText => $"{Job.DieSizeX:F1} × {Job.DieSizeY:F1} mm";
}
