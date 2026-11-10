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
}
