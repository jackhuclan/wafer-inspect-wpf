using System;

namespace WaferInspectHMI.Services;

/// <summary>1 Hz 实时数据节拍源；与 ViewModel 解耦，由通用主机负责启停。</summary>
public interface ILiveDataPump : IDisposable
{
    event EventHandler Tick;

    void Start();

    void Stop();
}
