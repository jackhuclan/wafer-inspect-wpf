namespace WaferInspect.Core.Navigation;

/// <summary>
/// 导航配置的只读访问入口。配置本身仍只定义在 <see cref="NavConfiguration"/>，
/// 该接口让上层通过 DI 消费，而不是直接依赖静态类。
/// </summary>
public interface INavigationCatalog
{
    string DefaultViewKey { get; }

    IReadOnlyList<NavGroupDefinition> Groups { get; }

    string? GroupKeyOf(string itemKey);
}
