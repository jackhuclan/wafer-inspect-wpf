using System.Windows.Controls;

namespace WaferInspectHMI.Views;

/// <summary>
/// H02 配方与 Job 管理视图：DataContext 为 <c>H02RecipeViewModel</c>（由 MainWindow 绑定），
/// 视图本身不持有业务状态，也不在 code-behind 里 new ViewModel。
/// </summary>
public partial class RecipeView : UserControl
{
    public RecipeView() => InitializeComponent();
}
