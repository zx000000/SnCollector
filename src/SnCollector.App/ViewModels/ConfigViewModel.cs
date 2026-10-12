using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SnCollector.Core.Models;
using SnCollector.Core.Services;
using System.Collections.ObjectModel;
using System.Windows;

namespace SnCollector.App.ViewModels;

/// <summary>参数配置页：通讯参数 + 触发设置 + 数据项配置表，保存落盘本地</summary>
public partial class ConfigViewModel : ObservableObject
{
    private readonly CollectorService _service;

    [ObservableProperty] private string _plcIp = "";
    [ObservableProperty] private string _plcPort = "";
    [ObservableProperty] private string _localPort = "";
    [ObservableProperty] private string _timeout = "";
    [ObservableProperty] private string _triggerAddress = "";
    [ObservableProperty] private string _pollInterval = "";
    [ObservableProperty] private bool _autoReset;
    [ObservableProperty] private string _maxListRows = "";
    [ObservableProperty] private ObservableCollection<DataItemConfig> _items = new();

    public Array DataTypes => Enum.GetValues(typeof(DataItemType));

    public ConfigViewModel(CollectorService service)
    {
        _service = service;
        LoadFrom(service.Config);
    }

    private void LoadFrom(AppConfig cfg)
    {
        PlcIp = cfg.Plc.IpAddress;
        PlcPort = cfg.Plc.PlcPort.ToString();
        LocalPort = cfg.Plc.LocalPort.ToString();
        Timeout = cfg.Plc.TimeoutMs.ToString();
        TriggerAddress = cfg.Trigger.TriggerAddress;
        PollInterval = cfg.Trigger.PollIntervalMs.ToString();
        AutoReset = cfg.Trigger.AutoReset;
        MaxListRows = cfg.Display.MaxListRows.ToString();
        Items = new ObservableCollection<DataItemConfig>(
            cfg.Items.Select(i => new DataItemConfig
            {
                Name = i.Name, DataType = i.DataType, Address = i.Address, LengthWords = i.LengthWords
            }));
    }

    [RelayCommand]
    private void AddItem()
    {
        var n = Items.Count + 1;
        Items.Add(new DataItemConfig
        {
            Name = $"SN{n}",
            DataType = DataItemType.Ascii,
            Address = $"D{200 + (n - 1) * 20}",
            LengthWords = 20
        });
    }

    [RelayCommand]
    private void DeleteItem(DataItemConfig? item)
    {
        if (item is not null)
            Items.Remove(item);
    }

    [RelayCommand]
    private void Save()
    {
        try
        {
            var cfg = new AppConfig
            {
                Plc = new PlcCommConfig
                {
                    IpAddress = PlcIp.Trim(),
                    PlcPort = int.Parse(PlcPort),
                    LocalPort = int.Parse(LocalPort),
                    TimeoutMs = int.Parse(Timeout)
                },
                Trigger = new TriggerConfig
                {
                    TriggerAddress = TriggerAddress.Trim(),
                    PollIntervalMs = int.Parse(PollInterval),
                    AutoReset = AutoReset
                },
                Display = new DisplayConfig
                {
                    MaxListRows = Math.Clamp(int.Parse(MaxListRows), 1, 2000)
                },
                Items = Items.ToList()
            };
            if (cfg.Items.Count == 0)
            {
                MessageBox.Show("至少保留一个数据项。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _service.SaveConfig(cfg);
            MessageBox.Show("配置已保存，下次启动自动加载。", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"保存失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void ResetDefault()
    {
        LoadFrom(new AppConfig());
    }

    [RelayCommand]
    private async Task TestConnection()
    {
        try
        {
            // 先用当前输入框的值临时测试
            var ok = await _service.TestConnectionAsync();
            MessageBox.Show(ok ? "PLC 连接正常。" : "连接失败，请检查 IP/端口与网络。",
                "测试连接", MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"连接失败：{ex.Message}", "测试连接", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
