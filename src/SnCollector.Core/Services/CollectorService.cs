using SnCollector.Core.Collectors;
using SnCollector.Core.Fins;
using SnCollector.Core.Models;
using SnCollector.Core.Storage;

namespace SnCollector.Core.Services;

/// <summary>
/// FINS/UDP 协议采集器：轮询触发位，上升沿/置位时按数据项配置依次读取，入库并抛事件。
/// 实现 <see cref="ICollector"/> 接口，UI 层通过接口与之交互。
/// </summary>
public sealed class CollectorService : ICollector
{
    private readonly RecordStore _store = new();
    private FinsUdpClient? _fins;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    public string ProtocolName => "FINS";
    public string ServerAddress => $"{Config.Plc.IpAddress}:{Config.Plc.PlcPort}";

    public AppConfig Config { get; private set; } = ConfigStore.Load();

    public bool IsRunning => _loopTask is { IsCompleted: false };
    public bool IsConnected { get; private set; }
    public DateTime StartedAt { get; private set; }
    public DateTime? LastReceiveAt { get; private set; }
    public int SessionCount { get; private set; }

    public event Action<SnRecord>? RecordReceived;
    public event Action<bool>? ConnectionChanged;
    public event Action<string>? ErrorOccurred;

    public void ReloadConfig() => Config = ConfigStore.Load();

    public void SaveConfig(AppConfig config)
    {
        ConfigStore.Save(config);
        Config = config;
    }

    public void Start(AppConfig config)
    {
        if (IsRunning) return;
        Config = config;
        var c = config;
        _fins = new FinsUdpClient(c.Plc.IpAddress, c.Plc.PlcPort, c.Plc.LocalPort, c.Plc.TimeoutMs);
        _cts = new CancellationTokenSource();
        StartedAt = DateTime.Now;
        SessionCount = 0;
        _loopTask = Task.Run(() => PollLoopAsync(_cts.Token));
    }

    public async Task StopAsync()
    {
        if (_cts is not null)
        {
            await _cts.CancelAsync();
            _cts.Dispose();
            _cts = null;
        }
        if (_loopTask is not null)
        {
            try { await _loopTask; } catch (OperationCanceledException) { }
            _loopTask = null;
        }
        _fins?.Dispose();
        _fins = null;
        SetConnected(false);
    }

    private void SetConnected(bool value)
    {
        if (IsConnected == value) return;
        IsConnected = value;
        ConnectionChanged?.Invoke(value);
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        var cfg = Config;
        var wasOn = false;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var on = await _fins!.ReadBitAsync(cfg.Trigger.TriggerAddress, ct);
                SetConnected(true);
                if (on && !wasOn)
                {
                    await CollectOnceAsync(ct);
                    if (cfg.Trigger.AutoReset)
                        await _fins.WriteBitAsync(cfg.Trigger.TriggerAddress, false, ct);
                }
                wasOn = on;
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                SetConnected(false);
                ErrorOccurred?.Invoke(ex.Message);
            }
            try { await Task.Delay(cfg.Trigger.PollIntervalMs, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task CollectOnceAsync(CancellationToken ct)
    {
        var cfg = Config;
        var record = new SnRecord { Timestamp = DateTime.Now };
        var ok = true;
        foreach (var item in cfg.Items)
        {
            try
            {
                record.Values[item.Name] = await _fins!.ReadItemAsync(item, ct);
            }
            catch
            {
                record.Values[item.Name] = "<读取失败>";
                ok = false;
            }
        }
        record.IsOk = ok;
        record.Id = _store.Insert(record);
        SessionCount++;
        LastReceiveAt = record.Timestamp;
        RecordReceived?.Invoke(record);
    }

    public async Task<bool> TestConnectionAsync(AppConfig config)
    {
        var c = config;
        try
        {
            using var fins = new FinsUdpClient(c.Plc.IpAddress, c.Plc.PlcPort, c.Plc.LocalPort, c.Plc.TimeoutMs);
            using var cts = new CancellationTokenSource(c.Plc.TimeoutMs + 1000);
            await fins.TestAsync(cts.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _fins?.Dispose();
        _store.Dispose();
        _cts?.Dispose();
    }
}
