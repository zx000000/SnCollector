// ============================================================
// SnSimulator —— Omron FINS/UDP PLC 模拟器（测试用）
// 行为：每隔 N 秒生成随机 SN 写入 DM 区，然后置位触发位；
//      采集端读走数据并复位触发位后，开始下一轮。
// 用法：dotnet run --project src/SnSimulator [-- --port 9600 --interval 5 --verbose]
//      或直接运行 SnSimulator.exe，同目录下 simulator.json 可改配置
// ============================================================
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace SnSimulator;

public class DataAreaConfig
{
    public string Address { get; set; } = "D200";
    public int Words { get; set; } = 20;
}

public class SimConfig
{
    public int ListenPort { get; set; } = 9600;
    public string TriggerAddress { get; set; } = "D100.00";
    public int GenerateIntervalSeconds { get; set; } = 5;
    public string SnPrefix { get; set; } = "SN";
    public int SnLength { get; set; } = 16;
    public List<DataAreaConfig> DataAreas { get; set; } = new()
    {
        new() { Address = "D200", Words = 20 },
        new() { Address = "D220", Words = 20 },
    };
    public bool Verbose { get; set; } = false;

    public static SimConfig Load(string[] args)
    {
        var cfg = new SimConfig();
        var path = Path.Combine(AppContext.BaseDirectory, "simulator.json");
        if (File.Exists(path))
        {
            try { cfg = JsonSerializer.Deserialize<SimConfig>(File.ReadAllText(path)) ?? cfg; }
            catch { }
        }
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--port" when i + 1 < args.Length:
                    cfg.ListenPort = int.Parse(args[++i]); break;
                case "--interval" when i + 1 < args.Length:
                    cfg.GenerateIntervalSeconds = int.Parse(args[++i]); break;
                case "--verbose":
                    cfg.Verbose = true; break;
            }
        }
        if (!File.Exists(path))
            File.WriteAllText(path, JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true }));
        return cfg;
    }
}

/// <summary>模拟的 PLC DM 区内存</summary>
public class PlcMemory
{
    private readonly Dictionary<int, ushort> _dm = new();
    private readonly object _lock = new();

    public ushort ReadWord(int addr)
    {
        lock (_lock) return _dm.TryGetValue(addr, out var v) ? v : (ushort)0;
    }

    public bool ReadBit(int addr, int bit) => (ReadWord(addr) & (1 << bit)) != 0;

    public void WriteBit(int addr, int bit, bool on)
    {
        lock (_lock)
        {
            var w = _dm.TryGetValue(addr, out var v) ? v : (ushort)0;
            w = on ? (ushort)(w | (1 << bit)) : (ushort)(w & ~(1 << bit));
            _dm[addr] = w;
        }
    }

    public void WriteWords(int addr, ushort[] words)
    {
        lock (_lock)
        {
            for (var i = 0; i < words.Length; i++)
                _dm[addr + i] = words[i];
        }
    }

    public static (int addr, int bit) ParseBit(string s)
    {
        s = s.Trim().ToUpperInvariant();
        var rest = s[1..];
        var dot = rest.IndexOf('.');
        return (int.Parse(rest[..dot]), int.Parse(rest[(dot + 1)..]));
    }

    public static int ParseWord(string s) => int.Parse(s.Trim().ToUpperInvariant()[1..]);
}

public sealed class FinsSimulator : IDisposable
{
    private readonly SimConfig _cfg;
    private readonly PlcMemory _mem = new();
    private readonly UdpClient _udp;
    private readonly CancellationTokenSource _cts = new();
    private readonly Random _rand = new();
    private int _seq;
    private bool _disposed;

    public FinsSimulator(SimConfig cfg)
    {
        _cfg = cfg;
        _udp = new UdpClient(cfg.ListenPort);
        // Windows 特有：收到 ICMP 端口不可达时不抛 ConnectionReset，避免服务循环被打死
        if (OperatingSystem.IsWindows())
            _udp.Client.IOControl((IOControlCode)(-1744830452), new byte[] { 0, 0, 0, 0 }, null);
    }

    public async Task RunAsync()
    {
        Console.WriteLine("==============================================");
        Console.WriteLine(" FINS/UDP PLC 模拟器");
        Console.WriteLine($" 监听端口 : {_cfg.ListenPort}");
        Console.WriteLine($" 触发地址 : {_cfg.TriggerAddress}");
        Console.WriteLine($" 生成间隔 : {_cfg.GenerateIntervalSeconds} 秒");
        Console.WriteLine($" 数据区   : {string.Join(", ", _cfg.DataAreas.Select(a => $"{a.Address}({a.Words}字)"))}");
        Console.WriteLine(" 按 Ctrl+C 退出");
        Console.WriteLine("==============================================");
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; _cts.Cancel(); };

        await Task.WhenAll(ServeAsync(_cts.Token), GenerateLoopAsync(_cts.Token));
    }

    // ---- 数据生成循环：触发位 OFF 时才生成新一轮，避免覆盖未读取的数据 ----
    private async Task GenerateLoopAsync(CancellationToken ct)
    {
        var (tAddr, tBit) = PlcMemory.ParseBit(_cfg.TriggerAddress);
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(_cfg.GenerateIntervalSeconds * 1000, ct); }
            catch (OperationCanceledException) { break; }

            if (_mem.ReadBit(tAddr, tBit))
                continue;

            _seq++;
            foreach (var area in _cfg.DataAreas)
            {
                var sn = GenerateSn();
                WriteAscii(area.Address, area.Words, sn);
                Console.WriteLine($"[生成 #{_seq}] {area.Address} <- {sn}");
            }
            _mem.WriteBit(tAddr, tBit, true);
            Console.WriteLine($"[触发 #{_seq}] {_cfg.TriggerAddress} = ON");
        }
    }

    private string GenerateSn()
    {
        var head = $"{_cfg.SnPrefix}{DateTime.Now:yyMMdd}";
        var tailLen = Math.Max(_cfg.SnLength - head.Length, 4);
        var tail = new string(Enumerable.Range(0, tailLen).Select(_ => (char)('0' + _rand.Next(10))).ToArray());
        return head + tail;
    }

    private void WriteAscii(string address, int words, string text)
    {
        var addr = PlcMemory.ParseWord(address);
        var bytes = Encoding.ASCII.GetBytes(text);
        var buf = new byte[words * 2];
        Array.Copy(bytes, buf, Math.Min(bytes.Length, buf.Length));
        var w = new ushort[words];
        for (var i = 0; i < words; i++)
            w[i] = (ushort)((buf[i * 2] << 8) | buf[i * 2 + 1]); // 大端序，与采集端解码对应
        _mem.WriteWords(addr, w);
    }

    // ---- FINS/UDP 服务循环（任何异常都不能打死服务） ----
    private async Task ServeAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult req;
            try { req = await _udp.ReceiveAsync(ct); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Console.WriteLine($"[接收异常，已跳过] {ex.Message}");
                continue;
            }
            _ = Task.Run(() => HandleAsync(req, ct), ct);
        }
    }

    private async Task HandleAsync(UdpReceiveResult req, CancellationToken ct)
    {
        var resp = ProcessRequest(req.Buffer);
        if (resp is null) return;
        var mres = resp[10];
        try { await _udp.SendAsync(resp, req.RemoteEndPoint, ct); }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Console.WriteLine($"[回包异常] {req.RemoteEndPoint}: {ex.Message}");
        }
        if (_cfg.Verbose)
            Console.WriteLine($"[FINS] {req.RemoteEndPoint} -> MRES={mres:X2}");
    }

    /// <summary>纯逻辑：FINS 请求字节 → 响应字节；请求非法时返回 null（不响应）</summary>
    public byte[]? ProcessRequest(byte[] b)
    {
        if (b.Length < 18) return null;
        var mrc = b[10];
        var src = b[11];
        byte mres = 0;
        byte[] data = Array.Empty<byte>();
        try
        {
            if (mrc == 0x01 && src == 0x01) data = HandleRead(b);
            else if (mrc == 0x01 && src == 0x02) HandleWrite(b);
            else mres = 0x04; // 不支持的命令
        }
        catch { mres = 0x04; }

        // 响应头：ICF=C0，网络/节点/单元地址对调，SID 原样返回
        var resp = new byte[12 + data.Length];
        resp[0] = 0xC0; resp[1] = 0x00; resp[2] = b[2];
        resp[3] = b[6]; resp[4] = b[7]; resp[5] = b[8];
        resp[6] = b[3]; resp[7] = b[4]; resp[8] = b[5];
        resp[9] = b[9];
        resp[10] = mres; resp[11] = 0x00;
        Array.Copy(data, 0, resp, 12, data.Length);
        return resp;
    }

    private byte[] HandleRead(byte[] b)
    {
        var area = b[12];
        var addr = (b[13] << 8) | b[14];
        var bit = b[15];
        var count = (b[16] << 8) | b[17];
        if (area == 0x82) // DM 字
        {
            var out_ = new byte[count * 2];
            for (var i = 0; i < count; i++)
            {
                var w = _mem.ReadWord(addr + i);
                out_[i * 2] = (byte)(w >> 8);
                out_[i * 2 + 1] = (byte)(w & 0xFF);
            }
            return out_;
        }
        if (area == 0x02) // DM 位
        {
            var out_ = new byte[count];
            for (var i = 0; i < count; i++)
                out_[i] = _mem.ReadBit(addr, bit) ? (byte)0x01 : (byte)0x00;
            return out_;
        }
        throw new InvalidOperationException("不支持的存储区");
    }

    private void HandleWrite(byte[] b)
    {
        var area = b[12];
        var addr = (b[13] << 8) | b[14];
        var bit = b[15];
        if (area != 0x02 || b.Length < 19)
            throw new InvalidOperationException("不支持的写操作");
        var on = b[18] != 0x00;
        _mem.WriteBit(addr, bit, on);
        var (tAddr, tBit) = PlcMemory.ParseBit(_cfg.TriggerAddress);
        if (addr == tAddr && bit == tBit && !on)
            Console.WriteLine("[复位] 触发位 OFF，客户端已取走数据");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        _udp.Dispose();
        _cts.Dispose();
    }
}

public static class Program
{
    public static async Task Main(string[] args)
    {
        var cfg = SimConfig.Load(args);
        using var sim = new FinsSimulator(cfg);
        await sim.RunAsync();
    }
}
