using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using LivoxHapController.Config;
using LivoxHapController.Enums;
using LivoxHapController.Models;
using LivoxHapController.Services.Parsers;

namespace LivoxHapController.Services
{
    /// <summary>
    /// Livox HAP LiDAR 上层管理类
    /// 封装完整的LiDAR控制生命周期，提供统一的API接口
    /// 内部协调 LidarDiscovery、LidarCommander、UdpCommunicator 的事件和工作流
    /// 
    /// 典型使用流程：
    /// 1. Initialize() - 加载配置文件
    /// 2. Discover() - 搜索网络中的LiDAR设备
    /// 3. Connect() - 连接到指定设备
    /// 4. Configure() - 配置设备参数（点云类型、扫描模式等）
    /// 5. StartScan() - 启动扫描，开始接收点云数据
    /// 6. StopScan() - 停止扫描
    /// 7. Disconnect() - 断开连接
    /// </summary>
    public class LivoxHapRadar : IDisposable
    {
        #region 私有字段

        /// <summary>设备发现服务</summary>
        private LidarDiscovery
            //.net 9框架下使返回对象可为空
#if NET9_0_OR_GREATER
            ?
#endif
            _discovery;

        /// <summary>UDP通信处理器</summary>
        private UdpCommunicator
            //.net 9框架下使返回对象可为空
#if NET9_0_OR_GREATER
            ?
#endif
            _udpComm;

        /// <summary>已发现的设备列表</summary>
        private readonly List<LidarDeviceInfo> _discoveredDevices;

        /// <summary>当前连接的设备信息</summary>
        private LidarDeviceInfo
            //.net 9框架下使返回对象可为空
#if NET9_0_OR_GREATER
            ?
#endif
            _connectedDevice;

        /// <summary>当前连接的命令控制器</summary>
        private LidarCommander
            //.net 9框架下使返回对象可为空
#if NET9_0_OR_GREATER
            ?
#endif
            _commander;

        /// <summary>设备句柄计数器，每发现一个新设备递增</summary>
        private int _handleCounter;

        /// <summary>是否已释放资源</summary>
        private bool _disposed;

        /// <summary>同步锁对象，确保线程安全</summary>
#if NET9_0_OR_GREATER
        private readonly Lock _syncRoot = new();
#elif NET45_OR_GREATER
        private readonly object _syncRoot = new object();
#endif

        /// <summary>是否正在扫描</summary>
        private bool _isScanning;

        /// <summary>异步监控缓存的取消令牌源，按需创建</summary>
#if NET9_0_OR_GREATER
        private readonly CancellationTokenSource? _monitorCts;
#elif NET45_OR_GREATER
        private readonly CancellationTokenSource _monitorCts;
#endif

        #endregion

        #region 事件

        /// <summary>
        /// 发现设备事件
        /// 当搜索到新的LiDAR设备时触发
        /// </summary>
        public event EventHandler<LidarDeviceInfo>
            //.net 9框架下使返回对象可为空
#if NET9_0_OR_GREATER
            ?
#endif
            DeviceDiscovered;

        /// <summary>
        /// 收到ACK响应事件
        /// 当命令端口收到设备的ACK响应时触发
        /// </summary>
        public event EventHandler<AsyncControlResponse>
            //.net 9框架下使返回对象可为空
#if NET9_0_OR_GREATER
            ?
#endif
            AckResponseReceived;

        /// <summary>
        /// 收到设备推送消息事件
        /// 当收到设备主动推送的状态信息时触发
        /// </summary>
        public event EventHandler<byte[]>
             //.net 9框架下使返回对象可为空
#if NET9_0_OR_GREATER
            ?
#endif
             PushMessageReceived;

        /// <summary>
        /// 设备状态变更事件
        /// 当收到设备推送的工作状态信息时触发
        /// </summary>
        public event EventHandler<InternalInfoResponse>
             //.net 9框架下使返回对象可为空
#if NET9_0_OR_GREATER
            ?
#endif
             DeviceStatusUpdated;

        /// <summary>
        /// 点云数据接收事件
        /// 当收到点云端口的原始数据时触发
        /// </summary>
        public event EventHandler<byte[]>
             //.net 9框架下使返回对象可为空
#if NET9_0_OR_GREATER
            ?
#endif
             PointCloudDataReceived;

        /// <summary>
        /// IMU数据接收事件
        /// 当收到IMU端口的原始数据时触发
        /// </summary>
        public event EventHandler<byte[]>
             //.net 9框架下使返回对象可为空
#if NET9_0_OR_GREATER
            ?
#endif
             ImuDataReceived;

        #endregion

        #region 属性

        /// <summary>
        /// 是否已初始化
        /// </summary>
        public bool IsInitialized { get; private set; }

        /// <summary>
        /// 是否正在扫描
        /// </summary>
        public bool IsScanning { get { return _isScanning; } }

        /// <summary>
        /// 是否已连接到设备
        /// </summary>
        public bool IsConnected { get { return _connectedDevice != null && _connectedDevice.IsConnected; } }

        /// <summary>
        /// 当前连接的设备信息
        /// 未连接时返回null
        /// </summary>
        public LidarDeviceInfo
            //.net 9框架下使返回对象可为空
#if NET9_0_OR_GREATER
            ?
#endif
            ConnectedDevice
        { get { return _connectedDevice; } }

        /// <summary>
        /// 已发现的设备数量
        /// </summary>
        public int DiscoveredDeviceCount { get { lock (_syncRoot) { return _discoveredDevices.Count; } } }

        /// <summary>
        /// 当前应用的配置
        /// </summary>
        public AppConfig
             //.net 9框架下使返回对象可为空
#if NET9_0_OR_GREATER
            ?
#endif
             Config
        { get; private set; }

        /// <summary>
        /// 坐标变换参数集
        /// </summary>
        public CoordTransParamSet
             //.net 9框架下使返回对象可为空
#if NET9_0_OR_GREATER
            ?
#endif
             CoordTransParamSet
        { get; set; }

        #endregion

        #region 构造与析构

        /// <summary>
        /// 构造LivoxHapRadar管理类
        /// </summary>
        public LivoxHapRadar()
        {
#if NET45_OR_GREATER
            _discoveredDevices = new List<LidarDeviceInfo>();
#elif NET9_0_OR_GREATER
            _discoveredDevices = [];
#endif
            _handleCounter = 0;
            _isScanning = false;
            _disposed = false;
            IsInitialized = false;
        }

        /// <summary>
        /// 析构函数，确保资源释放
        /// </summary>
        ~LivoxHapRadar()
        {
            Dispose(false);
        }

        #endregion

        #region 生命周期方法

        /// <summary>
        /// 初始化雷达管理器
        /// 加载配置文件并初始化内部组件
        /// </summary>
        /// <param name="configFile">配置文件路径或文件名</param>
        /// <param name="coordTransParamSet">坐标变换参数集（可选）</param>
        /// <exception cref="ArgumentNullException">配置文件路径为空</exception>
        /// <exception cref="ArgumentException">配置文件不存在</exception>
        public void Initialize(string configFile, CoordTransParamSet
             //.net 9框架下使返回对象可为空
#if NET9_0_OR_GREATER
            ?
#endif
             coordTransParamSet = null)
        {
            if (string.IsNullOrWhiteSpace(configFile))
                throw new ArgumentNullException(nameof(configFile), "配置文件路径不能为空");

            string path = configFile.Contains(System.IO.Path.VolumeSeparatorChar)
                ? configFile
                : System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, configFile);

            if (!System.IO.File.Exists(path))
                throw new ArgumentException("配置文件不存在: " + path, nameof(configFile));

            // 加载配置
            AppConfig.Init(path);
            Config = AppConfig.Instance;

            // 保存坐标变换参数
            if (coordTransParamSet != null)
                CoordTransParamSet = coordTransParamSet;

            // 初始化UDP通信处理器
            _udpComm = new UdpCommunicator();

            // 初始化设备发现服务
            _discovery = new LidarDiscovery();
            _discovery.DeviceDiscovered += OnDeviceDiscovered;

            IsInitialized = true;
        }

        /// <summary>
        /// 开始搜索网络中的LiDAR设备
        /// 启动广播发现并监听设备响应，同时启动UDP数据端口监听
        /// </summary>
        /// <param name="hostIp">本机IP地址（用于绑定监听端口），为空则绑定所有接口</param>
        public void Discover(string hostIp = "")
        {
            if (!IsInitialized)
                throw new InvalidOperationException("请先调用 Initialize() 初始化");

            // 获取配置中的主机IP
            string ip = string.IsNullOrWhiteSpace(hostIp) && Config?.HapConfig?.HostNetInfo?.Count > 0
                ? Config.HapConfig.HostNetInfo[0].HostIp
                : hostIp;

            if (_udpComm != null)
            {
                _udpComm.PointCloudDataReceived += OnPointCloudDataReceived;
                _udpComm.ImuDataReceived += OnImuDataReceived;
                _udpComm.CommandAckReceived += OnCommandAckReceived;
                _udpComm.CommandPushReceived += OnCommandPushReceived;
                // 启动UDP数据端口监听
                if (Config?.HapConfig?.HostNetInfo?.Count > 0)
                    _udpComm.StartListening(Config.HapConfig.HostNetInfo[0]);
                else
                    _udpComm.StartListening(ip);
            }

            // 启动设备发现
            _discovery?.Start(ip);
        }

        /// <summary>
        /// 连接到已发现的LiDAR设备
        /// 创建命令控制器，设置设备为已连接状态
        /// </summary>
        /// <param name="deviceInfo">要连接的设备信息</param>
        /// <exception cref="ArgumentNullException">设备信息为空</exception>
        public void Connect(LidarDeviceInfo deviceInfo)
        {
            if (deviceInfo == null)
                throw new ArgumentNullException(nameof(deviceInfo), "设备信息不能为空");

            if (_udpComm == null)
                throw new NullReferenceException("UdpCommunicator对象不能为空（_udpComm）");

            // 创建命令控制器
            // 传入UdpCommunicator，使命令通过其命令端口发送，确保ACK能被正确接收
            _commander = new LidarCommander(deviceInfo.LidarIpString, deviceInfo.CommandPort, _udpComm);
            _connectedDevice = deviceInfo;
            _connectedDevice.IsConnected = true;
        }

        /// <summary>
        /// 通过序列号连接到已发现的设备
        /// </summary>
        /// <param name="serialNumber">设备序列号</param>
        /// <returns>是否连接成功</returns>
        public bool ConnectBySerialNumber(string serialNumber)
        {
            LidarDeviceInfo
            //.net 9框架下使返回对象可为空
#if NET9_0_OR_GREATER
            ?
#endif
             device;
            lock (_syncRoot)
            {
                device = _discoveredDevices.FirstOrDefault(d => d.SerialNumberString == serialNumber);
            }

            if (device == null) return false;
            Connect(device);
            return true;
        }

        /// <summary>
        /// 连接到第一个已发现的设备
        /// </summary>
        /// <returns>是否连接成功</returns>
        public bool ConnectFirst()
        {
            LidarDeviceInfo
            //.net 9框架下使返回对象可为空
#if NET9_0_OR_GREATER
            ?
#endif
             device;
            lock (_syncRoot)
            {
                device = _discoveredDevices.FirstOrDefault();
            }

            if (device == null) return false;
            Connect(device);
            return true;
        }

        /// <summary>
        /// 配置已连接的设备
        /// 发送设备IP配置、主机IP配置等初始化命令
        /// </summary>
        /// <param name="hostIp">主机IP地址</param>
        /// <param name="cmdPort">主机命令端口</param>
        /// <param name="pointPort">主机点云端口</param>
        /// <param name="imuPort">主机IMU端口</param>
        /// <param name="pushMsgPort">主机推送消息端口</param>
        /// <exception cref="InvalidOperationException">设备未连接</exception>
        public void Configure(string hostIp, ushort cmdPort = 56000, ushort pointPort = 57000,
            ushort imuPort = 58000, ushort pushMsgPort = 55000)
        {
            if (!IsConnected || _commander == null)
                throw new InvalidOperationException("设备未连接，请先调用 Connect()");

            // 设置状态信息主机IP配置
            _commander.SetStateInfoHostIp(hostIp, cmdPort, pushMsgPort);

            // 设置点云数据主机IP配置
            _commander.SetPointCloudHostIp(hostIp, pointPort, 0);

            // 设置IMU数据主机IP配置
            _commander.SetImuHostIp(hostIp, imuPort, 0);
        }

        /// <summary>
        /// 使用配置文件中的参数配置已连接的设备
        /// </summary>
        /// <exception cref="InvalidOperationException">设备未连接</exception>
        public void ConfigureFromConfig()
        {
            if (!IsConnected || _commander == null)
                throw new InvalidOperationException("设备未连接，请先调用 Connect()");

            if (Config?.HapConfig?.HostNetInfo == null || Config.HapConfig.HostNetInfo.Count == 0)
                throw new InvalidOperationException("配置中无有效的HostNetInfo");

            var hostNetInfo = Config.HapConfig.HostNetInfo[0];

            // 设置各通道的主机IP配置
            _commander.SetStateInfoHostIp(hostNetInfo.HostIp, (ushort)hostNetInfo.CmdDataPort, (ushort)hostNetInfo.PushMsgPort);
            _commander.SetPointCloudHostIp(hostNetInfo.HostIp, (ushort)hostNetInfo.PointDataPort, 0);
            _commander.SetImuHostIp(hostNetInfo.HostIp, (ushort)hostNetInfo.ImuDataPort, 0);
        }

        /// <summary>
        /// 启动正常扫描模式
        /// 设置工作模式为Normal并标记为正在扫描
        /// </summary>
        /// <exception cref="InvalidOperationException">设备未连接</exception>
        public void StartScan()
        {
            if (!IsConnected || _commander == null)
                throw new InvalidOperationException("设备未连接，请先调用 Connect()");

            _commander.StartNormalScan();
            _isScanning = true;
        }

        /// <summary>
        /// 停止扫描
        /// HAP(TX)版本不支持休眠状态(0x03)，设置工作模式为待机(0x02)
        /// </summary>
        public void StopScan()
        {
            if (!IsConnected || _commander == null)
                throw new InvalidOperationException("设备未连接");

            // HAP(TX)不支持休眠模式(0x03)，使用待机模式(0x02)停止扫描
            _commander.SetStandbyMode();
            _isScanning = false;
        }

        /// <summary>
        /// 断开与设备的连接
        /// 释放命令控制器资源，标记设备为未连接
        /// </summary>
        public void Disconnect()
        {
            if (_isScanning)
                StopScan();

            _commander?.Dispose();
            _commander = null;

            if (_connectedDevice != null)
            {
                _connectedDevice.IsConnected = false;
                _connectedDevice = null;
            }
        }

        #endregion

        #region 便捷命令方法

        /// <summary>
        /// 设置点云数据类型
        /// </summary>
        /// <param name="dataType">数据类型（0=IMU, 1=笛卡尔高精度, 2=笛卡尔低精度, 3=球坐标）</param>
        public void SetPclDataType(byte dataType)
        {
            if (_commander == null) throw new InvalidOperationException("设备未连接");
            _commander.SetPclDataType(dataType);
        }

        /// <summary>
        /// 设置扫描模式
        /// </summary>
        /// <param name="pattern">扫描模式（0=非重复, 1=重复, 2=重复低帧率）</param>
        public void SetScanPattern(byte pattern)
        {
            if (_commander == null) throw new InvalidOperationException("设备未连接");
            _commander.SetScanPattern(pattern);
        }

        /// <summary>
        /// 设置双发射模式
        /// </summary>
        /// <param name="enable">是否启用双发射</param>
        public void SetDualEmit(bool enable)
        {
            if (_commander == null) throw new InvalidOperationException("设备未连接");
            _commander.SetDualEmit(enable);
        }

        /// <summary>
        /// 启用IMU数据
        /// </summary>
        public void EnableImuData()
        {
            if (_commander == null) throw new InvalidOperationException("设备未连接");
            _commander.EnableImuData();
        }

        /// <summary>
        /// 禁用IMU数据
        /// </summary>
        public void DisableImuData()
        {
            if (_commander == null) throw new InvalidOperationException("设备未连接");
            _commander.DisableImuData();
        }

        /// <summary>
        /// 设置安装姿态
        /// </summary>
        /// <param name="roll">横滚角（度）</param>
        /// <param name="pitch">俯仰角（度）</param>
        /// <param name="yaw">偏航角（度）</param>
        /// <param name="x">X偏移（mm）</param>
        /// <param name="y">Y偏移（mm）</param>
        /// <param name="z">Z偏移（mm）</param>
        public void SetInstallAttitude(float roll, float pitch, float yaw, int x, int y, int z)
        {
            if (_commander == null) throw new InvalidOperationException("设备未连接");
            _commander.SetInstallAttitude(roll, pitch, yaw, x, y, z);
        }

        /// <summary>
        /// 设置盲区
        /// </summary>
        /// <param name="blindSpot">盲区距离（cm，范围50-200）</param>
        public void SetBlindSpot(uint blindSpot)
        {
            if (_commander == null) throw new InvalidOperationException("设备未连接");
            _commander.SetBlindSpot(blindSpot);
        }

        /// <summary>
        /// 查询设备内部信息
        /// </summary>
        /// <param name="keys">要查询的参数键列表</param>
        public void QueryInternalInfo(KeyType[] keys)
        {
            if (_commander == null) throw new InvalidOperationException("设备未连接");
            _commander.QueryInternalInfo(keys);
        }

        /// <summary>
        /// 查询固件类型
        /// </summary>
        public void QueryFirmwareType()
        {
            if (_commander == null) throw new InvalidOperationException("设备未连接");
            _commander.QueryFirmwareType();
        }

        /// <summary>
        /// 查询固件版本
        /// </summary>
        public void QueryFirmwareVersion()
        {
            if (_commander == null) throw new InvalidOperationException("设备未连接");
            _commander.QueryFirmwareVersion();
        }

        /// <summary>
        /// 重启设备
        /// </summary>
        /// <param name="timeoutMs">延迟重启时间（毫秒），范围100~2000</param>
        public void RebootDevice(ushort timeoutMs = 100)
        {
            if (_commander == null) throw new InvalidOperationException("设备未连接");
            _commander.RebootDevice(timeoutMs);
        }

        /// <summary>
        /// 设置FOV配置0
        /// </summary>
        /// <param name="yawStart">水平起始角（0.01度）</param>
        /// <param name="yawStop">水平结束角（0.01度）</param>
        /// <param name="pitchStart">垂直起始角（0.01度）</param>
        /// <param name="pitchStop">垂直结束角（0.01度）</param>
        public void SetFovConfig0(int yawStart, int yawStop, int pitchStart, int pitchStop)
        {
            if (_commander == null) throw new InvalidOperationException("设备未连接");
            _commander.SetFovConfig0(yawStart, yawStop, pitchStart, pitchStop);
        }

        /// <summary>
        /// 设置FOV配置1
        /// </summary>
        /// <param name="yawStart">水平起始角（0.01度）</param>
        /// <param name="yawStop">水平结束角（0.01度）</param>
        /// <param name="pitchStart">垂直起始角（0.01度）</param>
        /// <param name="pitchStop">垂直结束角（0.01度）</param>
        public void SetFovConfig1(int yawStart, int yawStop, int pitchStart, int pitchStop)
        {
            if (_commander == null) throw new InvalidOperationException("设备未连接");
            _commander.SetFovConfig1(yawStart, yawStop, pitchStart, pitchStop);
        }

        /// <summary>
        /// 设置开机后的工作模式
        /// </summary>
        /// <param name="workMode">开机工作模式（0=默认, 1=正常, 2=唤醒）</param>
        public void SetWorkModeAfterBoot(byte workMode)
        {
            if (_commander == null) throw new InvalidOperationException("设备未连接");
            _commander.SetWorkModeAfterBoot(workMode);
        }

        /// <summary>
        /// 启用窗口加热
        /// 对应LidarCommander.EnableGlassHeat()
        /// </summary>
        public void EnableGlassHeat()
        {
            if (_commander == null) throw new InvalidOperationException("设备未连接");
            _commander.EnableGlassHeat();
        }

        /// <summary>
        /// 禁用窗口加热
        /// 对应LidarCommander.DisableGlassHeat()
        /// </summary>
        public void DisableGlassHeat()
        {
            if (_commander == null) throw new InvalidOperationException("设备未连接");
            _commander.DisableGlassHeat();
        }

        /// <summary>
        /// 启用强制加热
        /// 对应LidarCommander.StartForcedHeating()
        /// </summary>
        public void StartForcedHeating()
        {
            if (_commander == null) throw new InvalidOperationException("设备未连接");
            _commander.StartForcedHeating();
        }

        /// <summary>
        /// 停止强制加热
        /// 对应LidarCommander.StopForcedHeating()
        /// </summary>
        public void StopForcedHeating()
        {
            if (_commander == null) throw new InvalidOperationException("设备未连接");
            _commander.StopForcedHeating();
        }

        #endregion

        #region 获取已发现设备

        /// <summary>
        /// 获取已发现的所有设备信息的副本
        /// </summary>
        /// <returns>设备信息列表</returns>
        public List<LidarDeviceInfo> GetDiscoveredDevices()
        {
            lock (_syncRoot)
            {
#if NET45_OR_GREATER
                return new List<LidarDeviceInfo>(_discoveredDevices);
#elif NET9_0_OR_GREATER
                return [.. _discoveredDevices];
#endif
            }
        }

        #endregion

        #region 内部事件处理

        /// <summary>
        /// 处理设备发现事件
        /// 将新发现的设备添加到列表，并触发外部事件
        /// </summary>
        private void OnDeviceDiscovered(object
             //.net 9框架下使返回对象可为空
#if NET9_0_OR_GREATER
            ?
#endif
             sender, DeviceDiscoveredEventArgs e)
        {
            // 检查是否已存在相同序列号的设备（避免重复添加）
            lock (_syncRoot)
            {
                bool exists = _discoveredDevices.Any(d => d.SerialNumberString == e.SerialNumberString);
                if (!exists)
                {
                    var deviceInfo = new LidarDeviceInfo(
                        _handleCounter++,
                        e.DeviceType,
                        e.SerialNumber,
                        e.LidarIp,
                        e.CommandPort,
                        e.RemoteEndPoint);

                    _discoveredDevices.Add(deviceInfo);
                    DeviceDiscovered?.Invoke(this, deviceInfo);
                }
            }
        }

        /// <summary>
        /// 处理命令端口ACK响应
        /// 解析ACK响应并触发外部事件
        /// </summary>
        private void OnCommandAckReceived(object
             //.net 9框架下使返回对象可为空
#if NET9_0_OR_GREATER
            ?
#endif
             sender, byte[] data)
        {
            if (data == null || data.Length < SdkPacketBuilder.HeaderSize)
                return;

            // 解析命令ID以区分不同类型的ACK
            ushort cmdId = BitConverter.ToUInt16(data, 8);

            if (cmdId == (ushort)CommandType.ParameterConfiguration)
            {
                // 参数配置命令的ACK
                var response = AckResponseParser.ParseAsyncControlResponseFromPacket(data);
                AckResponseReceived?.Invoke(this, response);
            }
            else if (cmdId == (ushort)CommandType.RadarInfoQuery)
            {
                // 信息查询命令的ACK
                var response = AckResponseParser.ParseInternalInfoResponseFromPacket(data);
                DeviceStatusUpdated?.Invoke(this, response);
            }
        }

        /// <summary>
        /// 处理命令端口设备推送消息
        /// </summary>
        private void OnCommandPushReceived(object
             //.net 9框架下使返回对象可为空
#if NET9_0_OR_GREATER
            ?
#endif
             sender, byte[] data)
        {
            PushMessageReceived?.Invoke(this, data);
        }

        /// <summary>
        /// 处理点云数据接收
        /// </summary>
        private void OnPointCloudDataReceived(object
            //.net 9框架下使返回对象可为空
#if NET9_0_OR_GREATER
            ?
#endif
            sender, byte[] data)
        {
            PointCloudDataReceived?.Invoke(this, data);
        }

        /// <summary>
        /// 处理IMU数据接收
        /// </summary>
        private void OnImuDataReceived(object
   //.net 9框架下使返回对象可为空
#if NET9_0_OR_GREATER
            ?
#endif
            sender, byte[] data)
        {
            ImuDataReceived?.Invoke(this, data);
        }

        #endregion

        #region 资源释放

        /// <summary>
        /// 释放所有资源
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// 释放资源的核心方法
        /// </summary>
        /// <param name="disposing">是否由用户代码调用</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                // 释放托管资源
                Disconnect();

                // 停止设备发现
                _discovery?.Dispose();

                // 停止UDP监听
                _monitorCts?.Cancel();
                _monitorCts?.Dispose();
                _udpComm?.Dispose();
            }

            _disposed = true;
        }

        #endregion
    }
}
