using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SnCollector.Core.Storage;
using System.Data;
using System.IO;
using System.Text;
using System.Windows;

namespace SnCollector.App.ViewModels;

/// <summary>历史查询页：按时间范围 + 关键字 + 结果筛选</summary>
public partial class QueryViewModel : ObservableObject, IDisposable
{
    private readonly RecordStore _store = new();
    private bool _disposed;

    [ObservableProperty] private DateTime _from = DateTime.Today;
    [ObservableProperty] private DateTime _to = DateTime.Today.AddDays(1).AddSeconds(-1);
    [ObservableProperty] private string _keyword = "";
    [ObservableProperty] private int _resultFilterIndex = 0; // 0全部 1正常 2异常
    [ObservableProperty] private DataView? _resultsView;
    [ObservableProperty] private string _resultInfo = "";
    [ObservableProperty] private bool _isBusy;

    private List<Core.Models.SnRecord> _lastResults = new();

    [RelayCommand]
    private async Task QueryAsync()
    {
        IsBusy = true;
        try
        {
            bool? isOk = ResultFilterIndex switch { 1 => true, 2 => false, _ => null };
            // 结束日期按当天 23:59:59 处理，避免选日期后漏掉当天数据
            var toEnd = To.Date.AddDays(1).AddSeconds(-1);
            _lastResults = await Task.Run(() =>
                _store.Query(From.Date, toEnd, string.IsNullOrWhiteSpace(Keyword) ? null : Keyword, isOk));
            BuildTable();
            ResultInfo = $"共 {_lastResults.Count} 条";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"查询失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void BuildTable()
    {
        var t = new DataTable();
        t.Columns.Add("序号", typeof(long));
        t.Columns.Add("接收时间", typeof(string));
        var names = _lastResults.SelectMany(r => r.Values.Keys).Distinct().ToList();
        foreach (var n in names)
            t.Columns.Add(n, typeof(string));
        t.Columns.Add("结果", typeof(string));
        foreach (var r in _lastResults)
        {
            var row = t.NewRow();
            row["序号"] = r.Id;
            row["接收时间"] = r.Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
            foreach (var n in names)
                row[n] = r.Values.TryGetValue(n, out var v) ? v : "";
            row["结果"] = r.IsOk ? "正常" : "异常";
            t.Rows.Add(row);
        }
        ResultsView = t.DefaultView;
    }

    [RelayCommand]
    private void Export()
    {
        if (_lastResults.Count == 0)
        {
            MessageBox.Show("没有可导出的数据，请先查询。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dlg = new SaveFileDialog
        {
            Filter = "CSV 文件|*.csv",
            FileName = $"SN历史_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var names = _lastResults.SelectMany(r => r.Values.Keys).Distinct().ToList();
            var sb = new StringBuilder();
            sb.AppendLine("序号,接收时间," + string.Join(",", names) + ",结果");
            foreach (var r in _lastResults)
            {
                var vals = names.Select(n => "\"" + (r.Values.TryGetValue(n, out var v) ? v.Replace("\"", "\"\"") : "") + "\"");
                sb.AppendLine($"{r.Id},{r.Timestamp:yyyy-MM-dd HH:mm:ss},{string.Join(",", vals)},{(r.IsOk ? "正常" : "异常")}");
            }
            File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(true));
            MessageBox.Show($"已导出 {_lastResults.Count} 条到：{dlg.FileName}", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"导出失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void Reset()
    {
        From = DateTime.Today;
        To = DateTime.Today.AddDays(1).AddSeconds(-1);
        Keyword = "";
        ResultFilterIndex = 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _store.Dispose();
    }
}
