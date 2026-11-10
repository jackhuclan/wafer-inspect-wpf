using System;

namespace WaferInspect.Core.Alignment;

/// <summary>
/// 真实设备扫描规范（T-029 相机采集 / T-035 AI 推理）未确认时抛出的诚实异常。用于在依赖闭环前，
/// 阻断任何伪造真实设备扫描结果的尝试（E-08 / E-10：依赖 T-029/T-035 的设备能力必须诚实占位，
/// 绝不注册生产 <c>IDeviceGateway</c>、绝不伪造设备成功 / AI 推理结果）。
/// </summary>
public sealed class DeviceScanNotConfirmedException : NotSupportedException
{
    public DeviceScanNotConfirmedException()
        : base("T-029（相机采集）/ T-035（AI 推理）规范待确认；拒绝伪造真实设备扫描结果。" +
               "请确认规范后实现 DeviceScanExecutor.Execute（经已闭环的 IDeviceGateway 采集真实 FOV + 真实 AI 推理），" +
               "并切换 SelectedScanExecutorKind = Device。") { }
}
