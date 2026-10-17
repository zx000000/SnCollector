using SnCollector.Core.Models;
using SnCollector.Core.Services;

namespace SnCollector.Core.Collectors;

/// <summary>
/// 采集器工厂：按配置的协议类型创建对应的采集器实现。
/// UI 层只通过 <see cref="ICollector"/> 与采集器交互。
/// </summary>
public static class CollectorFactory
{
    public static ICollector Create(ProtocolType protocol) => protocol switch
    {
        ProtocolType.Fins => new CollectorService(),
        ProtocolType.OpcUa => throw new NotSupportedException("OPC UA 采集器正在开发中，敬请期待"),
        _ => throw new ArgumentOutOfRangeException(nameof(protocol), $"未知协议类型: {protocol}"),
    };

    /// <summary>按配置文件的协议类型创建采集器</summary>
    public static ICollector Create(AppConfig config) => Create(config.Protocol);
}
