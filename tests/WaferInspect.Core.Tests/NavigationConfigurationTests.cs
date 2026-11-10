using Xunit;
using Nav = global::WaferInspect.Core.Navigation;

namespace WaferInspect.Core.Tests;

public sealed class NavigationConfigurationTests
{
    [Fact]
    public void Groups_HaveTitlesAndItems()
    {
        var groups = Nav.NavConfiguration.Groups;

        Assert.Equal(4, groups.Count);
        Assert.All(groups, group =>
        {
            Assert.False(string.IsNullOrWhiteSpace(group.Key));
            Assert.False(string.IsNullOrWhiteSpace(group.Title));
            Assert.NotEmpty(group.Items);
        });
        Assert.Equal(14, groups.Sum(group => group.Items.Count));
    }

    [Fact]
    public void DefaultExpanded_IsConfigurablePerGroup()
    {
        var groups = Nav.NavConfiguration.Groups;

        Assert.True(groups.Single(g => g.Key == "workbench").DefaultExpanded);
        Assert.True(groups.Single(g => g.Key == "business").DefaultExpanded);
        Assert.True(groups.Single(g => g.Key == "devices").DefaultExpanded);
        Assert.False(groups.Single(g => g.Key == "system").DefaultExpanded);
    }

    [Fact]
    public void ItemKeys_AreUnique()
    {
        var keys = Nav.NavConfiguration.Groups
            .SelectMany(group => group.Items)
            .Select(item => item.Key)
            .ToArray();

        Assert.Equal(keys.Length, keys.Distinct().Count());
    }

    [Fact]
    public void DefaultView_BelongsToAnExpandedGroup()
    {
        var groupKey = Nav.NavConfiguration.GroupKeyOf(Nav.NavConfiguration.DefaultViewKey);

        Assert.NotNull(groupKey);
        Assert.True(Nav.NavConfiguration.Groups.Single(g => g.Key == groupKey).DefaultExpanded);
    }

    [Fact]
    public void GroupKeyOf_ResolvesEveryItem()
    {
        foreach (var item in Nav.NavConfiguration.Groups.SelectMany(group => group.Items))
        {
            Assert.NotNull(Nav.NavConfiguration.GroupKeyOf(item.Key));
        }
    }

    [Fact]
    public void Pages_MatchAllFourteenNavigationItemsExactly()
    {
        var navigationKeys = Nav.NavConfiguration.Groups
            .SelectMany(group => group.Items)
            .Select(item => item.Key)
            .OrderBy(key => key, StringComparer.Ordinal);
        var pageKeys = Nav.PageConfiguration.Pages
            .Select(page => page.Key)
            .OrderBy(key => key, StringComparer.Ordinal);

        Assert.Equal(navigationKeys, pageKeys);
        Assert.Equal(14, Nav.PageConfiguration.Pages.Count);
        Assert.Equal(14, Nav.PageConfiguration.Pages.Select(page => page.Key).Distinct().Count());
    }

    [Fact]
    public void MigrationPages_PreserveRequiredNamesAndOwners()
    {
        Assert.Equal("检测任务", Nav.PageConfiguration.PageOf("tasks")?.Title);
        Assert.Equal("H01", Nav.PageConfiguration.PageOf("tasks")?.OwnerModule);

        var analysis = Nav.PageConfiguration.PageOf("analysis");
        Assert.Equal("数据分析", analysis?.Title);
        Assert.Equal("H08", analysis?.OwnerModule);
        Assert.True(analysis?.IsMigrationSkeleton);

        var maps = Nav.PageConfiguration.PageOf("maps");
        Assert.Equal("图谱管理", maps?.Title);
        Assert.Equal("H05", maps?.OwnerModule);
        // 1.1.0：H05 MapType 编辑与 Map 渲染已迁移，图谱管理页不再是 P0 骨架。
        Assert.False(maps?.IsMigrationSkeleton);
        Assert.DoesNotContain(Nav.NavConfiguration.Groups.SelectMany(group => group.Items),
            item => item.Label.Contains("Review", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ExistingPages_KeepLegacyTitlesCrumbsAndNonSkeletonState()
    {
        Assert.Equal("运行总览", Nav.PageConfiguration.PageOf("overview")?.Title);
        Assert.Equal("工作台 / 实时监控", Nav.PageConfiguration.PageOf("live")?.Crumb);
        Assert.Equal("系统 / 系统设置", Nav.PageConfiguration.PageOf("settings")?.Crumb);
        Assert.All(Nav.PageConfiguration.Pages.Where(page => page.Key is not "analysis" and not "maps"),
            page => Assert.False(page.IsMigrationSkeleton));
    }
}
