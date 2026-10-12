using SnCollector.App.ViewModels;
using SnCollector.Core.Services;
using System.Windows;

namespace SnCollector.App;

public partial class MainWindow : Window
{
    private readonly CollectorService _service = new();
    private readonly MonitorViewModel _monitorVm;
    private readonly QueryViewModel _queryVm;
    private readonly ConfigViewModel _configVm;

    public MainWindow()
    {
        InitializeComponent();
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
