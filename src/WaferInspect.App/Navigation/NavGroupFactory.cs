using System.Windows.Media;
using WaferInspect.Core.Navigation;
using WaferInspectHMI.Models;

namespace WaferInspectHMI.Navigation;

/// <summary>把 Core 层的纯导航配置转换成 WPF 可绑定的运行时导航模型。</summary>
public static class NavGroupFactory
{
    public static List<NavGroup> Create(string selectedKey = NavConfiguration.DefaultViewKey)
        => NavConfiguration.Groups.Select(group => new NavGroup
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
