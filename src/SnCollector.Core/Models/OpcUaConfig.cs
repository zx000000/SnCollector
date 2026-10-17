namespace SnCollector.Core.Models;

/// <summary>OPC UA 连接参数</summary>
public class OpcUaConfig
{
    /// <summary>服务器 Endpoint，如 opc.tcp://192.168.250.10:4840</summary>
    public string EndpointUrl { get; set; } = "opc.tcp://192.168.250.10:4840";

    /// <summary>安全策略：None（开发调试） / Basic256Sha256（生产建议）</summary>
    public string SecurityPolicy { get; set; } = "None";

    /// <summary>用户名（匿名访问留空）</summary>
    public string Username { get; set; } = "";

    /// <summary>密码（明文保存在本地配置文件中，请注意保管）</summary>
    public string Password { get; set; } = "";

    /// <summary>发布间隔（ms），Server 向客户端推送数据的周期</summary>
    public int PublishingIntervalMs { get; set; } = 500;

    /// <summary>采样间隔（ms），Server 采样被监视节点的周期</summary>
    public int SamplingIntervalMs { get; set; } = 100;
}
