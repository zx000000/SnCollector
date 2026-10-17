using SnCollector.App.ViewModels;
using SnCollector.Core.Collectors;
using SnCollector.Core.Storage;
using System.Windows;

namespace SnCollector.App;

public partial class MainWindow : Window
{
    private readonly ICollector _service;
    private readonly MonitorViewModel _monitorVm;
    private readonly QueryViewModel _queryVm;
    private readonly ConfigViewModel _configVm;

    public MainWindow()
    {
        InitializeComponent();
        // 按配置文件中的协议类型创建采集器；main 分支保持原样，此分支为插件化重构
        _service = CollectorFactory.Create(ConfigStore.Load().Protocol);
        _monitorVm = new MonitorViewModel(_service);
        _queryVm = new QueryViewModel();
        _configVm = new ConfigViewModel(_service);
        MonitorViewControl.DataContext = _monitorVm;
        QueryViewControl.DataContext = _queryVm;
        ConfigViewControl.DataContext = _configVm;
        Closed += (_, _) =>
        {
            _ = _service.StopAsync();
            _monitorVm.Dispose();
            _queryVm.Dispose();
            _service.Dispose();
        };
    }
}
