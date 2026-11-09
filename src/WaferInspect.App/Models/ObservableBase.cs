using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;

namespace WaferInspectHMI.Models;

/// <summary>
/// <see cref="ObservableObject"/> 的薄适配层：绝大多数通知由 [ObservableProperty]
/// 生成的 setter 负责；保留公开 <see cref="Raise"/> 仅用于跨对象通知
/// （例如主 ViewModel 要求导航组重算「是否有选中项」这类计算属性）。
/// </summary>
public abstract class ObservableBase : ObservableObject
{
    public void Raise([CallerMemberName] string name = null) => OnPropertyChanged(name);
}
