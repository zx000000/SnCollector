using System.Net;
using System.Net.Sockets;
using SnCollector.Core.Models;

namespace SnCollector.Core.Fins;

/// <summary>
/// Omron FINS/UDP 客户端（最小实现：读 DM 字、读位、写位）。
/// 帧格式：10 字节 FINS 头 + 命令。默认 DNA/DA1 等取 0，直连以太网口 PLC 可用。
/// </summary>
public sealed class FinsUdpClient : IDisposable
{
    private readonly UdpClient _udp;
    private readonly IPEndPoint _plcEndPoint;
    private readonly int _timeoutMs;
    private int _sid;
    private bool _disposed;

    public FinsUdpClient(string plcIp, int plcPort = 9600, int localPort = 9601, int timeoutMs = 2000)
    {
        _plcEndPoint = new IPEndPoint(IPAddress.Parse(plcIp), plcPort);
        _udp = new UdpClient(localPort);
        // Windows 特有：收到 ICMP 端口不可达时不抛 ConnectionReset，超时走正常的超时逻辑
        if (OperatingSystem.IsWindows())
            _udp.Client.IOControl((IOControlCode)(-1744830452), new byte[] { 0, 0, 0, 0 }, null);
        _timeoutMs = timeoutMs;
    }

    /// <summary>解析 "D200" / "D100.00" 为（区代码，字地址，位）。</summary>
    public static (byte area, ushort addr, byte bit) ParseAddress(string address, bool forBit)
    {
        address = address.Trim().ToUpperInvariant();
        if (!address.StartsWith('D'))
            throw new ArgumentException($"不支持的存储区地址: {address}（当前仅支持 D 区）");

        var rest = address[1..];
        byte bit = 0;
        var dot = rest.IndexOf('.');
        if (dot >= 0)
        {
            bit = byte.Parse(rest[(dot + 1)..]);
            rest = rest[..dot];
        }
        var addr = ushort.Parse(rest);
        // DM 字: 0x82；DM 位: 0x02
        return (forBit ? (byte)0x02 : (byte)0x82, addr, bit);
    }

    private static byte[] BuildHeader(int sid) =>
        // ICF RSV GCT DNA DA1 DA2 SNA SA1 SA2 SID
        new byte[] { 0x80, 0x00, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, (byte)sid };

    private async Task<byte[]> SendReceiveAsync(byte[] command, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var sid = Interlocked.Increment(ref _sid) & 0xFF;
        var packet = BuildPacket(sid, command);

        await _udp.SendAsync(packet, _plcEndPoint, ct);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_timeoutMs);
        try
        {
            var result = await _udp.ReceiveAsync(cts.Token);
            return ParseResponse(result.Buffer, _timeoutMs, command[0], command[1]);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"FINS 通讯超时（{_timeoutMs}ms），请检查 IP/端口与网络");
        }
    }

    internal static byte[] BuildPacket(int sid, byte[] command) =>
        BuildHeader(sid).Concat(command).ToArray();

    internal static byte[] BuildReadBitCommand(string address)
    {
        var (area, addr, bit) = ParseAddress(address, forBit: true);
        return new byte[]
        {
            0x01, 0x01, area,
            (byte)(addr >> 8), (byte)(addr & 0xFF),
            bit, 0x00, 0x01
        };
    }

    internal static byte[] BuildReadWordCommand(string address, int wordCount)
    {
        var (area, addr, _) = ParseAddress(address, forBit: false);
        return new byte[]
        {
            0x01, 0x01, area,
            (byte)(addr >> 8), (byte)(addr & 0xFF),
            0x00,
            (byte)(wordCount >> 8), (byte)(wordCount & 0xFF)
        };
    }

    internal static byte[] BuildWriteBitCommand(string address, bool value)
    {
        var (area, addr, bit) = ParseAddress(address, forBit: true);
        return new byte[]
        {
            0x01, 0x02, area,
            (byte)(addr >> 8), (byte)(addr & 0xFF),
            bit, 0x00, 0x01, value ? (byte)0x01 : (byte)0x00
        };
    }

    /// <summary>
    /// 解析 FINS/UDP 响应帧，兼容：
    /// 标准（欧姆龙 PLC / 本模拟器）: [10字节头][MRES][SRES][数据...]
    /// HSL Server: MRES 在下标 12（位读时 SRES 缺省，数据从 13 开始；字读时 SRES 在 13，数据从 14 开始）
    /// </summary>
    internal static byte[] ParseResponse(byte[] resp, int timeoutMs, byte reqMrc, byte reqSrc)
    {
        if (resp.Length < 12)
            throw new InvalidOperationException("FINS 响应帧过短");

        // 标准格式
        if (resp[10] == 0x00 && resp[11] == 0x00)
            return resp[12..];

        // HSL 风格：MRES 在 12
        if (resp.Length >= 14 && resp[12] == 0x00)
        {
            // SRES 在 13 且为 0 → 数据从 14 开始；否则 SRES 缺省，数据从 13 开始
            return (resp[13] == 0x00) ? resp[14..] : resp[13..];
        }

        throw new InvalidOperationException($"FINS 返回错误: MRES={resp[10]:X2} SRES={resp[11]:X2}");
    }

    /// <summary>读 DM 区字数据（0101 命令）</summary>
    public async Task<ushort[]> ReadDmAsync(string address, int wordCount, CancellationToken ct = default)
    {
        var data = await SendReceiveAsync(BuildReadWordCommand(address, wordCount), ct);
        if (data.Length < wordCount * 2)
            throw new InvalidOperationException("FINS 读数返回数据不足");
        var words = new ushort[wordCount];
        for (var i = 0; i < wordCount; i++)
            words[i] = (ushort)((data[i * 2] << 8) | data[i * 2 + 1]);
        return words;
    }

    /// <summary>读位状态（0101 命令，位区代码）</summary>
    public async Task<bool> ReadBitAsync(string address, CancellationToken ct = default)
    {
        var data = await SendReceiveAsync(BuildReadBitCommand(address), ct);
        return data.Length > 0 && data[0] != 0x00;
    }

    /// <summary>写位（0102 命令）</summary>
    public async Task WriteBitAsync(string address, bool value, CancellationToken ct = default)
    {
        await SendReceiveAsync(BuildWriteBitCommand(address, value), ct);
    }

    /// <summary>按数据项配置读取并解码为字符串</summary>
    public async Task<string> ReadItemAsync(DataItemConfig item, CancellationToken ct = default)
    {
        var words = await ReadDmAsync(item.Address, item.LengthWords, ct);
        return item.DataType switch
        {
            DataItemType.Ascii => DecodeAscii(words),
            DataItemType.Hex => string.Concat(words.Select(w => w.ToString("X4"))),
            DataItemType.Int => words[0].ToString(),
            _ => DecodeAscii(words),
        };
    }

    private static string DecodeAscii(ushort[] words)
    {
        var bytes = new byte[words.Length * 2];
        for (var i = 0; i < words.Length; i++)
        {
            // FINS 传输为大端序：每字高字节在前
            bytes[i * 2] = (byte)(words[i] >> 8);
            bytes[i * 2 + 1] = (byte)(words[i] & 0xFF);
        }
        return System.Text.Encoding.ASCII.GetString(bytes).Trim('\0', ' ', '\r', '\n', '\t');
    }

    /// <summary>连通性测试：读一个字</summary>
    public Task TestAsync(CancellationToken ct = default) =>
        ReadDmAsync("D0", 1, ct);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _udp.Dispose();
    }
}
