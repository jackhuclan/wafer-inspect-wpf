namespace WaferInspect.Core.Devices;

/// <summary>PRD DEP-12 的设备类别；不包含协议、地址或供应商私有参数。</summary>
public enum DeviceKind
{
    Efem,
    Acs,
    Camera,
    Light,
    Plc,
    Io,
    SecsGem,
    Ocr
}

/// <summary>一次设备命令的内部统一表达；参数使用显式键值，不向视图暴露协议帧。</summary>
public sealed record DeviceCommand(
    string DeviceId,
    DeviceKind Kind,
    string Name,
    IReadOnlyDictionary<string, string> Arguments);

/// <summary>设备命令结果。Code/Message 原样来自适配器，禁止把失败转换为成功。</summary>
public sealed record DeviceCommandResult(bool Success, string Code, string Message);

/// <summary>
/// 基础设施设备网关。具体协议、地址、数据格式与异常映射必须由设备文档确认后实现。
/// 【待确认 T-029】当前仅定义边界，不提供虚构的生产适配器。
/// </summary>
// 【待确认 T-029】协议、地址、命令与异常契约未提供前，不实现生产网关。
public interface IDeviceGateway
{
    Task<DeviceCommandResult> ExecuteAsync(DeviceCommand command, CancellationToken cancellationToken = default);
}

/// <summary>H06 唯一设备命令层；视图与模块只能调用本层，不能各自拼装设备协议。</summary>
public interface IDeviceCommandLayer
{
    Task<DeviceCommandResult> SendAsync(DeviceCommand command, CancellationToken cancellationToken = default);
}

/// <summary>只做内部契约校验与网关转发，不改变命令参数或适配器结果。</summary>
public sealed class DeviceCommandLayer(IDeviceGateway gateway) : IDeviceCommandLayer
{
    public async Task<DeviceCommandResult> SendAsync(
        DeviceCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.DeviceId))
            throw new ArgumentException("设备编号不能为空。", nameof(command));
        if (string.IsNullOrWhiteSpace(command.Name))
            throw new ArgumentException("设备命令名称不能为空。", nameof(command));
        ArgumentNullException.ThrowIfNull(command.Arguments);

        return await gateway.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
    }
}
