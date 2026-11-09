using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WaferInspect.Core.DependencyInjection;
using WaferInspect.Core.Security;
using Xunit;

namespace WaferInspect.Core.Tests;

/// <summary>
/// T-017 / T-027 闭环后的统一角色模型与认证链路断言（AC-P0-04 登录门禁）。
/// 角色集与继承序按 PRD 3.3 目标态锁定；初始账号按 BR-76 历史行为原样保留。
/// </summary>
public sealed class AuthenticationTests
{
    [Theory]
    [InlineData(new[] { RoleModelKeys.Operator }, RoleModelKeys.Operator)]
    [InlineData(new[] { RoleModelKeys.Operator, RoleModelKeys.Technician }, RoleModelKeys.Technician)]
    [InlineData(new[] { RoleModelKeys.Operator, RoleModelKeys.Technician, RoleModelKeys.Engineer }, RoleModelKeys.Engineer)]
    [InlineData(new[] { RoleModelKeys.Operator, RoleModelKeys.Technician, RoleModelKeys.Engineer, RoleModelKeys.Service }, RoleModelKeys.Service)]
    [InlineData(new[] { RoleModelKeys.Operator, RoleModelKeys.Technician, RoleModelKeys.Engineer, RoleModelKeys.Service, RoleModelKeys.Admin }, RoleModelKeys.Admin)]
    public void RoleModel_ExpandsEffectiveRolesByInheritance(string[] expected, string role)
    {
        Assert.Equal(expected, RoleModel.EffectiveRoles(role));
    }

    [Fact]
    public void RoleModel_AdminInheritsEverything_AndServiceDoesNotInheritAdmin()
    {
        Assert.Contains(RoleModel.Admin, RoleModel.EffectiveRoles(RoleModel.Admin));
        Assert.DoesNotContain(RoleModel.Admin, RoleModel.EffectiveRoles(RoleModel.Service));
        // 能力继承方向：admin ⊇ service ⊇ engineer ⊇ technician ⊇ operator
        Assert.Subset(
            RoleModel.EffectiveRoles(RoleModel.Admin).ToHashSet(),
            RoleModel.EffectiveRoles(RoleModel.Service).ToHashSet());
        Assert.Subset(
            RoleModel.EffectiveRoles(RoleModel.Engineer).ToHashSet(),
            RoleModel.EffectiveRoles(RoleModel.Operator).ToHashSet());
    }

    [Fact]
    public void RoleModel_RejectsUnknownRoles()
    {
        Assert.Throws<ArgumentException>(() => RoleModel.EffectiveRoles("root"));
        Assert.Throws<ArgumentException>(() => RoleModel.DisplayNameOf("root"));
    }

    [Fact]
    public void Authenticate_WithInitialAccountEstablishesAdminSession()
    {
        var authentication = new AuthenticationService(new InMemoryAccountStore());
        var authorization = new AuthorizationService();

        var result = authentication.Authenticate("admin", "123456");

        Assert.True(result.Success);
        Assert.NotNull(result.User);
        authorization.EstablishSession(result.User!);
        Assert.True(authorization.IsAuthenticated);
        // admin 会话携带五级角色；权限集 = 有效角色集，操作级矩阵按 T-018 于 P5 固化
        Assert.Equal(RoleModel.EffectiveRoles(RoleModel.Admin), result.User!.Roles);
        Assert.Equal("系统管理员", result.User.DisplayName);
    }

    [Fact]
    public void Authenticate_FailsWithoutLeakingWhichPartWasWrong()
    {
        var authentication = new AuthenticationService(new InMemoryAccountStore());

        Assert.False(authentication.Authenticate("admin", "wrong").Success);
        Assert.False(authentication.Authenticate("nobody", "123456").Success);
        Assert.Equal("用户编号或密码不正确。", authentication.Authenticate("admin", "wrong").FailureReason);
        Assert.Equal("用户编号或密码不正确。", authentication.Authenticate("nobody", "123456").FailureReason);
        Assert.Throws<ArgumentNullException>(() => authentication.Authenticate(null!, "123456"));
        Assert.Throws<ArgumentNullException>(() => authentication.Authenticate("admin", null!));
    }

    [Fact]
    [BusinessRule("BR-76")]
    public void InitialAccount_Admin123456_IsPreservedAsHistoricalCompatibility()
    {
        // T-027 已确认（2026-11-09）：初始账号按历史行为（BR-76）原样保留，兼容行为登记于 P0 状态文档。
        var authentication = new AuthenticationService(new InMemoryAccountStore());
        var result = authentication.Authenticate("admin", "123456");

        Assert.True(result.Success);
        Assert.Equal(RoleModel.Admin, result.User!.Roles[^1]);
    }

    [Fact]
    public void LoginGate_BlocksUnauthenticatedAndAllowsAuthenticated()
    {
        var authorization = new AuthorizationService();
        Assert.Throws<UnauthorizedAccessException>(
            () => LoginGate.EnsureAuthenticated(authorization));

        authorization.EstablishSession(new UserIdentity("admin", "系统管理员",
            RoleModel.EffectiveRoles(RoleModel.Admin), RoleModel.EffectiveRoles(RoleModel.Admin)));
        LoginGate.EnsureAuthenticated(authorization);
    }

    [Fact]
    public void CoreHost_ResolvesAuthenticationContracts()
    {
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services => services.AddWaferInspectCore())
            .Build();

        var authentication = host.Services.GetRequiredService<IAuthenticationService>();
        Assert.Same(authentication, host.Services.GetRequiredService<IAuthenticationService>());

        var result = authentication.Authenticate("admin", "123456");
        Assert.True(result.Success);
    }
}

/// <summary>常量别名，供 Theory InlineData 使用（属性参数须为编译期常量）。</summary>
public static class RoleModelKeys
{
    public const string Operator = RoleModel.Operator;
    public const string Technician = RoleModel.Technician;
    public const string Engineer = RoleModel.Engineer;
    public const string Service = RoleModel.Service;
    public const string Admin = RoleModel.Admin;
}
