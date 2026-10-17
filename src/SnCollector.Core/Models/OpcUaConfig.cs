namespace SnCollector.Core.Models;

/// <summary>
/// OPC UA 连接参数。第二步实现 OpcUaCollector 时启用，
/// 目前仅做配置模型占位，不参与任何逻辑。
/// </summary>
public class OpcUaConfig
{
    /// <summary>服务器 Endpoint，如 opc.tcp://192.168.250.10:4840</summary>
    public string EndpointUrl { get; set; } = "opc.tcp://192.168.250.10:4840";

    /// <summary>安全策略：None / Basic256Sha256 等。开发调试先用 None，生产用签名+加密。</summary>
    public string SecurityPolicy { get; set; } = "None";

    /// <summary>用户名（匿名访问留空）</summary>
    public string Username { get; set; } = "";

    /// <summary>触发节点 NodeId，如 ns=2;s=Trigger</summary>
    public string TriggerNodeId { get; set; } = "ns=2;s=Trigger";

    /// <summary>发布间隔（ms），Server 向客户端推送数据的周期</summary>
    public int PublishingIntervalMs { get; set; } = 500;

    /// <summary>采样间隔（ms），Server 采样被监视节点的周期</summary>
    public int SamplingIntervalMs { get; set; } = 100;
}
