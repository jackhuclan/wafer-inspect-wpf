namespace WaferInspectHMI.ViewModels.Modules;

/// <summary>
/// H02 对话框边界：ViewModel 只表达「需要一次用户交互」，具体窗口由 <c>H02DialogService</c> 实现，
/// 保证参数校验与 CRUD 逻辑不依赖 WPF 类型（与 H01 的 IH01DialogService 同构）。
/// </summary>
public interface IH02DialogService
{
    /// <summary>F-3.2.1-17：选择 Map 文件；取消返回空字符串。</summary>
    string PickMapFile(string currentPath);

    /// <summary>F-3.2.1-03：编辑任务名称；取消返回空字符串。</summary>
    string PromptJobName(string title, string initialName);

    bool Confirm(string title, string message);

    void ShowMessage(string title, string message);

    /// <summary>步骤 4 参考图像：选择本地图像文件；取消返回空字符串（采集属 H06，T-029）。</summary>
    string PickImageFile(string currentPath);

    /// <summary>
    /// 打开子配方七步向导（F-3.2.2，T-042 顺序）；窗口关闭即返回。
    /// </summary>
    /// <param name="parentJobId">向导归属的父 Job。</param>
    void ShowSubRecipeWizard(string parentJobId);
}
