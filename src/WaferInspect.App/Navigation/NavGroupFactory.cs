using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using WaferInspect.Core.Navigation;
using WaferInspectHMI.Models;

namespace WaferInspectHMI.Navigation;

/// <summary>把 Core 层的纯导航配置转换成 WPF 可绑定的运行时导航模型。</summary>
public interface INavGroupFactory
{
    List<NavGroup> Create(string selectedKey);
}

public sealed class NavGroupFactory : INavGroupFactory
{
    private readonly INavigationCatalog _catalog;

    public NavGroupFactory(INavigationCatalog catalog) => _catalog = catalog;

    public List<NavGroup> Create(string selectedKey)
        => _catalog.Groups.Select(group => new NavGroup
        {
            Key = group.Key,
            Title = group.Title,
            IsExpanded = group.DefaultExpanded,
            Items = group.Items.Select(item => new NavItem
            {
                Key = item.Key,
                Label = item.Label,
                Icon = Geometry.Parse(item.IconPath),
                Badge = item.Badge,
                IsSelected = item.Key == selectedKey
            }).ToList()
        }).ToList();
}
