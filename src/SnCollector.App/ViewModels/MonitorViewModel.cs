using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SnCollector.Core.Models;
using SnCollector.Core.Services;
using SnCollector.Core.Storage;
using System.Data;
using System.Windows;
using System.Windows.Threading;

namespace SnCollector.App.ViewModels;

/// <summary>运行监视页：连接状态、运行信息、实时数据列表（列随数据项配置动态生成）</summary>
public partial class MonitorViewModel : ObservableObject, IDisposable
{
    private readonly CollectorService _service;
    private readonly RecordStore _store = new();
    private readonly DispatcherTimer _uiTimer;
    private DataTable _table = new();
    private bool _disposed;

    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private string _plcAddress = "";
    [ObservableProperty] private string _todayCount = "0";
    [ObservableProperty] private string _totalCount = "0";
    [ObservableProperty] private string _upTime = "--:--:--";
    [ObservableProperty] private string _lastReceive = "--";
    [ObservableProperty] private bool _isPaused;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _lastError = "";
    [ObservableProperty] private DataView? _recordsView;

    public MonitorViewModel(CollectorService service)
    {
        _service = service;
        RebuildColumns();
        _service.RecordReceived += OnRecordReceived;
        _service.ConnectionChanged += c =>
        {
            IsConnected = c;
            if (c) LastError = "";
        };
        _service.ErrorOccurred += OnErrorOccurred;
        PlcAddress = $"{service.Config.Plc.IpAddress}:{service.Config.Plc.PlcPort}";

        _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _uiTimer.Tick += (_, _) => RefreshStats();
        _uiTimer.Start();
        RefreshStats();
    }

    /// <summary>配置变更后重建列</summary>
    public void RebuildColumns()
    {
        var t = new DataTable();
        t.Columns.Add("序号", typeof(long));
        t.Columns.Add("接收时间", typeof(string));
        foreach (var item in _service.Config.Items)
            t.Columns.Add(item.Name, typeof(string));
        t.Columns.Add("结果", typeof(string));
        _table = t;
        RecordsView = t.DefaultView;
    }

    private void OnRecordReceived(SnRecord record)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (IsPaused) return;
            var row = _table.NewRow();
            row["序号"] = record.Id;
            row["接收时间"] = record.Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
            foreach (var item in _service.Config.Items)
                row[item.Name] = record.Values.TryGetValue(item.Name, out var v) ? v : "";
            row["结果"] = record.IsOk ? "正常" : "异常";
            _table.Rows.InsertAt(row, 0);
            var maxRows = Math.Clamp(_service.Config.Display.MaxListRows, 1, 2000);
            while (_table.Rows.Count > maxRows)
                _table.Rows.RemoveAt(_table.Rows.Count - 1);
            LastReceive = record.Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
        });
    }

    private void OnErrorOccurred(string msg) =>
        Application.Current.Dispatcher.Invoke(() => LastError = msg);

    private void RefreshStats()
    {
        try
        {
            TodayCount = _store.CountToday().ToString("N0");
            TotalCount = _store.CountAll().ToString("N0");
        }
        catch { }
        if (_service.IsRunning)
        {
            var span = DateTime.Now - _service.StartedAt;
            UpTime = $"{(int)span.TotalHours:D2}:{span.Minutes:D2}:{span.Seconds:D2}";
        }
    }

    [RelayCommand]
    private void StartStop()
    {
        if (_service.IsRunning)
        {
            _ = _service.StopAsync();
            IsRunning = false;
        }
        else
        {
            _service.ReloadConfig();
            RebuildColumns();
            PlcAddress = $"{_service.Config.Plc.IpAddress}:{_service.Config.Plc.PlcPort}";
            _service.Start();
            IsRunning = true;
        }
    }

    [RelayCommand]
    private void TogglePause() => IsPaused = !IsPaused;

    [RelayCommand]
    private void Clear() => _table.Clear();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _uiTimer.Stop();
        _service.RecordReceived -= OnRecordReceived;
        _service.ErrorOccurred -= OnErrorOccurred;
        _store.Dispose();
    }
}
