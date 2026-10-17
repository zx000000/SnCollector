using SnCollector.Core.Models;

namespace SnCollector.Core.Collectors;

/// <summary>
/// 采集器插件接口：UI 层只依赖此接口，不感知具体协议。
/// FINS / OPC UA 各自实现，互不干扰；新增协议只需实现此接口并在工厂注册。
/// </summary>
public interface ICollector : IDisposable
{
    /// <summary>协议名称，用于界面显示，如 "FINS" / "OPC UA"</summary>
    string ProtocolName { get; }

    /// <summary>
    /// 服务器地址显示串，如 "192.168.250.10:9600" / "opc.tcp://192.168.250.10:4840"。
    /// 运行监视页的"PLC 地址"栏直接显示此值。
    /// </summary>
    string ServerAddress { get; }

    /// <summary>当前生效的配置：启动前为默认/上次保存的配置，Start 时更新</summary>
    AppConfig Config { get; }

    bool IsRunning { get; }
    bool IsConnected { get; }
    DateTime StartedAt { get; }
    DateTime? LastReceiveAt { get; }
    int SessionCount { get; }

    event Action<SnRecord>? RecordReceived;
    event Action<bool>? ConnectionChanged;
    event Action<string>? ErrorOccurred;

    /// <summary>重新从本地加载配置（供配置页保存后刷新）</summary>
    void ReloadConfig();

    /// <summary>保存配置到本地</summary>
    void SaveConfig(AppConfig config);

    /// <summary>按给定配置启动采集</summary>
    void Start(AppConfig config);

    Task StopAsync();

    /// <summary>按给定配置测试连接，不影响当前运行状态</summary>
    Task<bool> TestConnectionAsync(AppConfig config);
}
