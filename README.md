# LivoxHapController

Livox HAP 激光雷达 .NET 控制库 — 基于 Livox-SDK2 通信协议，提供设备发现、连接、配置、点云数据接收及坐标变换的完整功能。

---

## 一、迁移任务完成总结

本项目从 Livox-SDK2 (C++) 迁移核心通信与控制逻辑至 .NET Framework 4.8 平台，完成以下5个步骤：

### Step 1：合并 KeyTypeSupplement 到 KeyType，修复硬编码键值，补充 CommandType

- **KeyType 枚举合并**：将 `KeyTypeSupplement.cs` 中的所有枚举值（`DualEmitEnable=0x0002`、`StateInfoHostIpConfig=0x0005`、`FovConfig0=0x0015`、`FovConfig1=0x0016` 等）合并到 `KeyType.cs`，从 `.csproj` 中移除 `KeyTypeSupplement.cs` 的编译引用。
- **LidarCommander 硬编码修复**：将所有硬编码的键值替换为 `KeyType` 枚举成员（`DualEmit → KeyType.DualEmitEnable`、`SetStateInfoHostIp → KeyType.StateInfoHostIpConfig`、`SetFovConfig0 → KeyType.FovConfig0`、`SetFovConfig1 → KeyType.FovConfig1`）。
- **CommandType 补充**：新增 `LidarResetDevice(0x0201)`、`LidarSetPPSSync(0x0202)`、`LogTimeSync(0x0302)`、`DebugPointCloudControl(0x0303)`、`RequestFirmwareInfo(0x00FF)` 等缺失命令。

### Step 2：扩展 UdpCommunicator（命令端口监听 + ACK/推送区分）

- 启用命令端口（56000）和 IMU 端口（58000）的监听。
- 新增 `CommandAckReceived` 事件：当 `cmd_type=1` 时触发，表示收到雷达对上位机命令的 ACK 应答。
- 新增 `CommandPushReceived` 事件：当 `cmd_type=0` 且 `sender_type=1` 时触发，表示收到雷达主动推送的状态消息。
- 原 `CommandDataReceived` 事件标记为 `[Obsolete]`，保留向后兼容。

### Step 3：创建 LidarDeviceInfo 设备信息模型

- 新建 `LidarDeviceInfo.cs`，存储设备发现后获取的雷达基本信息。
- 包含属性：`Handle`（句柄）、`DeviceType`（设备类型）、`SerialNumber`（序列号）、`LidarIpBytes`/`LidarIpString`（IP地址）、`CommandPort`（命令端口）、`RemoteEndPoint`（远程端点）、`IsConnected`（连接状态）、`DiscoveredTime`（发现时间）。
- 计算属性：`DeviceTypeName`（可读型号名）、`SerialNumberString`（字符串序列号）。

### Step 4：创建 LivoxHapRadar 上层管理类

- 新建 `LivoxHapRadar.cs`，封装完整的 LiDAR 控制生命周期。
- 内部协调 `LidarDiscovery`、`LidarCommander`、`UdpCommunicator` 的事件和工作流。
- 典型流程：`Initialize()` → `Discover()` → `Connect()` → `Configure()` → `StartScan()` → `StopScan()` → `Disconnect()`。
- 事件：`DeviceDiscovered`、`AckResponseReceived`、`PushMessageReceived`、`DeviceStatusUpdated`、`PointCloudDataReceived`、`ImuDataReceived`。
- 便捷命令方法：`SetPclDataType`、`SetScanPattern`、`SetDualEmit`、`EnableImuData`/`DisableImuData`、`SetInstallAttitude`、`SetBlindSpot`、`SetFovConfig0/1`、`RebootDevice` 等。

### Step 5：重构 LivoxHapQuickStart 测试类

- 保留旧版 `Start()` 方法（仅监听模式），兼容已有调用。
- 新增 `StartFull()` 方法（完整流程模式），自动完成：加载配置 → 设备发现 → 连接 → 配置 → 启动扫描。
- 首个设备发现后自动连接和配置，支持 `autoStartScan` 参数。
- 点云数据通过 `GetCurrentFrameOfRawPoints()` 获取。

### 坐标变换增强

- `CoordinateTransformer` 新增 `devicePitchDeg` 和 `deviceYawDeg` 可选参数（默认0），支持设备级俯仰和回转旋转。
- 变换执行顺序：安装旋转(R_install) → 设备旋转(R_device) → 平移。组合矩阵：`R_total = R_device × R_install`。
- `CoordTransParamSet` 新增 `DevicePitch` 和 `DeviceYaw` 属性及对应更新方法。

---

## 二、使用说明

### 2.1 配置文件

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
| `host_ip` | 本机IP地址，用于绑定UDP监听端口 |
| `frame_time` | 帧速率（毫秒），非重复扫描模式下帧窗口越长细节越丰富 |
| `cmd_data_port` | 命令数据端口（默认56000） |
| `push_msg_port` | 推送消息端口（默认55000） |
| `point_data_port` | 点云数据端口（默认57000） |
| `imu_data_port` | IMU数据端口（默认58000） |
| `lidar_ip` | 雷达IP列表，用于连接指定设备 |

---

### 2.2 快速开始（完整流程模式 — 推荐）

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

---

### 2.3 快速开始（仅监听模式 — 兼容旧版）

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

---

### 2.4 监听发现设备

使用 `LidarDiscovery` 或 `LivoxHapRadar` 发现网络中的雷达设备。

#### 方式一：使用 LidarDiscovery（底层API）

```csharp
using LivoxHapController.Services;

var discovery = new LidarDiscovery();

// 订阅设备发现事件
discovery.DeviceDiscovered += (sender, e) =>
{
    Console.WriteLine($"发现设备: SN={e.SerialNumberString}, IP={e.LidarIpString}, Type={e.DeviceTypeName}");
};

// 启动发现（参数为本机IP，留空则绑定所有接口）
discovery.Start("192.168.1.50");

// 停止发现
discovery.Dispose();
```

**工作原理：** 每秒向 `255.255.255.255:56000` 发送广播搜索包，监听 `56001` 端口接收设备响应。

#### 方式二：使用 LivoxHapRadar（上层API — 推荐）

```csharp
using LivoxHapController.Services;

var radar = new LivoxHapRadar();
radar.Initialize("hap_config.json");

// 订阅设备发现事件
radar.DeviceDiscovered += (sender, device) =>
{
    Console.WriteLine($"发现设备: {device}");
};

// 启动发现
radar.Discover("192.168.1.50");
```

**事件参数 `LidarDeviceInfo` 包含：**
- `Handle` — 内部分配的设备句柄
- `DeviceType` / `DeviceTypeName` — 设备类型（如 "HAP"、"Mid360" 等）
- `SerialNumberString` — 设备序列号（唯一标识）
- `LidarIpString` — 雷达IP地址
- `CommandPort` — 命令端口
- `IsConnected` — 是否已连接

---

### 2.5 连接设备

#### 方式一：连接指定设备

```csharp
// 从已发现设备列表中选择
List<LidarDeviceInfo> devices = radar.GetDiscoveredDevices();
radar.Connect(devices[0]);
```

#### 方式二：通过序列号连接

```csharp
bool success = radar.ConnectBySerialNumber("1JHDG9800H00200");
```

#### 方式三：连接第一个发现的设备

```csharp
bool success = radar.ConnectFirst();
```

---

### 2.6 获取设备基本信息

连接设备后，可通过查询命令获取设备内部信息：

```csharp
using LivoxHapController.Enums;

// 订阅设备状态更新事件（查询结果通过此事件返回）
radar.DeviceStatusUpdated += (sender, info) =>
{
    if (info.IsSuccess)
    {
        foreach (var result in info.ParamResults)
        {
            Console.WriteLine($"Key=0x{result.Key:X4}, Value={result.RawValueHex}");
        }
    }
};

// 查询固件类型
radar.QueryFirmwareType();

// 查询固件版本
radar.QueryFirmwareVersion();

// 批量查询多个参数
radar.QueryInternalInfo(new KeyType[]
{
    KeyType.SerialNumber,
    KeyType.ProductInfo,
    KeyType.VersionApp,
    KeyType.MacAddress,
    KeyType.CurrentWorkState,
    KeyType.CoreTemp,
    KeyType.StatusCode
});
```

**可查询的常用 `KeyType`：**

| KeyType | 值 | 说明 |
|---------|-----|------|
| `SerialNumber` | 0x8000 | 设备序列号 |
| `ProductInfo` | 0x8001 | 产品信息 |
| `VersionApp` | 0x8002 | 应用固件版本 |
| `VersionLoader` | 0x8003 | 加载器固件版本 |
| `VersionHardware` | 0x8004 | 硬件版本 |
| `MacAddress` | 0x8005 | MAC地址 |
| `CurrentWorkState` | 0x8006 | 当前工作状态 |
| `CoreTemp` | 0x8007 | 核心温度 |
| `PowerUpCount` | 0x8008 | 上电次数 |
| `FirmwareType` | 0x8010 | 固件类型 |
| `StatusCode` | 0x800D | 状态码 |
| `LidarDiagStatus` | 0x800E | 诊断状态 |

---

### 2.7 对设备进行各项配置

连接设备后，需先配置主机IP等信息，再进行其他参数配置。

#### 2.7.1 网络配置（必须 — 连接后首先执行）

```csharp
// 方式一：手动指定参数
radar.Configure(
    hostIp: "192.168.1.50",
    cmdPort: 56000,
    pointPort: 57000,
    imuPort: 58000,
    pushMsgPort: 55000
);

// 方式二：从配置文件读取
radar.ConfigureFromConfig();
```

此步骤告诉雷达将点云数据、IMU数据、状态推送等发送到哪个主机的哪个端口。**未配置则无法接收数据。**

#### 2.7.2 点云数据类型

```csharp
// 32位高精度笛卡尔坐标（1mm精度，推荐）
radar.SetPclDataType(0x01);

// 16位低精度笛卡尔坐标（10mm精度）
radar.SetPclDataType(0x02);

// IMU数据
radar.SetPclDataType(0x00);
```

#### 2.7.3 扫描模式

```csharp
// 非重复扫描（默认，覆盖式扫描，视野逐渐填充）
radar.SetScanPattern(0x00);

// 重复扫描（固定区域反复扫描）
radar.SetScanPattern(0x01);

// 重复扫描（低帧率）
radar.SetScanPattern(0x02);
```

#### 2.7.4 双发射模式

```csharp
// 启用双发射（提高点云密度）
radar.SetDualEmit(true);

// 禁用双发射
radar.SetDualEmit(false);
```

#### 2.7.5 IMU 数据控制

```csharp
// 启用IMU数据推送
radar.EnableImuData();

// 禁用IMU数据推送
radar.DisableImuData();
```

#### 2.7.6 安装姿态

```csharp
// 设置雷达安装角度和偏移（角度单位：度，偏移单位：毫米）
radar.SetInstallAttitude(
    roll: 0f,      // 横滚角
    pitch: 15f,    // 俯仰角
    yaw: 0f,       // 偏航角
    x: 100,        // X偏移
    y: 0,          // Y偏移
    z: 500         // Z偏移
);
```

#### 2.7.7 盲区设置

```csharp
// 设置盲区距离（单位cm，范围50-200）
radar.SetBlindSpot(100);
```

#### 2.7.8 FOV 视场配置

```csharp
// 设置FOV区域0（角度单位：0.01度）
radar.SetFovConfig0(
    yawStart: 0,       // 水平起始角
    yawStop: 36000,    // 水平结束角（360°）
    pitchStart: -700,  // 垂直起始角（-7°）
    pitchStop: 5500    // 垂直结束角（55°）
);

// 设置FOV区域1
radar.SetFovConfig1(
    yawStart: 0, yawStop: 18000,
    pitchStart: -500, pitchStop: 3000
);
```

#### 2.7.9 窗口加热

```csharp
// 启用/禁用窗口自动加热
radar.EnableGlassHeat();
radar.DisableGlassHeat();

// 启用/停止强制加热
radar.StartForcedHeating();
radar.StopForcedHeating();
```

#### 2.7.10 开机工作模式

```csharp
// 设置开机后进入的工作模式
// 0=默认, 1=正常扫描, 2=唤醒
radar.SetWorkModeAfterBoot(0x01);
```

#### 2.7.11 设备重启

```csharp
// 延迟重启（毫秒，范围100~2000）
radar.RebootDevice(500);
```

---

### 2.8 从设备接收点云数据

#### 2.8.1 启动/停止扫描

```csharp
// 启动正常扫描
radar.StartScan();

// 停止扫描（进入休眠模式）
radar.StopScan();
```

#### 2.8.2 接收点云数据

```csharp
// 订阅点云数据事件
radar.PointCloudDataReceived += (sender, rawData) =>
{
    // rawData 是UDP原始字节数据，使用 PointCloudParser 解析
    var packet = PointCloudParser.ParsePacket(rawData);

    Console.WriteLine($"时间戳: {packet.Header.Timestamp}, " +
                      $"点数: {packet.Header.DotNum}, " +
                      $"数据类型: {packet.Header.DataType}");

    // 访问笛卡尔坐标点
    foreach (var point in packet.CartesianDataPoints)
    {
        // point.X, point.Y, point.Z — 坐标（米）
        // point.Reflectivity — 反射率
        // point.TagInformation — 标签信息
        // point.Distance — 距离（米）
    }

    // 访问IMU数据
    foreach (var imu in packet.ImuDataPoints)
    {
        // imu.GyroX/Y/Z — 陀螺仪
        // imu.AccX/Y/Z — 加速度计
    }
};
```

#### 2.8.3 使用 QuickStart 获取帧数据

```csharp
// 获取当前帧的点云数据快照（线程安全）
CartesianDataPoint[] frame = LivoxHapQuickStart.GetCurrentFrameOfRawPoints();

foreach (var point in frame)
{
    Console.WriteLine($"X={point.X:F3}m, Y={point.Y:F3}m, Z={point.Z:F3}m, Ref={point.Reflectivity}");
}
```

**帧参数说明：**

| 属性 | 说明 |
|------|------|
| `FrameTime` | 帧速率（毫秒），默认100ms，值越大扫描细节越丰富 |
| `PkgsPerFrame` | 每帧包数，= 4 × FrameTime |
| `PointsPerFrame` | 每帧点数，= 96 × PkgsPerFrame |
| `CurrentlyReceiving` | 是否正在接收数据（2秒内收到数据视为正在接收） |

#### 2.8.4 点云数据包头信息

每个点云包的 `PointCloudHeader` 包含以下关键信息：

| 字段 | 类型 | 说明 |
|------|------|------|
| `Version` | byte | 协议版本 |
| `Length` | ushort | 整包长度 |
| `DotNum` | ushort | 本包点数（点云96，IMU为1） |
| `UdpCnt` | ushort | UDP包计数 |
| `FrameCnt` | byte | 帧计数 |
| `DataType` | PointCloudDataType | 数据类型（Cartesian32Bit/Cartesian16Bit/ImuData） |
| `TimeType` | TimeType | 时间戳类型（0=无同步源, 1=gPTP同步） |
| `TimestampNanoSec` | ulong | 纳秒时间戳 |
| `Timestamp` | DateTime | UTC时间 |
| `SafetyInformation` | enum | 功能安全信息（整包可信/不可信/非0点可信） |

---

### 2.9 坐标变换

#### 2.9.1 参数配置

```csharp
using LivoxHapController.Models;

// 创建参数集
var paramSet = new CoordTransParamSet(
    roll: 0,       // 横滚角（度），绕X轴，向前看时顺时针为正
    pitch: 15,     // 俯仰角（度），绕Y轴，向下转为正
    yaw: 30,       // 偏航角（度），绕Z轴，俯视时向左为正
    x: 100,        // X偏移（毫米）
    y: 0,          // Y偏移（毫米）
    z: 500         // Z偏移（毫米）
);

// 设置设备级旋转（如设备有俯仰/回转运动）
paramSet.UpdateDevicePitch(5.0);   // 设备俯仰角（度）
paramSet.UpdateDeviceYaw(45.0);    // 设备回转角（度）
```

#### 2.9.2 变换执行顺序

变换按以下顺序执行：**安装旋转 → 设备旋转 → 平移**

1. **安装旋转**（`R_install = Rz × Ry × Rx`）：雷达安装在设备上的固定角度
2. **设备旋转**（`R_device = R_yaw × R_pitch`）：设备自身运动引起的额外旋转
3. **平移**：安装位置的空间偏移

数学表达式：`P' = R_device × (R_install × P) + T = (R_device × R_install) × P + T`

#### 2.9.3 使用坐标变换

```csharp
using LivoxHapController.Services;

// 单点变换
double[] result = CoordinateTransformer.TransformPoint(
    x: 1000, y: 500, z: 3000,     // 原始坐标（毫米）
    paramSet                         // 参数集
);

// 批量变换（扩展方法）
CartesianDataPoint[] transformed = pointCloud.TransformPoints(paramSet);
```

> **注意**：使用 `LivoxHapQuickStart` 时，若提供了 `CoordTransParamSet`，点云数据在入缓冲区前会自动进行坐标变换。

---

### 2.10 多台扫描仪数据区分

当同一子网内存在两台或多台 Livox 扫描仪时，需在接收数据时区分不同设备的数据。以下是具体方法：

#### 2.10.1 核心原理：通过 LiDAR IP 地址区分

每台 Livox 雷达出厂时有固定的IP地址（如 `192.168.1.1xx`）。UDP数据包的来源IP即为发送该数据的雷达IP，可通过此信息区分不同设备。

#### 2.10.2 方式一：为每台雷达分配不同端口（推荐）

在配置文件中将不同雷达的点云数据发送到主机的不同端口：

```json
{
  "HAP": {
    "host_net_info": [
      {
        "host_ip": "192.168.1.50",
        "cmd_data_port": 56000,
        "point_data_port": 57000,
        "imu_data_port": 58000,
        "push_msg_port": 55000,
        "frame_time": 100
      }
    ]
  }
}
```

然后对每台雷达分别调用 `SetPointCloudHostIp`，将其点云数据发送到不同的主机端口：

```csharp
// 雷达1：点云发送到57000端口
commander1.SetPointCloudHostIp("192.168.1.50", 57000, 0);

// 雷达2：点云发送到57001端口
commander2.SetPointCloudHostIp("192.168.1.50", 57001, 0);
```

主机端为每个端口创建独立的 `UdpCommunicator` 实例进行监听，天然实现数据隔离。

#### 2.10.3 方式二：同一端口，通过 UDP 远程端点区分

若多台雷达配置为发送到同一端口，可通过 UDP 接收时的远程端点（`IPEndPoint`）中的IP地址来区分数据来源。当前 `UdpCommunicator` 的事件仅传递 `byte[]` 数据，不包含远程端点信息。若需使用此方式，需对 `UdpCommunicator` 进行扩展，在事件参数中携带远程端点信息：

**扩展方案示例：**

```csharp
// 1. 自定义事件参数，携带远程端点
public class PointCloudReceivedEventArgs : EventArgs
{
    public byte[] Data { get; set; }
    public IPEndPoint RemoteEndPoint { get; set; }
}

// 2. 修改 UdpCommunicator 中的点云接收逻辑
// 在 ListenerWorkerCloudPoint 中：
IPEndPoint remoteEP = null;
byte[] data = _pointCloudClient.Receive(ref remoteEP);
PointCloudDataReceived?.Invoke(this, 
    new PointCloudReceivedEventArgs { Data = data, RemoteEndPoint = remoteEP });

// 3. 使用时根据来源IP区分
udpComm.PointCloudDataReceived += (sender, e) =>
{
    string lidarIp = e.RemoteEndPoint.Address.ToString();
    if (lidarIp == "192.168.1.101")
    {
        // 雷达1的数据
    }
    else if (lidarIp == "192.168.1.102")
    {
        // 雷达2的数据
    }
};
```

#### 2.10.4 方式三：使用多个 LivoxHapRadar 实例

为每台雷达创建独立的 `LivoxHapRadar` 实例，分别管理各自的生命周期：

```csharp
// 创建多个管理实例
var radar1 = new LivoxHapRadar();
var radar2 = new LivoxHapRadar();

// 分别初始化
radar1.Initialize("hap_config_radar1.json");
radar2.Initialize("hap_config_radar2.json");

// 分别订阅事件
radar1.PointCloudDataReceived += (s, data) => { /* 雷达1的数据 */ };
radar2.PointCloudDataReceived += (s, data) => { /* 雷达2的数据 */ };

// 分别发现和连接
radar1.Discover("192.168.1.50");
radar2.Discover("192.168.1.50");

// 等待设备发现后，按序列号分别连接
radar1.ConnectBySerialNumber("1JHDG9800H00200");
radar2.ConnectBySerialNumber("1JHDG9800H00300");

// 分别配置端口（关键：每台雷达发送到不同端口）
radar1.Configure("192.168.1.50", pointPort: 57000);
radar2.Configure("192.168.1.50", pointPort: 57001);

// 分别启动扫描
radar1.StartScan();
radar2.StartScan();
```

> **注意**：多个 `LivoxHapRadar` 实例使用同一主机IP时，各实例的UDP端口不能冲突。需确保配置文件中每台雷达的 `point_data_port`、`imu_data_port`、`cmd_data_port` 互不相同。

#### 2.10.5 多设备数据区分总结

| 方式 | 适用场景 | 优点 | 缺点 |
|------|---------|------|------|
| 不同端口 | 雷达数量少（2-3台） | 实现简单，天然隔离 | 端口数量有限 |
| 远程端点区分 | 雷达数量多，端口紧张 | 节省端口资源 | 需扩展 UdpCommunicator |
| 多实例 | 需要完全独立管理每台雷达 | 隔离性好，代码清晰 | 资源占用较多 |

---

### 2.11 ACK 响应与设备推送

#### ACK 响应

所有命令发送后，雷达会返回ACK应答。订阅 `AckResponseReceived` 事件获取：

```csharp
radar.AckResponseReceived += (sender, response) =>
{
    // response.RetCode — 返回码（0=成功）
    // response.ErrorKey — 出错时的参数键
    // response.IsSuccess — 是否成功
    Console.WriteLine($"ACK: RetCode={response.RetCode}, Success={response.IsSuccess}");
};
```

#### 设备推送

雷达会主动推送工作状态等信息。订阅 `DeviceStatusUpdated` 或 `PushMessageReceived` 事件获取：

```csharp
// 解析后的设备状态更新
radar.DeviceStatusUpdated += (sender, info) =>
{
    Console.WriteLine($"状态更新: RetCode={info.RetCode}, 参数数={info.ParamResults?.Length ?? 0}");
    if (info.ParamResults != null)
    {
        foreach (var result in info.ParamResults)
        {
            Console.WriteLine($"  Key=0x{result.Key:X4}, Value={result.RawValueHex}");
        }
    }
};

// 原始推送字节数据
radar.PushMessageReceived += (sender, rawData) =>
{
    Console.WriteLine($"收到推送数据: {rawData.Length} 字节");
};
```

---

### 2.12 完整使用示例

```csharp
using LivoxHapController.Services;
using LivoxHapController.Models;
using LivoxHapController.Enums;
using LivoxHapController.Services.Parsers;

// 1. 创建管理实例
var radar = new LivoxHapRadar();

// 2. 初始化
radar.Initialize("hap_config.json", new CoordTransParamSet(0, 15, 0, 100, 0, 500));

// 3. 订阅事件
radar.DeviceDiscovered += (s, device) =>
    Console.WriteLine($"发现: {device.DeviceTypeName} SN={device.SerialNumberString} IP={device.LidarIpString}");

radar.AckResponseReceived += (s, response) =>
    Console.WriteLine($"ACK: Success={response.IsSuccess}, RetCode={response.RetCode}");

radar.PointCloudDataReceived += (s, rawData) =>
{
    var packet = PointCloudParser.ParsePacket(rawData);
    // 处理点云数据...
};

// 4. 发现设备
radar.Discover("192.168.1.50");

// 5. 等待发现后连接
// （实际代码中应等待 DeviceDiscovered 事件触发后再连接）
System.Threading.Thread.Sleep(3000); // 等待设备发现
radar.ConnectFirst();

// 6. 配置设备
radar.ConfigureFromConfig();
radar.SetPclDataType(0x01);      // 32位高精度
radar.SetScanPattern(0x00);      // 非重复扫描
radar.SetDualEmit(true);         // 启用双发射

// 7. 启动扫描
radar.StartScan();

// 8. 工作中...
System.Threading.Thread.Sleep(60000);

// 9. 停止扫描
radar.StopScan();

// 10. 断开连接并释放资源
radar.Dispose();
```

---

### 2.13 项目结构

```
LivoxHapController/
├── Config/                     # 配置管理
│   ├── AppConfig.cs            #   应用配置（单例）
│   ├── ConfigLoader.cs         #   配置加载器
│   ├── DeviceConfig.cs         #   设备配置模型
│   ├── HostNetInfo.cs          #   主机网络信息
│   └── NetInfoConfig.cs        #   网络配置模型
├── Enums/                      # 枚举定义
│   ├── CommandType.cs          #   命令类型（协议命令ID）
│   ├── DeviceEnums.cs          #   设备类型枚举
│   ├── KeyType.cs              #   参数键类型（合并后）
│   ├── PointCloudDataType.cs   #   点云数据类型
│   ├── ReturnCode.cs           #   返回码
│   └── ...
├── Models/                     # 数据模型
│   ├── CoordTransParamSet.cs   #   坐标变换参数集
│   ├── LidarDeviceInfo.cs      #   设备信息模型
│   ├── DataPoints/             #   点云数据模型
│   │   ├── CartesianDataPoint.cs       # 笛卡尔坐标点
│   │   ├── ImuDataPoint.cs             # IMU数据点
│   │   ├── PointCloudHeader.cs         # 点云包头
│   │   └── PointCloudDataPacket.cs     # 点云数据包
│   └── ...
├── Services/                   # 核心服务
│   ├── CoordinateTransformer.cs #  坐标变换核心
│   ├── CrcCalculator.cs        #   CRC校验计算
│   ├── KeyValueCodec.cs        #   KeyValue编解码
│   ├── LidarCommander.cs       #   命令控制器
│   ├── LidarDiscovery.cs       #   设备发现服务
│   ├── LivoxHapRadar.cs        #   上层管理类（推荐入口）
│   ├── MathUtils.cs            #   数学工具（矩阵运算）
│   ├── SdkPacketBuilder.cs     #   协议包构建器
│   ├── UdpCommunicator.cs      #   UDP通信处理器
│   └── Parsers/                #   数据解析器
│       ├── AckResponseParser.cs    # ACK响应解析
│       ├── PointCloudParser.cs     # 点云数据解析
│       └── ProtocolParser.cs       # 协议通用解析
└── Test/                       # 测试/快捷入口
    └── LivoxHapQuickStart.cs   #   快速启动类
```

---

### 2.14 协议参考

本库基于 Livox HAP 通信协议实现，协议详细信息参见项目内 `Livox-SDK-Communication-Protocol-HAP.md` 文件。

**协议关键参数：**

- 传输层：UDP
- 字节序：小端序（Little-Endian）
- 包头校验：CRC-16
- 数据段校验：CRC-32
- 固定包头大小：24字节
- 点云每包点数：96
- 点云发送速率：452KHz

---

## LivoxLidarConfigurator 说明

```prompt
新添加的wpf窗体项目，用于做与LivoxHapController功能相匹配的查询、配置界面。界面需要具有整个生命周期内的全部功能，包括广播发现、显示广播搜索到的设备、选中对应设备后显示设备的相关信息、选中设备后支持向设备发送对应的配置命令；为了即时获得反馈，需要显示设备返回的信息以及发生的错误的信息；再预留一个文本框用来显示从设备收到的点云数据
```

```thinking
让我先了解现有项目的完整结构，然后创建WPF界面。

现在我需要查看解决方案文件，然后更新csproj添加项目引用，再创建完整的WPF界面。
现在让我查看LivoxHapRadar的完整API以设计匹配的界面。

现在我需要了解 `KeyValueResult` 结构来正确显示查询结果。
现在我有了所有需要的信息。让我创建完整的WPF应用程序。先更新csproj添加项目引用，然后创建XAML和代码。
现在创建MainWindow的代码后端：
现在csproj中使用通配符引用Core项目源码的方式可能有问题，因为Core项目也是用Link引用LivoxHapController的文件，这会造成路径嵌套问题。让我改用直接引用LivoxHapController源文件的方式。
现在需要检查代码中是否引用了 `CommonLib` 相关的类型。
WPF项目需要引用 `CommonLib.Core`（.NET 9.0版本），和Core项目类似。
让我检查一下关键文件是否有编译问题。

WPF配置器项目已创建完成。以下是完成的所有工作：
```

### 1. 项目文件 `LivoxLidarConfigurator.csproj`
- 目标框架 `net10.0-windows`，启用WPF
- 以Link方式引用 LivoxHapController 项目的全部46个源文件
- 引用 `Newtonsoft.Json` 和 `CommonLib.Core`
- 复制配置文件到输出目录

### 2. 界面布局 `MainWindow.xaml` — 三区域布局

**顶部（初始化与发现）：**
- 配置文件路径选择、主机IP输入
- 初始化 / 开始发现 / 停止发现 按钮

**中部（三列）：**
- **左列 — 设备列表**：ListBox 显示已发现设备（含连接状态指示灯），连接/断开按钮
- **中列 — 设备信息**：基本信息展示（类型/SN/IP/端口/发现时间/连接状态）+ 7个查询按钮（固件类型、版本、序列号、MAC、工作状态、核心温度、状态码）+ 查询结果文本框
- **右列 — 配置面板**：扫描控制（启动/停止+状态灯）、网络配置、点云数据配置（类型/扫描模式/双发射）、IMU控制、安装姿态、FOV配置、盲区设置、加热控制、开机模式、重启设备

**底部（双栏）：**
- 左栏 — 消息日志（黑底白字，ACK响应、错误、推送消息等）
- 右栏 — 点云数据显示（黑底绿字，包统计、坐标数据摘要、IMU数据），含自动滚动和显示开关

### 3. 代码逻辑 `MainWindow.xaml.cs`
- 完整生命周期管理：初始化 → 发现 → 连接 → 配置 → 扫描 → 停止 → 断开
- 所有 `LivoxHapRadar` 事件均有处理（设备发现、ACK响应、推送消息、状态更新、点云/IMU数据）
- 查询结果智能解析（版本号格式化、MAC地址格式化、温度/状态码等）
- 点云数据降频显示（每100包显示一次摘要，前3个点坐标）
- 线程安全的UI更新（Dispatcher.Invoke）
- 文本框自动截断防内存溢出