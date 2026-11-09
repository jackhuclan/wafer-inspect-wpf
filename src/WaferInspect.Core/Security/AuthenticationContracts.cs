namespace WaferInspect.Core.Security;

/// <summary>账号记录；P0 使用内存初始账号，真实账号存储由存量数据迁移提供（【待确认 T-030】）。</summary>
public sealed record AccountRecord(
    string UserId,
    string DisplayName,
    string Role,
    string Password);

/// <summary>认证结果；失败时只给统一原因，不区分「账号不存在」与「密码错误」。</summary>
public sealed record AuthenticationResult(bool Success, string? FailureReason, UserIdentity? User);

/// <summary>账号存储边界；初始账号之外的真实账号来源见【待确认 T-030】存量数据迁移。</summary>
public interface IAccountStore
{
    AccountRecord? Find(string userId);
}

/// <summary>
/// H10 认证边界：把凭据校验从会话管理（<see cref="IAuthorizationService"/>）中分离。
/// 【T-027 已确认 2026-11-09】初始账号 admin / 123456 按历史行为（BR-76）原样保留，
/// 兼容行为已登记 docs/P0-Migration-Status.md；安全加固（哈希存储、强制改密）可在此接口后替换实现，不改变调用方。
/// </summary>
public interface IAuthenticationService
{
    AuthenticationResult Authenticate(string userId, string password);
}

/// <summary>
/// P0 初始账号存储：仅含 BR-76 的历史初始账号 admin / 123456（T-027 已确认原样保留）。
/// 不内置任何其他账号；不把 OP/ME/SE 解释为角色——账号角色统一取 RoleModel 五级角色。
/// </summary>
public sealed class InMemoryAccountStore : IAccountStore
{
    private readonly Dictionary<string, AccountRecord> _accounts = new(StringComparer.Ordinal)
    {
        ["admin"] = new("admin", "系统管理员", RoleModel.Admin, "123456")
    };

    public AccountRecord? Find(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return null;
        return _accounts.TryGetValue(userId, out AccountRecord? account) ? account : null;
    }
}

/// <summary>
/// 默认认证实现：凭据校验 + 统一角色模型展开。
/// 会话仍由 <see cref="AuthorizationService"/> 管理，本类不持有任何会话状态。
/// </summary>
public sealed class AuthenticationService : IAuthenticationService
{
    private readonly IAccountStore _store;

    public AuthenticationService(IAccountStore store)
        => _store = store ?? throw new ArgumentNullException(nameof(store));

    public AuthenticationResult Authenticate(string userId, string password)
    {
        ArgumentNullException.ThrowIfNull(userId);
        ArgumentNullException.ThrowIfNull(password);
        if (string.IsNullOrWhiteSpace(userId))
            return new AuthenticationResult(false, "用户编号不能为空。", null);
        if (string.IsNullOrWhiteSpace(password))
            return new AuthenticationResult(false, "密码不能为空。", null);

        AccountRecord? account = _store.Find(userId);
        if (account is null || !string.Equals(account.Password, password, StringComparison.Ordinal))
            return new AuthenticationResult(false, "用户编号或密码不正确。", null);

        var roles = RoleModel.EffectiveRoles(account.Role);
        var user = new UserIdentity(account.UserId, account.DisplayName, roles, roles);
        return new AuthenticationResult(true, null, user);
    }
}

/// <summary>
/// 应用级登录门禁（AC-P0-04）：未认证不得创建 / 进入主窗口。
/// 主窗口创建前必须调用；门禁只校验「已登录」，不预判任何操作权限（操作级矩阵按 T-018 于 P5 固化）。
/// </summary>
public static class LoginGate
{
    public static void EnsureAuthenticated(IAuthorizationService authorization)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        if (!authorization.IsAuthenticated)
            throw new UnauthorizedAccessException("登录门禁生效：未登录用户不可进入主界面。");
    }
}
