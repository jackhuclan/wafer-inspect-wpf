using System.Windows;
using WaferInspectHMI.ViewModels.Modules;

namespace WaferInspectHMI.Views;

/// <summary>子配方七步向导窗口（T-042 顺序 / BR-19 可重入 / B-25 门禁）；编排逻辑全在 ViewModel。</summary>
public partial class SubRecipeWizardDialog : Window
{
    private readonly SubRecipeWizardViewModel _vm;

    /// <summary>由 DI 工厂构造（依赖 ISubRecipeRepository / IJobRepository / IMapTypeCatalog / IH02DialogService）。</summary>
    public SubRecipeWizardDialog(SubRecipeWizardViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = _vm;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
