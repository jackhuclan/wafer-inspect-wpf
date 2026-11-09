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
        Assert.Equal(12, groups.Sum(group => group.Items.Count));
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
}
