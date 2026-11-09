using System.Text.RegularExpressions;
using Xunit;
using Xunit.Sdk;

namespace WaferInspect.Core.Tests;

/// <summary>
/// 把测试方法绑定到 PRD 10.5 的 BR 编号；编号只标识契约，不代表未执行的行为已通过。
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class BusinessRuleAttribute : Attribute
{
    public BusinessRuleAttribute(string id)
    {
        if (!Regex.IsMatch(id, @"^BR-(?:0[1-9]|[1-6][0-9]|7[0-8])$"))
            throw new ArgumentOutOfRangeException(nameof(id), id, "必须使用 PRD 中 BR-01~BR-78 的完整编号。");
        Id = id;
    }

    public string Id { get; }
}

public static class BusinessRuleAssert
{
    public static void Equal<T>(string ruleId, T expected, T actual)
    {
        try
        {
            Assert.Equal(expected, actual);
        }
        catch (XunitException ex)
        {
            throw new XunitException($"{ruleId} 业务规则断言失败：{ex.Message}");
        }
    }
}

public sealed class BusinessRuleFrameworkTests
{
    [Fact]
    public void Attribute_AcceptsOnlyDefinedBaselineRange()
    {
        Assert.Equal("BR-78", new BusinessRuleAttribute("BR-78").Id);
        Assert.Throws<ArgumentOutOfRangeException>(() => new BusinessRuleAttribute("BR-79"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BusinessRuleAttribute("78"));
    }

    [Fact]
    public void AssertFailure_CarriesBusinessRuleId()
    {
        BusinessRuleAssert.Equal("BR-18", "MM-DD HH:MM:SS", "MM-DD HH:MM:SS");
        var error = Assert.Throws<XunitException>(() => BusinessRuleAssert.Equal("BR-18", "A", "B"));
        Assert.Contains("BR-18", error.Message);
    }
}
