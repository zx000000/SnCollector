namespace SnCollector.Core.Models;

/// <summary>PLC 通讯参数</summary>
public class PlcCommConfig
{
    public string IpAddress { get; set; } = "192.168.250.10";
    public int PlcPort { get; set; } = 9600;
    public int LocalPort { get; set; } = 9601;
    public int TimeoutMs { get; set; } = 2000;
}

/// <summary>触发设置</summary>
public class TriggerConfig
{
    /// <summary>
    /// 触发源：FINS 协议下为触发位地址（如 D100.00），PLC 置位后开始一次采集；
    /// OPC UA 协议下为触发节点的 NodeId（如 ns=2;s=Trigger），通过订阅推送触发。
    /// </summary>
    public string TriggerAddress { get; set; } = "D100.00";

    /// <summary>轮询间隔（ms）</summary>
    public int PollIntervalMs { get; set; } = 100;

    /// <summary>读取完成后是否自动复位触发位</summary>
    public bool AutoReset { get; set; } = true;
}

/// <summary>界面显示设置</summary>
public class DisplayConfig
{
    /// <summary>运行监视页实时列表最大行数，超出自动删除最旧行（1~2000）</summary>
    public int MaxListRows { get; set; } = 200;
}

/// <summary>应用总配置，JSON 落盘，下次启动自动加载</summary>
public class AppConfig
{
    /// <summary>采集协议，决定使用哪种采集器实现</summary>
    public ProtocolType Protocol { get; set; } = ProtocolType.Fins;

    public PlcCommConfig Plc { get; set; } = new();
    public OpcUaConfig OpcUa { get; set; } = new();
    public TriggerConfig Trigger { get; set; } = new();
    public DisplayConfig Display { get; set; } = new();
    public List<DataItemConfig> Items { get; set; } = new()
    {
        new DataItemConfig { Name = "SN1", DataType = DataItemType.Ascii, Address = "D200", LengthWords = 20 },
        new DataItemConfig { Name = "SN2", DataType = DataItemType.Ascii, Address = "D220", LengthWords = 20 },
    };
}
