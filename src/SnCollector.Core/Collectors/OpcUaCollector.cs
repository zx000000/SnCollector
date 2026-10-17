using Opc.Ua;
using Opc.Ua.Client;
using SnCollector.Core.Models;
using SnCollector.Core.Storage;

namespace SnCollector.Core.Collectors;

/// <summary>
/// OPC UA 协议采集器：订阅触发节点，上升沿时读取各数据项节点，入库并抛事件。
/// 实现 <see cref="ICollector"/> 接口，与 <see cref="Services.CollectorService"/>(FINS) 可互换。
///
/// 映射关系（与 FINS 版对应）：
/// - FINS 轮询触发位      → OPC UA 订阅触发节点（MonitoredItem + DataChangeNotification）
/// - FINS 读 DM 区地址     → OPC UA 读变量节点（NodeId，如 ns=2;s=SN1）
/// - FINS 写回复位         → OPC UA 写触发节点 false（AutoReset）
/// </summary>
public sealed class OpcUaCollector : ICollector
{
    public string ProtocolName => "OPC UA";
    public string ServerAddress => Config.OpcUa.EndpointUrl;

    public AppConfig Config { get; private set; } = ConfigStore.Load();

    public bool IsRunning => _loopTask is { IsCompleted: false };
    public bool IsConnected { get; private set; }
    public DateTime StartedAt { get; private set; }
    public DateTime? LastReceiveAt { get; private set; }
    public int SessionCount { get; private set; }

    public event Action<SnRecord>? RecordReceived;
    public event Action<bool>? ConnectionChanged;
    public event Action<string>? ErrorOccurred;

    private readonly RecordStore _store = new();
    private readonly SemaphoreSlim _collectGate = new(1, 1);
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _reconnectCts;
    private Task? _loopTask;
    private Session? _session;
    private Subscription? _subscription;
    private bool _wasTriggered;
    private bool _disposed;

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
        _cts = new CancellationTokenSource();
        StartedAt = DateTime.Now;
        SessionCount = 0;
        _wasTriggered = false;
        _loopTask = Task.Run(() => RunAsync(_cts.Token));
    }

    public async Task StopAsync()
    {
        if (_cts is not null)
        {
            await _cts.CancelAsync().ConfigureAwait(false);
            _cts.Dispose();
            _cts = null;
        }
        if (_loopTask is not null)
        {
            try { await _loopTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
            _loopTask = null;
        }
        CleanupSession();
        SetConnected(false);
    }

    /// <summary>主循环：连接→订阅→等待，故障或断开时 5 秒后重连</summary>
    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            _reconnectCts = new CancellationTokenSource();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _reconnectCts.Token);
            try
            {
                await ConnectAndSubscribeAsync(linked.Token).ConfigureAwait(false);
                SetConnected(true);
                // 订阅回调在后台推送数据，这里只需等待（取消=停止或会话故障）
                await Task.Delay(Timeout.Infinite, linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 正常停止，或 KeepAlive 判定会话故障触发的重连
            }
            catch (Exception ex)
            {
                SetConnected(false);
                ErrorOccurred?.Invoke($"OPC UA 连接失败: {ex.Message}，5 秒后重试");
            }
            finally
            {
                CleanupSession();
                _reconnectCts?.Dispose();
                _reconnectCts = null;
            }

            if (ct.IsCancellationRequested) break;
            try { await Task.Delay(5000, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
        SetConnected(false);
    }

    /// <summary>建会话、建订阅、订阅触发节点</summary>
    private async Task ConnectAndSubscribeAsync(CancellationToken ct)
    {
        var cfg = Config.OpcUa;
        var appConfig = await CreateApplicationConfigurationAsync().ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        var endpointDesc = SelectEndpoint(cfg.EndpointUrl, cfg.SecurityPolicy);
        var endpoint = new ConfiguredEndpoint(null, endpointDesc, EndpointConfiguration.Create(appConfig));

        IUserIdentity identity = string.IsNullOrWhiteSpace(cfg.Username)
            ? new UserIdentity(new AnonymousIdentityToken())
            : new UserIdentity(cfg.Username, cfg.Password ?? string.Empty);

        _session = await Session.Create(
            appConfig, endpoint, updateBeforeConnect: false,
            sessionName: "SnCollector", sessionTimeout: 60000, identity, preferredLocales: null)
            .ConfigureAwait(false);
        _session.KeepAlive += OnKeepAlive;
        ct.ThrowIfCancellationRequested();

        _subscription = new Subscription(_session.DefaultSubscription)
        {
            PublishingInterval = cfg.PublishingIntervalMs,
            DisplayName = "SnCollector"
        };
        _session.AddSubscription(_subscription);
        _subscription.Create();

        var triggerNodeId = NodeId.Parse(Config.Trigger.TriggerAddress);
        var triggerItem = new MonitoredItem(_subscription.DefaultItem)
        {
            StartNodeId = triggerNodeId,
            AttributeId = Attributes.Value,
            SamplingInterval = cfg.SamplingIntervalMs,
            DisplayName = "Trigger",
            DiscardOldest = true,
            QueueSize = 1
        };
        triggerItem.Notification += OnTriggerNotification;
        _subscription.AddItem(triggerItem);
        _subscription.ApplyChanges();
    }

    /// <summary>会话保活：Server 状态异常时触发重连</summary>
    private void OnKeepAlive(ISession session, KeepAliveEventArgs e)
    {
        try
        {
            if (e.Status != null && ServiceResult.IsBad(e.Status))
                _reconnectCts?.Cancel();
        }
        catch { /* 保活回调不抛异常 */ }
    }

    /// <summary>触发节点数据变化：上升沿时采集一次</summary>
    private void OnTriggerNotification(MonitoredItem item, MonitoredItemNotificationEventArgs e)
    {
        foreach (var dataValue in item.DequeueValues())
        {
            bool on = IsTriggered(dataValue.Value);
            if (on && !_wasTriggered)
            {
                // 订阅回调线程：抛到后台采集，避免阻塞回调
                _ = CollectOnceAsync();
            }
            _wasTriggered = on;
        }
    }

    /// <summary>触发值兼容：bool / 各类整型非零即视为触发</summary>
    private static bool IsTriggered(object? value) => value switch
    {
        bool b => b,
        byte x => x != 0,
        sbyte x => x != 0,
        ushort x => x != 0,
        short x => x != 0,
        uint x => x != 0,
        int x => x != 0,
        ulong x => x != 0,
        long x => x != 0,
        _ => false
    };

    /// <summary>采集一次：按数据项配置读取各节点，入库并抛事件</summary>
    private async Task CollectOnceAsync()
    {
        var session = _session;
        if (session is null) return;
        // 0 超时：上一次还没采完就丢弃本次，避免触发频繁时堆积
        if (!await _collectGate.WaitAsync(0).ConfigureAwait(false)) return;
        try
        {
            var cfg = Config;
            var record = new SnRecord { Timestamp = DateTime.Now };
            bool ok = true;
            var items = cfg.Items;

            var nodesToRead = items.Select(i => new ReadValueId
            {
                NodeId = NodeId.Parse(i.Address),
                AttributeId = Attributes.Value
            }).ToList();

            // Session.Read 是同步阻塞调用，扔到线程池避免卡住订阅回调
            DataValueCollection results = await Task.Run(() =>
            {
                session.Read(requestHeader: null, maxAge: 0, TimestampsToReturn.Neither,
                    nodesToRead, out var r, out _);
                return r;
            }).ConfigureAwait(false);

            for (int k = 0; k < items.Count; k++)
            {
                try
                {
                    var dv = results[k];
                    if (StatusCode.IsBad(dv.StatusCode))
                        throw new Exception(dv.StatusCode.ToString());
                    record.Values[items[k].Name] = ConvertValue(dv.Value, items[k].DataType);
                }
                catch
                {
                    record.Values[items[k].Name] = "<读取失败>";
                    ok = false;
                }
            }

            record.IsOk = ok;
            record.Id = _store.Insert(record);
            SessionCount++;
            LastReceiveAt = record.Timestamp;
            RecordReceived?.Invoke(record);

            // 自动复位：把触发节点写回 false（节点只读时会失败，忽略）
            if (cfg.Trigger.AutoReset)
            {
                try
                {
                    var triggerNodeId = NodeId.Parse(cfg.Trigger.TriggerAddress);
                    await Task.Run(() =>
                    {
                        session.Write(requestHeader: null, new WriteValueCollection
                        {
                            new WriteValue
                            {
                                NodeId = triggerNodeId,
                                AttributeId = Attributes.Value,
                                Value = new DataValue(new Variant(false))
                            }
                        }, out _, out _);
                    }).ConfigureAwait(false);
                }
                catch { /* 触发节点可能只读，忽略复位失败 */ }
            }
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke($"OPC UA 采集失败: {ex.Message}");
        }
        finally
        {
            _collectGate.Release();
        }
    }

    private static string ConvertValue(object? value, DataItemType type)
    {
        if (value is null) return "";
        return type switch
        {
            DataItemType.Ascii => value is byte[] bytes
                ? System.Text.Encoding.ASCII.GetString(bytes).TrimEnd('\0', ' ')
                : value.ToString() ?? "",
            DataItemType.Hex => value is byte[] h
                ? BitConverter.ToString(h).Replace("-", "")
                : value.ToString() ?? "",
            DataItemType.Int => Convert.ToInt64(value).ToString(),
            _ => value.ToString() ?? ""
        };
    }

    public async Task<bool> TestConnectionAsync(AppConfig config)
    {
        Session? session = null;
        try
        {
            var appConfig = await CreateApplicationConfigurationAsync().ConfigureAwait(false);
            var endpointDesc = SelectEndpoint(config.OpcUa.EndpointUrl, config.OpcUa.SecurityPolicy);
            var endpoint = new ConfiguredEndpoint(null, endpointDesc, EndpointConfiguration.Create(appConfig));
            IUserIdentity identity = string.IsNullOrWhiteSpace(config.OpcUa.Username)
                ? new UserIdentity(new AnonymousIdentityToken())
                : new UserIdentity(config.OpcUa.Username, config.OpcUa.Password ?? string.Empty);
            session = await Session.Create(
                appConfig, endpoint, updateBeforeConnect: false,
                sessionName: "SnCollector-Test", sessionTimeout: 15000, identity, preferredLocales: null)
                .ConfigureAwait(false);
            return session.Connected;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (session is not null)
            {
                try { session.Close(); } catch { }
                try { session.Dispose(); } catch { }
            }
        }
    }

    /// <summary>按配置的安全策略挑选 Endpoint</summary>
    private static EndpointDescription SelectEndpoint(string endpointUrl, string securityPolicy)
    {
        bool useSecurity = !string.Equals(securityPolicy, "None", StringComparison.OrdinalIgnoreCase);
        // 先按是否加密做初选，再强制指定安全策略与模式
        var endpoint = CoreClientUtils.SelectEndpoint(endpointUrl, useSecurity, operationTimeout: 15000);
        endpoint.SecurityPolicyUri = securityPolicy switch
        {
            "Basic256Sha256" => SecurityPolicies.Basic256Sha256,
            _ => SecurityPolicies.None
        };
        endpoint.SecurityMode = endpoint.SecurityPolicyUri == SecurityPolicies.None
            ? MessageSecurityMode.None
            : MessageSecurityMode.SignAndEncrypt;
        return endpoint;
    }

    /// <summary>
    /// 构建客户端应用配置（含自签名证书；首次运行自动生成）。
    /// AutoAcceptUntrustedCertificates=true：开发调试便利，生产环境建议关闭并手动信任 Server 证书。
    /// </summary>
    private static async Task<ApplicationConfiguration> CreateApplicationConfigurationAsync()
    {
        var config = new ApplicationConfiguration
        {
            ApplicationName = "SnCollector",
            ApplicationUri = $"urn:{Environment.MachineName}:SnCollector:OpcUaClient",
            ApplicationType = ApplicationType.Client,
            SecurityConfiguration = new SecurityConfiguration
            {
                ApplicationCertificate = new CertificateIdentifier
                {
                    StoreType = "Directory",
                    StorePath = "%LocalApplicationData%/SnCollector/pki/own",
                    SubjectName = "SnCollector OPC UA Client"
                },
                TrustedPeerCertificates = new CertificateTrustList
                {
                    StoreType = "Directory",
                    StorePath = "%LocalApplicationData%/SnCollector/pki/trusted"
                },
                RejectedCertificateStore = new CertificateTrustList
                {
                    StoreType = "Directory",
                    StorePath = "%LocalApplicationData%/SnCollector/pki/rejected"
                },
                AutoAcceptUntrustedCertificates = true,
                AddAppCertToTrustedStore = true
            },
            TransportQuotas = new TransportQuotas { OperationTimeout = 15000 },
            ClientConfiguration = new ClientConfiguration { DefaultSessionTimeout = 60000 }
        };

        await config.ValidateAsync(ApplicationType.Client).ConfigureAwait(false);
        bool haveCert = await config.CheckApplicationInstanceCertificateAsync(silent: false, keySize: 2048).ConfigureAwait(false);
        if (!haveCert)
            throw new Exception("无法创建 OPC UA 应用证书");
        return config;
    }

    private void SetConnected(bool value)
    {
        if (IsConnected == value) return;
        IsConnected = value;
        ConnectionChanged?.Invoke(value);
    }

    private void CleanupSession()
    {
        var subscription = _subscription;
        _subscription = null;
        var session = _session;
        _session = null;

        if (subscription is not null)
        {
            try
            {
                if (session is not null)
                {
                    try { session.RemoveSubscription(subscription); } catch { }
                }
                subscription.Dispose();
            }
            catch { }
        }
        if (session is not null)
        {
            try { session.KeepAlive -= OnKeepAlive; } catch { }
            try { session.Close(); } catch { }
            try { session.Dispose(); } catch { }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel();
        CleanupSession();
        _store.Dispose();
        _cts?.Dispose();
        _reconnectCts?.Dispose();
        _collectGate.Dispose();
    }
}
