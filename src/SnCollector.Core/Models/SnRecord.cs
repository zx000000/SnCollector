namespace SnCollector.Core.Models;

/// <summary>一次采集记录：时间 + 各数据项的值（key 为数据项名称）</summary>
public class SnRecord
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; }
    public Dictionary<string, string> Values { get; set; } = new();
    public bool IsOk { get; set; } = true;
}
