using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SnCollector.Core.Collectors;
using SnCollector.Core.Models;
using System.Collections.ObjectModel;
using System.Windows;

namespace SnCollector.App.ViewModels;

/// <summary>协议下拉框选项</summary>
public record ProtocolChoice(ProtocolType Type, string Name);

/// <summary>参数配置页：通讯参数 + 触发设置 + 数据项配置表，保存落盘本地</summary>
public partial class ConfigViewModel : ObservableObject
{
    private readonly ICollector _service;

    [ObservableProperty] private ProtocolChoice? _selectedProtocol;
    public List<ProtocolChoice> ProtocolChoices { get; } = new()
    {
        new(ProtocolType.Fins, "FINS/UDP"),
        new(ProtocolType.OpcUa, "OPC UA"),
    };
    public bool IsFins => SelectedProtocol?.Type == ProtocolType.Fins;
    public bool IsOpcUa => SelectedProtocol?.Type == ProtocolType.OpcUa;

    partial void OnSelectedProtocolChanged(ProtocolChoice? value)
    {
        OnPropertyChanged(nameof(IsFins));
        OnPropertyChanged(nameof(IsOpcUa));
    }

    [ObservableProperty] private string _plcIp = "";
    [ObservableProperty] private string _plcPort = "";
    [ObservableProperty] private string _localPort = "";
    [ObservableProperty] private string _timeout = "";
    // OPC UA 连接参数
    [ObservableProperty] private string _opcEndpoint = "";
    [ObservableProperty] private string _opcSecurityPolicy = "None";
    public List<string> SecurityPolicyOptions { get; } = new() { "None", "Basic256Sha256" };
    [ObservableProperty] private string _opcUsername = "";
    [ObservableProperty] private string _opcPassword = "";
    [ObservableProperty] private string _opcPublishInterval = "500";
    [ObservableProperty] private string _opcSamplingInterval = "100";

    [ObservableProperty] private string _triggerAddress = "";
    [ObservableProperty] private string _pollInterval = "";
    [ObservableProperty] private bool _autoReset;
    [ObservableProperty] private string _maxListRows = "";
    [ObservableProperty] private ObservableCollection<DataItemConfig> _items = new();

    public Array DataTypes => Enum.GetValues(typeof(DataItemType));

    public ConfigViewModel(ICollector service)
    {
        _service = service;
        LoadFrom(service.Config);
    }

    private void LoadFrom(AppConfig cfg)
    {
        SelectedProtocol = ProtocolChoices.First(p => p.Type == cfg.Protocol);
        PlcIp = cfg.Plc.IpAddress;
        PlcPort = cfg.Plc.PlcPort.ToString();
        LocalPort = cfg.Plc.LocalPort.ToString();
        Timeout = cfg.Plc.TimeoutMs.ToString();
        OpcEndpoint = cfg.OpcUa.EndpointUrl;
        OpcSecurityPolicy = cfg.OpcUa.SecurityPolicy;
        OpcUsername = cfg.OpcUa.Username;
        OpcPassword = cfg.OpcUa.Password;
        OpcPublishInterval = cfg.OpcUa.PublishingIntervalMs.ToString();
        OpcSamplingInterval = cfg.OpcUa.SamplingIntervalMs.ToString();
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

    /// <summary>按当前界面输入框的值组装配置（保存与测试连接共用）</summary>
    private AppConfig BuildConfigFromUi()
    {
        var protocol = SelectedProtocol?.Type ?? ProtocolType.Fins;
        return new AppConfig
        {
            Protocol = protocol,
            Plc = new PlcCommConfig
            {
                IpAddress = PlcIp.Trim(),
                PlcPort = int.Parse(PlcPort),
                LocalPort = int.Parse(LocalPort),
                TimeoutMs = int.Parse(Timeout)
            },
            OpcUa = new OpcUaConfig
            {
                EndpointUrl = OpcEndpoint.Trim(),
                SecurityPolicy = OpcSecurityPolicy,
                Username = OpcUsername.Trim(),
                Password = OpcPassword,
                PublishingIntervalMs = int.Parse(OpcPublishInterval),
                SamplingIntervalMs = int.Parse(OpcSamplingInterval)
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
    }

    [RelayCommand]
    private void Save()
    {
        try
        {
            var cfg = BuildConfigFromUi();
            if (cfg.Items.Count == 0)
            {
                MessageBox.Show("至少保留一个数据项。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var oldProtocol = _service.Config.Protocol;
            _service.SaveConfig(cfg);
            if (oldProtocol != cfg.Protocol)
                MessageBox.Show($"采集协议已切换为 {SelectedProtocol?.Name}，重启应用后生效。",
                    "完成", MessageBoxButton.OK, MessageBoxImage.Information);
            else
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
            // 用当前输入框的值临时测试，不落盘
            var cfg = BuildConfigFromUi();
            var ok = await _service.TestConnectionAsync(cfg);
            var target = cfg.Protocol == ProtocolType.OpcUa ? "OPC UA 服务器" : "PLC";
            MessageBox.Show(ok ? $"{target}连接正常。" : $"连接失败，请检查{target}地址与网络。",
                "测试连接", MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"连接失败：{ex.Message}", "测试连接", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
