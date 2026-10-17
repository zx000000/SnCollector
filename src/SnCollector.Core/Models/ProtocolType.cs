namespace SnCollector.Core.Models;

/// <summary>采集协议类型。新增协议时在此追加，并在 <c>CollectorFactory</c> 中注册实现。</summary>
public enum ProtocolType
{
    /// <summary>欧姆龙 FINS/UDP（私有协议）</summary>
    Fins,

    /// <summary>OPC UA（行业标准，开发中）</summary>
    OpcUa
}
