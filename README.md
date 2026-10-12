# SN 数据采集终端

接收 Omron PLC 端 SN 信息的上位机小工具（FINS/UDP 协议），WPF + .NET 10。

## 功能

- **运行监视**：PLC 连接状态、运行信息（启动时间/运行时长/最后接收）、实时数据列表（列随数据项配置动态生成）
- **历史查询**：按日期范围 + 关键字 + 结果筛选，支持导出 CSV
- **参数配置**：PLC 通讯参数、触发地址、数据项配置表；保存到本机，下次启动自动加载

## 多 SN / 多数据扩展方式

不需要改代码，在「参数配置 → 数据项配置」里加一行即可：

| 数据项名称 | 数据类型 | 起始地址 | 长度(字) |
|---|---|---|---|
| SN1 | ASCII | D200 | 20 |
| SN2 | ASCII | D220 | 20 |
| 批次号 | ASCII | D240 | 10 |

工作流程：PLC 置位触发地址（如 D100.00）→ 软件按配置表顺序依次读取各数据项 → 组装一条记录 → 存入 SQLite。监视页、查询页的列和历史库字段都随配置动态生成。

## 通讯说明

- 协议：Omron FINS/UDP，默认端口 9600
- 触发：轮询读取触发位（默认 D100.00），上升沿触发一次采集，可配置自动复位
- 地址格式：`D200`（字）、`D100.00`（位），目前支持 D 区

## 运行要求

- Windows 10/11，.NET 10 运行时（或 SDK）
- Visual Studio 2022/2026 打开 `SnCollector.sln` 生成即可运行

## 项目结构

```
src/
  SnCollector.Core/        核心库（协议/存储/服务，与界面无关）
    Fins/FinsUdpClient.cs  FINS/UDP 客户端（读 DM 字/位、写位）
    Models/                AppConfig / DataItemConfig / SnRecord
    Storage/               ConfigStore(JSON) / RecordStore(SQLite)
    Services/              CollectorService（轮询采集服务）
  SnCollector.App/         WPF 界面（MVVM）
    Views/                 运行监视 / 历史查询 / 参数配置
    ViewModels/
```

## 联调测试：SnSimulator 模拟器

没有 PLC 时，用自带的 FINS/UDP 模拟器联调：

```
dotnet run --project src/SnSimulator
```

- 监听 9600 端口，每 5 秒生成随机 SN 写入 D200/D220 并置位 D100.00
- 采集端读走数据并复位触发位后，开始下一轮
- 配置文件 `simulator.json`（端口/间隔/SN 格式/数据区），也支持 `--port`、`--interval`、`--verbose` 参数

## 本地数据位置

- 配置：`%AppData%/SnCollector/config.json`
- 历史：`%AppData%/SnCollector/history.db`（SQLite）
