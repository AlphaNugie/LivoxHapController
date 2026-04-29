# LivoxHapController

Livox HAP 激光雷达 .NET 控制库与 WPF 配置器 — 基于 Livox-SDK2 通信协议，提供设备发现、连接、配置、查询、点云/IMU/推送数据接收及坐标变换的完整功能。

---

## 一、项目概览

本解决方案包含两个项目：

| 项目 | 目标框架 | 说明 |
|------|---------|------|
| **LivoxHapController** | .NET Framework 4.8 | 基础类库，封装完整的 LiDAR 通信协议与控制逻辑 |
| **LivoxHapController.Core** | .NET 9.0 | 基础类库，封装完整的 LiDAR 通信协议与控制逻辑 |
| **LivoxLidarConfigurator** | .NET 10.0 (WPF) | 图形配置器，提供设备发现、连接、配置、查询、数据监控的可视化界面 |

### 1.1 基础库 LivoxHapController 功能总览

基础库覆盖 LiDAR 设备的完整生命周期管理，核心功能包括：

| 功能类别 | 核心类 | 说明 |
|---------|-------|------|
| 设备发现 | `LidarDiscovery` | UDP 广播搜索网络中的 LiDAR 设备，解析响应获取 SN/IP/端口 |
| UDP 通信 | `UdpCommunicator` | 管理命令/点云/IMU 三个 UDP 端口的监听，区分 ACK 响应与推送消息 |
| 命令控制 | `LidarCommander` | 封装所有参数配置和信息查询命令的发送逻辑 |
| 协议编解码 | `SdkPacketBuilder` / `KeyValueCodec` | 协议包构建（CRC校验）、KeyValue 参数序列化/反序列化 |
| 响应解析 | `AckResponseParser` / `PointCloudParser` | ACK 响应解析、点云/IMU 数据包解析 |
| 上层管理 | `LivoxHapRadar` | 一站式 API，协调各组件完成完整生命周期 |
| 坐标变换 | `CoordinateTransformer` | 安装旋转 + 设备旋转 + 平移的坐标变换 |

### 1.2 配置器 LivoxLidarConfigurator 功能总览

WPF 桌面应用，覆盖 LiDAR 设备的完整操作流程：

| 功能区域 | 说明 |
|---------|------|
| 初始化与发现 | 加载配置文件或直接指定参数初始化、广播搜索 LiDAR 设备 |
| 设备列表 | 展示已发现设备（含连接状态指示灯），支持连接/断开操作 |
| 设备信息 | 显示设备基本信息，提供 7 项一键查询（固件类型/版本/序列号/MAC/工作状态/核心温度/状态码） |
| 扫描控制 | 启动/停止扫描，实时显示扫描状态 |
| 网络配置 | 手动或从配置文件应用主机 IP、各通道端口配置 |
| 参数配置 | 点云数据类型、扫描模式、双发射、IMU 控制、安装姿态、FOV、盲区、加热控制、开机模式 |
| 消息日志 | 实时显示 ACK 响应（含 Return Code 详细描述）、错误信息、推送消息 |
| 点云数据 | 点云/IMU 数据包统计与摘要显示 |
| 设备控制 | 重启设备 |

---

## 二、设备生命周期

典型使用流程如下，每个步骤对应基础库的 API 调用或配置器的 UI 操作：

```
初始化 → 广播发现 → 连接设备 → 网络配置 → 参数配置 → 启动扫描 → 接收数据 → 停止扫描 → 断开连接
```

### 2.1 初始化

初始化 UDP 通信处理器和设备发现服务。支持两种方式：

#### 方式一：从配置文件初始化

加载 JSON 配置文件，初始化内部组件。

| API | 说明 |
|-----|------|
| `LivoxHapRadar.Initialize(configFile)` | 加载配置文件并初始化 |
| `LivoxHapRadar.Initialize(configFile, coordTransParamSet)` | 同时设置坐标变换参数 |

#### 方式二：从 AppConfig 对象初始化（无需配置文件）

直接传入 `AppConfig` 对象，支持可选参数覆盖。适用于不想依赖 JSON 文件的场景。

| API | 说明 |
|-----|------|
| `LivoxHapRadar.Initialize(appConfig, ...)` | 从对象初始化，支持可选参数覆盖 |

**可选覆盖参数：**

| 参数 | 类型 | 说明 |
|------|------|------|
| `coordTransParamSet` | `CoordTransParamSet` | 坐标变换参数集 |
| `masterSdk` | `bool?` | 覆盖 MasterSdk 设置 |
| `walkChangeThres` | `double?` | 覆盖步态切换阈值（HAP+MID360） |
| `lidarIp` | `string` | 覆盖 LiDAR 设备 IP（HAP+MID360） |
| `hostIp` | `string` | 覆盖主机 IP（HAP+MID360） |
| `pointDataPort` | `int?` | 覆盖点云数据端口（HAP+MID360） |

> **内部实现：** 两种初始化方式都通过 `AppConfigBuilder` 构建最终配置，共用 `InitCore()` 方法完成核心初始化，确保行为一致。

### 2.2 广播发现

通过 UDP 广播搜索同一局域网内的 LiDAR 设备。

**工作原理：**
1. 绑定本地 56001 端口，既用于发送广播也用于接收响应
2. 每秒向 `255.255.255.255:56000` 发送空的搜索命令包（cmd_id=0x0000）
3. 扫描仪收到广播后，将响应包发回发送方的源端口（即 56001）
4. 在同一客户端上接收并解析响应，获取设备 SN、IP、端口等信息

> **重要：** 发送与接收必须使用同一个 UdpClient，因为扫描仪会向广播包的源端口回复（协议"跟随策略"）。

| API | 说明 |
|-----|------|
| `LivoxHapRadar.Discover(hostIp)` | 启动广播发现，参数为本机 IP |
| `LidarDiscovery.Start(hostIp)` | 底层 API，单独使用发现服务 |
| `DeviceDiscovered` 事件 | 收到设备响应时触发，参数为 `LidarDeviceInfo` |

**发现事件参数 `LidarDeviceInfo` 包含：**

| 属性 | 说明 |
|------|------|
| `Handle` | 内部分配的设备句柄 |
| `DeviceType` / `DeviceTypeName` | 设备类型（HAP、Mid360 等） |
| `SerialNumberString` | 设备序列号（唯一标识） |
| `LidarIpString` | 雷达 IP 地址 |
| `CommandPort` | 命令端口 |
| `IsConnected` | 是否已连接 |

### 2.3 连接/断开设备

选择已发现的设备进行连接，创建命令控制器。

| API | 说明 |
|-----|------|
| `Connect(deviceInfo)` | 连接指定设备 |
| `ConnectBySerialNumber(sn)` | 通过序列号连接 |
| `ConnectFirst()` | 连接第一个发现的设备 |
| `Disconnect()` | 断开当前设备 |
| `IsConnected` | 是否已连接 |

> **注意：** 连接时会创建 `LidarCommander`，复用 `UdpCommunicator` 的命令端口发送命令，确保命令和 ACK 共用同一端口（协议"跟随策略"要求）。

### 2.4 网络配置（连接后必须首先执行）

告诉雷达将点云数据、IMU 数据、状态推送等发送到哪个主机的哪个端口。**未配置则无法接收数据。**

| API | 说明 |
|-----|------|
| `Configure(hostIp, cmdPort, pointPort, imuPort, pushMsgPort)` | 手动指定所有参数 |
| `ConfigureFromConfig()` | 从配置文件读取参数 |

### 2.5 参数配置

连接设备后，可进行各项参数配置。所有配置命令通过 0x0100 命令发送，使用 KeyValue 编码。

| 配置项 | API | 说明 |
|-------|-----|------|
| 点云数据类型 | `SetPclDataType(val)` | 0=IMU, 1=32位笛卡尔(1mm), 2=16位笛卡尔(10mm) |
| 扫描模式 | `SetScanPattern(val)` | 0=非重复, 1=重复, 2=重复低帧率 |
| 双发射模式 | `SetDualEmit(enable)` | 启用/禁用双发射（提高点云密度） |
| IMU 数据 | `EnableImuData()` / `DisableImuData()` | 启用/禁用 IMU 数据推送 |
| 安装姿态 | `SetInstallAttitude(r,p,y,x,y,z)` | 角度(度) + 偏移(mm) |
| FOV 配置 | `SetFovConfig0/1(...)` | 视场区域参数（0.01度单位） |
| 盲区 | `SetBlindSpot(cm)` | 50-200cm |
| 窗口加热 | `EnableGlassHeat()` / `DisableGlassHeat()` | 自动加热控制 |
| 强制加热 | `StartForcedHeating()` / `StopForcedHeating()` | 强制加热控制 |
| 开机模式 | `SetWorkModeAfterBoot(val)` | 0=默认, 1=正常, 2=唤醒 |
| 设备重启 | `RebootDevice(timeoutMs)` | 延迟重启（100~2000ms） |

### 2.6 查询设备信息

通过 0x0101 查询命令获取设备内部参数，查询结果通过 `DeviceStatusUpdated` 事件返回。

| API | 说明 |
|-----|------|
| `QueryInternalInfo(keys)` | 批量查询多个参数 |
| `QueryFirmwareType()` | 查询固件类型 |
| `QueryFirmwareVersion()` | 查询固件版本 |

**可查询的常用 KeyType：**

| KeyType | 值 | 说明 |
|---------|-----|------|
| `SerialNumber` | 0x8000 | 设备序列号 |
| `ProductInfo` | 0x8001 | 产品信息 |
| `VersionApp` | 0x8002 | 应用固件版本 |
| `VersionLoader` | 0x8003 | 加载器固件版本 |
| `VersionHardware` | 0x8004 | 硬件版本 |
| `MacAddress` | 0x8005 | MAC 地址 |
| `CurrentWorkState` | 0x8006 | 当前工作状态 |
| `CoreTemp` | 0x8007 | 核心温度 |
| `PowerUpCount` | 0x8008 | 上电次数 |
| `StatusCode` | 0x800D | 状态码（故障码） |
| `LidarDiagStatus` | 0x800E | 诊断状态 |
| `LidarFlashStatus` | 0x800F | Flash 存储状态 |
| `FirmwareType` | 0x8010 | 固件类型 |
| `HmsCode` | 0x8011 | HMS 故障码 |

### 2.7 启动/停止扫描

| API | 说明 |
|-----|------|
| `StartScan()` | 启动正常扫描（工作模式设为 0x01） |
| `StopScan()` | 停止扫描，进入待机模式（0x02） |

> **注意：** HAP(TX) 版本不支持休眠状态(0x03)，停止扫描时使用待机模式(0x02)。

### 2.8 接收数据

#### 点云数据

订阅 `PointCloudDataReceived` 事件接收原始 UDP 数据，使用 `PointCloudParser` 解析：

```csharp
radar.PointCloudDataReceived += (sender, rawData) =>
{
    var packet = PointCloudParser.ParsePacket(rawData);
    // packet.CartesianDataPoints — 笛卡尔坐标点列表
    // packet.ImuDataPoints — IMU 数据点列表
    // packet.Header — 包头信息（时间戳、点数、数据类型等）
};
```

**点云数据包头信息：**

| 字段 | 说明 |
|------|------|
| `DotNum` | 本包点数（点云=96, IMU=1） |
| `UdpCnt` | UDP 包计数 |
| `DataType` | 数据类型（Cartesian32Bit/Cartesian16Bit/ImuData） |
| `TimeType` | 时间戳类型（0=无同步源, 1=gPTP 同步） |
| `Timestamp` | UTC 时间 |

#### IMU 数据

订阅 `ImuDataReceived` 事件，或从点云数据包的 `ImuDataPoints` 中获取。每包包含 1 个 IMU 数据点（陀螺仪 3 轴 + 加速度计 3 轴）。

#### 设备推送消息

雷达每 1 秒主动推送工作状态、参数信息等。订阅 `PushMessageReceived` 事件获取原始推送数据，订阅 `DeviceStatusUpdated` 事件获取解析后的查询/推送结果。

### 2.9 ACK 响应与 Return Code

所有命令发送后，雷达会返回 ACK 应答。订阅 `AckResponseReceived` 事件获取。

**Return Code 定义（协议附录）：**

| 名称 | 值 | 描述 |
|------|-----|------|
| `Success` | 0x00 | 执行成功 |
| `Failure` | 0x01 | 执行失败 |
| `NotPermittedNow` | 0x02 | 当前状态不支持 |
| `OutOfRange` | 0x03 | 设置值超出范围 |
| `ParamNotSupported` | 0x20 | 参数不支持 |
| `ParamRebootEffect` | 0x21 | 参数需重启生效 |
| `ParamReadOnly` | 0x22 | 参数只读，不支持写入 |
| `ParamInvalidLength` | 0x23 | 请求参数长度错误，或 ACK 数据包超过最大长度 |
| `ParamKeyNumError` | 0x24 | 参数 key_num 和 key_list 不匹配 |
| `UpgradePubKeyError` | 0x30 | 公钥签名验证错误 |
| `UpgradeDigestError` | 0x31 | 固件摘要签名验证错误 |
| `UpgradeFwTypeError` | 0x32 | 固件类型不匹配 |
| `UpgradeFwOutOfRange` | 0x33 | 固件长度超出范围 |

使用 `ReturnCodeExtensions.GetDescription(byte)` 可获取格式化的返回码描述，如 `"0x20 - 参数不支持"`。配置器中 ACK 和查询结果事件已自动展示 Return Code 详细描述。

---

## 三、配置文件

使用前需准备 JSON 配置文件（默认 `hap_config.json`），放置于程序运行目录：

```json
{
  "master_sdk": false,
  "lidar_log_enable": false,
  "lidar_log_cache_size_MB": 10,
  "lidar_log_path": "./",
  "HAP": {
    "walk_change_thres": 1.0,
    "lidar_net_info": {
      "lidar_ip": ["192.168.1.1xx"]
    },
    "host_net_info": [
      {
        "host_ip": "192.168.1.50",
        "frame_time": 100,
        "cmd_data_port": 56000,
        "push_msg_port": 55000,
        "point_data_port": 57000,
        "imu_data_port": 58000,
        "log_data_port": 56001
      }
    ]
  }
}
```

**配置项说明：**

| 字段 | 说明 |
|------|------|
| `host_ip` | 本机 IP 地址，用于绑定 UDP 监听端口 |
| `frame_time` | 帧速率（毫秒），非重复扫描模式下帧窗口越长细节越丰富 |
| `cmd_data_port` | 命令数据端口（默认 56000） |
| `push_msg_port` | 推送消息端口（默认 55000） |
| `point_data_port` | 点云数据端口（默认 57000） |
| `imu_data_port` | IMU 数据端口（默认 58000） |
| `lidar_ip` | 雷达 IP 列表，用于连接指定设备 |

---

## 四、使用说明

### 4.1 快速开始（完整流程模式 — 推荐）

使用 `LivoxHapQuickStart.StartFull()` 一键启动完整流程：

```csharp
using LivoxHapController.Test;
using LivoxHapController.Models;

// 1. 创建坐标变换参数（可选）
var coordParams = new CoordTransParamSet(
    roll: 0, pitch: 0, yaw: 0,   // 安装角度（度）
    x: 100, y: 200, z: 300       // 安装偏移（毫米）
);

// 2. 启动完整流程（自动：发现→连接→配置→扫描）
LivoxHapQuickStart.StartFull("hap_config.json", coordParams, autoStartScan: true);

// 3. 获取点云数据
CartesianDataPoint[] frame = LivoxHapQuickStart.GetCurrentFrameOfRawPoints();

// 4. 停止
LivoxHapQuickStart.Stop();
```

### 4.2 快速开始（仅监听模式 — 兼容旧版）

若雷达已被其他程序配置好，只需接收点云数据：

```csharp
using LivoxHapController.Test;

// 启动监听（不执行发现和命令控制）
LivoxHapQuickStart.Start("hap_config.json");

// 获取点云数据
CartesianDataPoint[] frame = LivoxHapQuickStart.GetCurrentFrameOfRawPoints();

// 停止
LivoxHapQuickStart.Stop();
```

### 4.3 使用 LivoxHapRadar（上层 API — 推荐）

#### 4.3.1 从配置文件初始化

```csharp
using LivoxHapController.Services;
using LivoxHapController.Models;
using LivoxHapController.Enums;
using LivoxHapController.Services.Parsers;

// 1. 创建管理实例
var radar = new LivoxHapRadar();

// 2. 初始化（从配置文件）
radar.Initialize("hap_config.json", new CoordTransParamSet(0, 15, 0, 100, 0, 500));
```

#### 4.3.2 从 AppConfig 对象初始化（无需配置文件）

```csharp
using LivoxHapController.Config;

// 1. 创建管理实例
var radar = new LivoxHapRadar();

// 2. 初始化（直接指定参数，不依赖配置文件）
radar.Initialize(
    appConfig: new AppConfig(),
    coordTransParamSet: new CoordTransParamSet(0, 15, 0, 100, 0, 500),
    hostIp: "192.168.1.50",
    pointDataPort: 57000
);
```

#### 4.3.3 使用 AppConfigBuilder 精细构建配置

```csharp
using LivoxHapController.Config;

// 通过 Builder 链式调用构建配置，然后初始化
var config = AppConfigBuilder.FromFile("hap_config.json")
    .WithHostIp("192.168.1.100")       // 覆盖主机IP
    .WithPointDataPort(57001)           // 覆盖点云端口
    .WithWalkChangeThreshold(2.0)       // 覆盖步态阈值
    .Build();                           // 构建最终配置

radar.Initialize(config);
```

#### 4.3.4 完整使用流程

```csharp
// （接4.3.1或4.3.2初始化后）

// 3. 订阅事件
radar.DeviceDiscovered += (s, device) =>
    Console.WriteLine($"发现: {device.DeviceTypeName} SN={device.SerialNumberString} IP={device.LidarIpString}");

radar.AckResponseReceived += (s, response) =>
{
    if (response.IsSuccess)
        Console.WriteLine("ACK: 命令执行成功");
    else
        Console.WriteLine($"ACK: 失败 - {ReturnCodeExtensions.GetDescription(response.RetCode)}");
};

radar.DeviceStatusUpdated += (s, info) =>
{
    if (info.IsSuccess)
    {
        foreach (var kv in info.ParamResults)
            Console.WriteLine($"  Key=0x{(ushort)kv.Key:X4}, 解析值={ParseResult(kv)}");
    }
    else
        Console.WriteLine($"查询失败: {ReturnCodeExtensions.GetDescription(info.RetCode)}");
};

radar.PointCloudDataReceived += (s, rawData) =>
{
    var packet = PointCloudParser.ParsePacket(rawData);
    // 处理点云数据...
};

// 4. 发现设备
radar.Discover("192.168.1.50");

// 5. 等待发现后连接
System.Threading.Thread.Sleep(3000);
radar.ConnectFirst();

// 6. 配置设备
radar.ConfigureFromConfig();
radar.SetPclDataType(0x01);
radar.SetScanPattern(0x00);

// 7. 启动扫描
radar.StartScan();

// 8. 查询设备信息
radar.QueryFirmwareType();
radar.QueryInternalInfo(new KeyType[] { KeyType.CurrentWorkState, KeyType.CoreTemp });

// 9. 停止扫描
radar.StopScan();

// 10. 断开连接并释放资源
radar.Dispose();
```

### 4.4 使用 LivoxLidarConfigurator（图形界面）

1. 启动应用程序
2. 选择初始化方式：
   - **文件初始化**：输入配置文件路径，点击"文件初始化"按钮
   - **直接初始化**：输入主机 IP 地址，点击"直接初始化"按钮（无需配置文件，使用默认端口）
3. 点击 **开始发现**，等待设备出现在列表中
4. 选中设备，点击 **连接**
5. 在配置面板中设置参数，在查询区域查询设备信息
6. 点击 **启动扫描** 开始接收点云数据
7. 日志区域实时显示 ACK 响应（含 Return Code 描述）和错误信息
8. 点云区域显示数据包统计和摘要

---

## 五、坐标变换

### 5.1 参数配置

```csharp
using LivoxHapController.Models;

// 创建参数集
var paramSet = new CoordTransParamSet(
    roll: 0,       // 横滚角（度），绕X轴
    pitch: 15,     // 俯仰角（度），绕Y轴
    yaw: 30,       // 偏航角（度），绕Z轴
    x: 100,        // X偏移（毫米）
    y: 0,          // Y偏移（毫米）
    z: 500         // Z偏移（毫米）
);

// 设置设备级旋转
paramSet.UpdateDevicePitch(5.0);   // 设备俯仰角（度）
paramSet.UpdateDeviceYaw(45.0);    // 设备回转角（度）
```

### 5.2 变换执行顺序

**安装旋转 → 设备旋转 → 平移**

数学表达式：`P' = R_device × (R_install × P) + T = (R_device × R_install) × P + T`

### 5.3 使用坐标变换

```csharp
using LivoxHapController.Services;

// 单点变换
double[] result = CoordinateTransformer.TransformPoint(
    x: 1000, y: 500, z: 3000, paramSet
);

// 批量变换（扩展方法）
CartesianDataPoint[] transformed = pointCloud.TransformPoints(paramSet);
```

> **注意：** 使用 `LivoxHapQuickStart` 时，若提供了 `CoordTransParamSet`，点云数据在入缓冲区前会自动进行坐标变换。

---

## 六、多台扫描仪数据区分

当同一子网内存在多台 Livox 扫描仪时，需在接收数据时区分不同设备的数据。

### 6.1 方式一：为每台雷达分配不同端口（推荐）

对每台雷达分别调用 `SetPointCloudHostIp`，将其点云数据发送到不同的主机端口，主机端为每个端口创建独立的 `UdpCommunicator` 实例。

```csharp
// 雷达1：点云发送到57000端口
commander1.SetPointCloudHostIp("192.168.1.50", 57000, 0);
// 雷达2：点云发送到57001端口
commander2.SetPointCloudHostIp("192.168.1.50", 57001, 0);
```

### 6.2 方式二：使用多个 LivoxHapRadar 实例

为每台雷达创建独立的 `LivoxHapRadar` 实例，分别管理各自的生命周期。需确保各实例的 UDP 端口不冲突。

### 6.3 方式三：同一端口，通过远程端点区分

通过 UDP 接收时的远程端点 IP 地址来区分数据来源。需扩展 `UdpCommunicator` 在事件参数中携带远程端点信息。

---

## 七、协议参考

本库基于 Livox HAP 通信协议实现，协议详细信息参见项目内 `Livox-SDK-Communication-Protocol-HAP.md` 文件。

**协议关键参数：**

| 参数 | 值 |
|------|-----|
| 传输层 | UDP |
| 字节序 | 小端序（Little-Endian） |
| 包头校验 | CRC-16/CCITT-FALSE |
| 数据段校验 | CRC-32 |
| 固定包头大小 | 24 字节 |
| 点云每包点数 | 96 |
| 点云发送速率 | 452KHz |

**端口说明：**

| 数据类型 | 方向 | 端口 |
|---------|------|------|
| 设备类型查询（广播发现） | lidar ↔ host | 56000 |
| 雷达信息控制相关 | lidar ↔ host | 56000 |
| 点云数据 | lidar → host | 57000（可配置） |
| IMU 数据 | lidar → host | 58000（可配置） |
| Log | lidar ↔ host | 59000 |

**雷达推送目的端口自动选择策略（跟随策略）：**

雷达信息和控制相关的端口，在没有命令配置的情况下，雷达默认采用跟随策略：上位机发送命令的源 IP 地址和源端口，将作为雷达后续命令发送的目的 IP 地址和目的端口。

---

## 八、项目结构

```
LivoxHapController/
├── Config/                     # 配置管理
│   ├── AppConfig.cs            #   应用配置（单例），Init 内部改用 AppConfigBuilder
│   ├── AppConfigBuilder.cs     #   配置构建器（Builder模式，Fluent API）
│   ├── ConfigLoader.cs         #   配置加载器
│   ├── DeviceConfig.cs         #   设备配置模型
│   ├── HostNetInfo.cs          #   主机网络信息
│   └── NetInfoConfig.cs        #   网络配置模型
├── Enums/                      # 枚举定义
│   ├── CommandType.cs          #   命令类型（协议命令ID）
│   ├── DeviceEnums.cs          #   设备类型/工作模式/扫描模式等枚举
│   ├── DeviceWorkState.cs      #   设备工作状态枚举
│   ├── KeyType.cs              #   参数键类型
│   ├── ReturnCode.cs           #   返回码枚举 + ReturnCodeExtensions 扩展方法
│   ├── FirmwareType.cs         #   固件类型枚举
│   ├── PointCloudDataType.cs   #   点云数据类型
│   └── ...
├── Models/                     # 数据模型
│   ├── CoordTransParamSet.cs   #   坐标变换参数集
│   ├── LidarDeviceInfo.cs      #   设备信息模型
│   ├── DataPoints/             #   点云数据模型
│   │   ├── CartesianDataPoint.cs       # 笛卡尔坐标点
│   │   ├── ImuDataPoint.cs             # IMU 数据点
│   │   ├── PointCloudHeader.cs         # 点云包头
│   │   └── PointCloudDataPacket.cs     # 点云数据包
│   └── NetworkConfig.cs        #   网络配置模型
├── Services/                   # 核心服务
│   ├── CoordinateTransformer.cs #  坐标变换核心
│   ├── CrcCalculator.cs        #   CRC 校验计算
│   ├── KeyValueCodec.cs        #   KeyValue 编解码（含边界检查）
│   ├── LidarCommander.cs       #   命令控制器
│   ├── LidarDiscovery.cs       #   设备发现服务（广播+监听合并）
│   ├── LivoxHapRadar.cs        #   上层管理类（支持文件初始化和对象初始化两种方式）
│   ├── MathUtils.cs            #   数学工具（矩阵运算）
│   ├── SdkPacketBuilder.cs     #   协议包构建器
│   ├── UdpCommunicator.cs      #   UDP 通信处理器（命令端口线程安全）
│   ├── BufferManager.cs        #   缓冲区管理
│   ├── DataBufferService.cs    #   数据缓冲服务
│   └── Parsers/                #   数据解析器
│       ├── AckResponseParser.cs    # ACK 响应解析（含 ret_code 检查）
│       ├── PointCloudParser.cs     # 点云数据解析
│       └── ProtocolParser.cs       # 协议通用解析
├── Utilities/
│   └── ObjectPool.cs           #   对象池
└── Test/                       # 测试/快捷入口
    └── LivoxHapQuickStart.cs   #   快速启动类

LivoxLidarConfigurator/
├── MainWindow.xaml             #   WPF 界面布局（支持文件初始化和直接初始化两种入口）
├── MainWindow.xaml.cs          #   界面逻辑（完整生命周期管理，提取共用初始化方法）
├── App.xaml / App.xaml.cs      #   应用入口
└── LivoxLidarConfigurator.csproj
```

---

## 九、迁移任务完成总结

本项目从 Livox-SDK2 (C++) 迁移核心通信与控制逻辑至 .NET Framework 4.8 平台，完成以下步骤：

### Step 1：合并 KeyTypeSupplement 到 KeyType，修复硬编码键值，补充 CommandType

- **KeyType 枚举合并**：将 `KeyTypeSupplement.cs` 中的所有枚举值合并到 `KeyType.cs`
- **LidarCommander 硬编码修复**：将所有硬编码的键值替换为 `KeyType` 枚举成员
- **CommandType 补充**：新增缺失的命令类型

### Step 2：扩展 UdpCommunicator（命令端口监听 + ACK/推送区分）

- 启用命令端口（56000）和 IMU 端口（58000）的监听
- 新增 `CommandAckReceived` 事件：cmd_type=1 时触发（ACK 应答）
- 新增 `CommandPushReceived` 事件：cmd_type=0 且 sender_type=1 时触发（推送消息）

### Step 3：创建 LidarDeviceInfo 设备信息模型

- 存储设备发现后获取的雷达基本信息

### Step 4：创建 LivoxHapRadar 上层管理类

- 封装完整的 LiDAR 控制生命周期，协调各组件

### Step 5：重构 LivoxHapQuickStart 测试类

- 保留旧版 `Start()` 方法（仅监听模式），新增 `StartFull()` 方法（完整流程模式）

### Bug 修复

| 问题 | 原因 | 修复 |
|------|------|------|
| JSON 反序列化后 HostNetInfo 出现重复元素 | JSON.NET 追加到预填充的 List 而非替换 | 初始化空 List + `EnsureHostNetInfo()` 后置填充 |
| 广播发现无法收到设备响应 | 广播端口(56001)与监听端口(56000)分离 | 合并为单一 UdpClient(56001) 同时发送和接收 |
| 停止扫描无效 | LidarCommander 使用独立 UdpClient 发送，ACK 回到随机端口 | 复用 UdpCommunicator 命令端口，添加线程安全锁 |
| 查询固件类型崩溃 | 0x0101 ACK 偏移缺少 rsvd(2) 字段 | `DecodeAllKeyValuesForQueryAck` 偏移从 3 改为 5 |
| 查询核心温度崩溃 | ret_code!=0 时仍尝试解析 key_value_list | `ParseInternalInfoResponse` 增加 ret_code 检查 |
| DecodeKeyValue 越界 | 无边界检查 | 增加偏移+长度边界校验 |
| ACK 仅显示数字返回码 | 缺少描述映射 | 添加 `ReturnCodeExtensions.GetDescription()` 方法 |
| 初始化强依赖配置文件 | `LivoxHapRadar.Initialize` 仅支持文件路径 | 新增对象初始化重载 + `AppConfigBuilder` Builder 模式 |

### 坐标变换增强

- `CoordinateTransformer` 新增 `devicePitchDeg` 和 `deviceYawDeg` 参数
- 变换执行顺序：安装旋转(R_install) → 设备旋转(R_device) → 平移
- 组合矩阵：`R_total = R_device × R_install`
