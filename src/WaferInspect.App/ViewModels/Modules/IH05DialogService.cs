namespace WaferInspectHMI.ViewModels.Modules;

/// <summary>
/// H05 对话框边界（与 H01/H02/H04 同构）：ViewModel 只表达「需要一次用户交互」，
/// 具体窗口 / 文件选择由 <c>H05DialogService</c> 实现，MapType 规则不依赖 WPF 类型。
/// </summary>
public interface IH05DialogService
{
    /// <summary>BR-45 / F-3.7.2-11：选择图谱文件；取消返回空字符串。</summary>
    string PickMapFile(string currentPath);

    /// <summary>F-3.7.2-02：新增 / 重命名图谱类型名称；取消返回空字符串。</summary>
    string PromptTypeName(string title, string initialName);

    bool Confirm(string title, string message);

    void ShowMessage(string title, string message);
}
