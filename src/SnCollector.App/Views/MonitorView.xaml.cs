using System.Windows.Controls;

namespace SnCollector.App.Views;

public partial class MonitorView : UserControl
{
    public MonitorView()
    {
        InitializeComponent();
        BootTime.Text = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    }
}
