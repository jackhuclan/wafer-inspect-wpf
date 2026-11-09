namespace WaferInspect.Core.Security;

/// <summary>统一会话身份；角色与操作权限均来自同一账号体系，不建立 Review 第二套身份。</summary>
public sealed record UserIdentity(
    string UserId,
    string DisplayName,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions);

/// <summary>
/// H10 统一鉴权边界。默认拒绝，不允许「未登录即拥有权限」或硬编码全权限角色。
/// OP/ME/SE 与 Review 四角色的合并映射见【待确认 T-017】；
/// 初始账号兼容与改密策略见【待确认 T-027】，本骨架不内置任何账号或密码。
/// </summary>
// 【待确认 T-017】统一角色映射未确认，不在此处固化 OP/ME/SE 与 Review 角色的转换。
// 【待确认 T-027】初始账号兼容和强制改密策略未确认，不内置历史默认凭据。
public interface IAuthorizationService
{
    UserIdentity? CurrentUser { get; }

    bool IsAuthenticated { get; }

    event EventHandler? SessionChanged;

    bool HasPermission(string permission);

    void RequirePermission(string permission);

    void EstablishSession(UserIdentity user);

    void ClearSession();
}

/// <summary>仅实现统一会话与显式操作权限判定；认证源和角色映射由后续确认后的适配器提供。</summary>
public sealed class AuthorizationService : IAuthorizationService
{
    public UserIdentity? CurrentUser { get; private set; }

    public bool IsAuthenticated => CurrentUser is not null;

    public event EventHandler? SessionChanged;

    public bool HasPermission(string permission)
    {
        if (string.IsNullOrWhiteSpace(permission))
            throw new ArgumentException("权限标识不能为空。", nameof(permission));
        return CurrentUser?.Permissions.Contains(permission, StringComparer.Ordinal) == true;
    }

    public void RequirePermission(string permission)
    {
        if (!HasPermission(permission))
            throw new UnauthorizedAccessException($"当前会话缺少操作权限：{permission}");
    }

    public void EstablishSession(UserIdentity user)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (string.IsNullOrWhiteSpace(user.UserId))
            throw new ArgumentException("用户编号不能为空。", nameof(user));
        ArgumentNullException.ThrowIfNull(user.Roles);
        ArgumentNullException.ThrowIfNull(user.Permissions);

        CurrentUser = user;
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearSession()
    {
        CurrentUser = null;
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }
}
