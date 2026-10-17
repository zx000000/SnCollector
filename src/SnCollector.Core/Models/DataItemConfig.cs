namespace SnCollector.Core.Models;

/// <summary>数据项数据类型</summary>
public enum DataItemType
{
    Ascii,
    Hex,
    Int
}

/// <summary>
/// 一个可配置的数据项（如 SN1、SN2、批次号）。
/// 采集时按此配置从 PLC 读取，界面与数据库的列随之动态生成。
/// </summary>
public class DataItemConfig
{
    /// <summary>数据项名称，如 SN1</summary>
    public string Name { get; set; } = "SN1";

    public DataItemType DataType { get; set; } = DataItemType.Ascii;

    /// <summary>
    /// 数据地址：FINS 协议下为 DM 区字地址（如 D200）；
    /// OPC UA 协议下为节点 NodeId（如 ns=2;s=SN1）。
    /// </summary>
    public string Address { get; set; } = "D200";

    /// <summary>长度（字）。ASCII 按 1 字=2 字符换算。</summary>
    public int LengthWords { get; set; } = 20;
}
