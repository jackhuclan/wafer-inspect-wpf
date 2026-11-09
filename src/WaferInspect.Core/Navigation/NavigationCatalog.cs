namespace WaferInspect.Core.Navigation;

/// <summary>把静态单一配置源包装成可注入的只读目录。</summary>
public sealed class NavigationCatalog : INavigationCatalog
{
    public string DefaultViewKey => NavConfiguration.DefaultViewKey;

    public IReadOnlyList<NavGroupDefinition> Groups => NavConfiguration.Groups;

    public string? GroupKeyOf(string itemKey) => NavConfiguration.GroupKeyOf(itemKey);
}
