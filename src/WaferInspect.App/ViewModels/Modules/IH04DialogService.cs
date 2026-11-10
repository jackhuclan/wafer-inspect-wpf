using WaferInspect.Core.Inspection;

namespace WaferInspectHMI.ViewModels.Modules;

/// <summary>
/// H04 对话框边界（与 H01/H02 同构）：ViewModel 只表达「需要一次用户交互」，
/// 具体窗口 / 文件选择由 <c>H04DialogService</c> 实现，业务规则不依赖 WPF 类型。
/// </summary>
public interface IH04DialogService
{
    /// <summary>F-3.8.1-02：定位本地图片文件夹（选文件夹内任一图片即可定位其目录）；取消返回空字符串。</summary>
    string PickImageFolder(string currentFolder);

    /// <summary>枚举文件夹内的图片文件（不访问设备，真实采集随 T-029 搁置）。</summary>
    IReadOnlyList<string> EnumerateImages(string folder);

    /// <summary>
    /// 打开「算法参数调试 · 参数验证」页（F-3.8.1-02~12）。右上角 × 关闭时自动保存（BR-40）。
    /// 返回本次调试最后一条验证结果；未执行测试时返回 null。
    /// </summary>
    VerificationOutcome ShowParameterValidation(string recipeId);

    void ShowMessage(string title, string message);

    bool Confirm(string title, string message);
}
