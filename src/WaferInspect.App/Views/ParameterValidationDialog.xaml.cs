using System.ComponentModel;
using System.Windows;
using WaferInspect.Core.Inspection;
using WaferInspectHMI.ViewModels.Modules;

namespace WaferInspectHMI.Views;

/// <summary>
/// 「算法参数调试 · 参数验证」页（F-3.8.1-01~12）。
/// 右上角 × 与页脚「关闭并保存」都走 <c>Closing</c> → <c>SaveOnClose()</c>（BR-40），
/// 保证「关闭后重进参数保持」（AC-H04-03），且不存在「关闭丢弃」或「点保存才保存」的分支。
/// </summary>
public partial class ParameterValidationDialog : Window
{
    private readonly H04ParameterValidationViewModel _vm;

    public ParameterValidationDialog(string recipeId, IRoiScanAreaStore store, IH04DialogService dialogs)
    {
        InitializeComponent();
        _vm = new H04ParameterValidationViewModel(recipeId, store, dialogs);
        DataContext = _vm;
    }

    /// <summary>本次调试最后一条验证结果，供缺陷分析页在关闭后呈现（F-3.8.1-08/09）。</summary>
    public VerificationOutcome LastOutcome => _vm.LastOutcome;

    private void Window_Closing(object sender, CancelEventArgs e) => _vm.SaveOnClose();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
