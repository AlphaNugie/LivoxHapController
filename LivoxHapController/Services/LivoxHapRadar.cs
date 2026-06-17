using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using LivoxHapController.Config;
using LivoxHapController.Enums;
using LivoxHapController.Models;
using LivoxHapController.Models.DataPoints;
using LivoxHapController.Services.Parsers;
using LivoxHapController.Utilities;

#if NET45_OR_GREATER
using System.IO;
#endif

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
        private CancellationTokenSource? _monitorCts;
#elif NET45_OR_GREATER
        private CancellationTokenSource _monitorCts;
#endif

        #region 录制与播放

        /// <summary>点云数据录制器（按需创建）</summary>
        private PointCloudRecorder
            //.net 9框架下使返回对象可为空
#if NET9_0_OR_GREATER
            ?
#endif
            _recorder;

        /// <summary>点云数据播放器（按需创建）</summary>
        private PointCloudPlayer
            //.net 9框架下使返回对象可为空
#if NET9_0_OR_GREATER
            ?
#endif
            _player;

        #endregion

        #region 点云处理私有字段

        /// <summary>点云处理开关，默认关闭。开启后内部自动解析点云数据包、执行坐标变换、写入缓冲区</summary>
        private bool _enablePointCloudProcessing;

        /// <summary>每包包含的点数（固定96点）</summary>
        private const int PointsPerPkg = 96;

        /// <summary>每毫秒包含的包数（HAP点发送速率452KHz，96点/包 ≈ 4包/ms）</summary>
        private const int PkgsPerMillisec = 4;

        /// <summary>每毫秒包含的点数</summary>
        private const int PointsPerMillisec = PointsPerPkg * PkgsPerMillisec;

        /// <summary>帧时间字段（毫秒），非重复扫描下帧时间越长扫描细节越丰富</summary>
        private int _frameTime = 100;

        /// <summary>每帧的包数</summary>
        private int _pkgsPerFrame = PointsPerMillisec * 100;

        /// <summary>每帧的点数</summary>
        private int _ptsPerFrame = PointsPerMillisec * 100;

        /// <summary>最新一次接收到点云数据的时间</summary>
        private DateTime _lastReceivedTime = DateTime.MinValue;

        /// <summary>最新一次点云数据接收后处理完成的时间</summary>
        private DateTime _lastMergedTime = DateTime.MinValue;

        /// <summary>最新一个点云数据包的包头信息（格式化字符串，调试用）</summary>
        private string _packetHeader = string.Empty;

        /// <summary>最新一个点云数据包的前280字节数据（16进制字符串，调试用）</summary>
        private string _packetData = string.Empty;

        /// <summary>最新一个点云数据包的数据类型</summary>
        private PointCloudDataType _dataType;

        /// <summary>笛卡尔坐标系点云缓存队列（线程安全）</summary>
        private readonly ConcurrentQueue<CartesianDataPoint> _cartesianRawPointsQueue = 
#if NET45_OR_GREATER
            new ConcurrentQueue<CartesianDataPoint>();
#elif NET9_0_OR_GREATER
            new();
#endif

        /// <summary>点云处理锁（保护 List 缓冲区的线程安全）</summary>
#if NET9_0_OR_GREATER
        private readonly Lock _pointCloudLock = new();
#elif NET45_OR_GREATER
        private readonly object _pointCloudLock = new object();
#endif

        /// <summary>笛卡尔坐标系点云List缓冲区（当不使用双缓冲时）</summary>
#if NET45_OR_GREATER
        private readonly List<CartesianDataPoint> _cartesianRawPoints = new List<CartesianDataPoint>();
#elif NET9_0_OR_GREATER
        private readonly List<CartesianDataPoint> _cartesianRawPoints = [];
#endif

        #endregion

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

        /// <summary>
        /// 点云数据录制器
        /// 首次访问时自动创建，为 null 时录制功能不消耗任何资源
        /// </summary>
        public PointCloudRecorder Recorder
        {
            get
            {
#if NET45_OR_GREATER
                if (_recorder == null)
                    _recorder = new PointCloudRecorder();
#elif NET9_0_OR_GREATER
                _recorder ??= new PointCloudRecorder();
#endif
                return _recorder;
            }
        }

        /// <summary>
        /// 点云数据播放器
        /// 首次访问时自动创建，为 null 时播放功能不消耗任何资源
        /// </summary>
        public PointCloudPlayer Player
        {
            get
            {
#if NET45_OR_GREATER
                if (_player == null)
                    _player = new PointCloudPlayer();
#elif NET9_0_OR_GREATER
                _player ??= new PointCloudPlayer();
#endif
                return _player;
            }
        }

        #region 点云处理属性

        /// <summary>
        /// 点云处理开关（默认关闭）
        /// 开启后，收到点云数据时内部自动执行：解析数据包 → 坐标变换 → 写入缓冲区
        /// 关闭时仅触发 PointCloudDataReceived 事件，不做内部处理，节省资源
        /// </summary>
        public bool EnablePointCloudProcessing
        {
            get { return _enablePointCloudProcessing; }
            set
            {
                _enablePointCloudProcessing = value;
                // 开启时自动启动缓存监控，关闭时停止监控
                if (value)
                    StartCacheMonitor();
                else
                    StopCacheMonitor();
        }
        }

        /// <summary>
        /// 最新一次接收到点云数据的时间
        /// 假如慢于当前时间2秒，则认为未接收
        /// </summary>
        public DateTime LastReceivedTime { get { return _lastReceivedTime; } }

        /// <summary>
        /// 最新一次点云数据接收后处理完成的时间
        /// </summary>
        public DateTime LastMergedTime { get { return _lastMergedTime; } }

        /// <summary>
        /// 当前是否正在接收点云数据
        /// true：正在接收点云数据；false：未接收到点云数据
        /// </summary>
        public bool CurrentlyReceiving
        {
            get { return (DateTime.Now - _lastReceivedTime).TotalSeconds < 2; }
        }

        /// <summary>
        /// 最新一个点云数据包的包头信息（格式化字符串，调试用）
        /// </summary>
        public string PacketHeader { get { return _packetHeader; } }

        /// <summary>
        /// 最新一个点云数据包的前280字节数据（16进制字符串，调试用）
        /// </summary>
        public string PacketData { get { return _packetData; } }

        /// <summary>
        /// 最新一个点云数据包的数据类型
        /// </summary>
        public PointCloudDataType DataType { get { return _dataType; } }

        /// <summary>
        /// 帧速率（毫秒）
        /// 因为HAP雷达使用非重复扫描，因此帧速率时间越长，扫描图像细节越丰富
        /// </summary>
        public int FrameTime
        {
            get { return _frameTime; }
            set
            {
                if (value < 0) return;
                _frameTime = value;
                PkgsPerFrame = PkgsPerMillisec * _frameTime;
            }
        }

        /// <summary>
        /// 每帧的包数
        /// HAP雷达使用非重复扫描，因此包数越多，扫描细节越丰富
        /// 每包含96个点，HAP雷达点发送速率为452KHZ，因此当帧速率为1ms时，每帧包数为452K/1K/96=4
        /// </summary>
        public int PkgsPerFrame
        {
            get { return _pkgsPerFrame; }
            private set
            {
                _pkgsPerFrame = value;
                PointsPerFrame = PointsPerPkg * _pkgsPerFrame;
            }
        }

        /// <summary>
        /// 每帧的点数
        /// 每包含96个点，因此每帧点数 = 96 * 每帧包数
        /// </summary>
        public int PointsPerFrame
        {
            get { return _ptsPerFrame; }
            private set { _ptsPerFrame = value; }
        }

        #endregion

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
        /// 初始化雷达管理器（从配置文件加载）
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

            // 使用 AppConfigBuilder 从文件加载配置
            var config = AppConfigBuilder.FromFile(configFile).Build();

            // 调用共用的核心初始化逻辑
            InitCore(config, coordTransParamSet);
        }

        /// <summary>
        /// 初始化雷达管理器（从AppConfig对象加载，支持可选参数覆盖）
        /// 无需配置文件，直接传入AppConfig对象进行初始化；
        /// 可选参数非null/非默认值时将覆盖appConfig中对应字段
        /// </summary>
        /// <param name="appConfig">应用程序配置对象，作为基础配置（为null时使用默认值）</param>
        /// <param name="coordTransParamSet">坐标变换参数集（可选）</param>
        /// <param name="masterSdk">是否启用主SDK（可选，覆盖appConfig.MasterSdk）</param>
        /// <param name="walkChangeThres">步态切换阈值（可选，覆盖HAP/MID360的WalkChangedThreshold）</param>
        /// <param name="lidarIp">LiDAR设备IP地址（可选，覆盖HAP/MID360的LidarIp）</param>
        /// <param name="hostIp">主机IP地址（可选，覆盖HAP/MID360的HostIp）</param>
        /// <param name="pointDataPort">点云数据端口号（可选，覆盖HAP/MID360的PointDataPort）</param>
        public void Initialize(AppConfig appConfig, CoordTransParamSet
             //.net 9框架下使返回对象可为空
#if NET9_0_OR_GREATER
            ?
#endif
             coordTransParamSet = null,
            bool? masterSdk = null, double? walkChangeThres = null,
            string lidarIp = "", string hostIp = "", int? pointDataPort = null)
        {
            // 使用 AppConfigBuilder 从对象构建配置，并应用可选参数覆盖
            var config = AppConfigBuilder.FromConfig(appConfig)
                .WithMasterSdk(masterSdk)
                .WithWalkChangeThreshold(walkChangeThres)
                .WithLidarIp(lidarIp)
                .WithHostIp(hostIp)
                .WithPointDataPort(pointDataPort)
                .Build();

            // 调用共用的核心初始化逻辑
            InitCore(config, coordTransParamSet);
        }

        /// <summary>
        /// 共用的核心初始化逻辑
        /// 两个Initialize重载最终都调用此方法完成UDP通信器和设备发现服务的初始化
        /// </summary>
        /// <param name="config">已构建完成的AppConfig对象</param>
        /// <param name="coordTransParamSet">坐标变换参数集（可选）</param>
        private void InitCore(AppConfig config, CoordTransParamSet
             //.net 9框架下使返回对象可为空
#if NET9_0_OR_GREATER
            ?
#endif
             coordTransParamSet)
        {
            // 保存配置
            Config = config;

            // 保存坐标变换参数
            //if (coordTransParamSet != null)
            //    CoordTransParamSet = coordTransParamSet;
            CoordTransParamSet = coordTransParamSet ?? new CoordTransParamSet();

            // 从配置中读取帧时间（HostNetInfo.FrameTime）
            if (config?.HapConfig?.HostNetInfo?.Count > 0)
                FrameTime = config.HapConfig.HostNetInfo[0].FrameTime;

            // 初始化UDP通信处理器
            _udpComm = new UdpCommunicator();

            // 如果配置中存在LidarIp列表，设置来源IP过滤器
            // 条件A：已初始化且LidarIp存在至少一个值 → 启用过滤，仅接受白名单内IP
            // 条件A不满足 → 不过滤，接受所有来源
            if (config?.HapConfig?.HostNetInfo?.Count > 0)
            {
                var lidarIps = config.HapConfig.HostNetInfo[0].LidarIp;
                if (lidarIps != null && lidarIps.Count > 0)
                {
                    _udpComm.SetPointCloudSourceIpFilter(lidarIps);
                    // 注册到进程内全局路由表，使其他实例可将误发的数据转发过来
                    RadarRegistry.Register(lidarIps, _udpComm);
                }
            }

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
        /// 通过设备ip连接到已发现的设备（假如给的设备ip为空，则使用配置中 HostNetInfo 的第一个 LidarIp）
        /// </summary>
        /// <param name="ipAddress">设备ip地址，假如为空则使用配置中 HostNetInfo 的第一个 LidarIp</param>
        /// <returns>是否连接成功</returns>
        public bool ConnectByDeviceIp(string
            //.net 9框架下使返回对象可为空
#if NET9_0_OR_GREATER
            ?
#endif
  ipAddress = null)
        {
#if NET45_OR_GREATER
            ipAddress = ipAddress ?? Config?.HapConfig?.HostNetInfo?.FirstOrDefault()?.LidarIp.FirstOrDefault();
#elif NET9_0_OR_GREATER
            ipAddress ??= Config?.HapConfig?.HostNetInfo?.FirstOrDefault()?.LidarIp.FirstOrDefault();
#endif
            if (string.IsNullOrWhiteSpace(ipAddress))
                throw new ArgumentNullException(nameof(ipAddress), "提供的IP地址为空而且找不到已配置的设备ip");

            LidarDeviceInfo
            //.net 9框架下使返回对象可为空
#if NET9_0_OR_GREATER
            ?
#endif
             device;
            lock (_syncRoot)
            {
                device = _discoveredDevices.FirstOrDefault(d => d.LidarIpString.Equals(ipAddress.Trim()));
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
            // HAP (TX) 暂不支持
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
        /// <param name="stopScan">是否顺便停止扫描，true=停止扫描后断开，false=直接断开（雷达继续扫描），默认false</param>
        public void Disconnect(bool stopScan = false)
        {
            // 根据参数决定是否在断开前停止扫描
            if (stopScan && _isScanning)
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
        /// 无论是否开启点云处理，都会触发外部 PointCloudDataReceived 事件
        /// 当 EnablePointCloudProcessing 为 true 时，额外执行内部处理（解析→变换→缓冲）
        /// </summary>
        private void OnPointCloudDataReceived(object
            //.net 9框架下使返回对象可为空
#if NET9_0_OR_GREATER
            ?
#endif
            sender, byte[] data)
        {
            PointCloudDataReceived?.Invoke(this, data);

            // 若开启点云处理，执行内部处理流水线
            if (_enablePointCloudProcessing && data != null)
            {
                // 使用 Task.Run 在线程池中异步执行处理，避免阻塞UDP接收线程
                _ = Task.Run(() => MergePointCloudData(data));
            }
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

        #region 点云数据处理

        /// <summary>
        /// 合并点云数据（解析、坐标变换、写入缓冲区）
        /// 从 LivoxHapQuickStart.Merge() 迁移而来，集成到基础库内部
        /// 此方法在 Task.Run 中异步调用，不会阻塞UDP接收线程
        /// </summary>
        /// <param name="rawData">点云原始字节数据</param>
        private void MergePointCloudData(byte[] rawData)
        {
            // 解析点云数据包
            var packet = PointCloudParser.ParsePacket(rawData);
            if (packet == null) return;

            // 更新接收时间
            _lastReceivedTime = DateTime.Now;

            // 提取前280字节的16进制数据（调试用）
            int datalen = Math.Min(280, rawData.Length);
            _packetData = rawData.Take(datalen)
                .Aggregate("", (current, b) => current + b.ToString("X2") + " ").Trim();

            // 记录数据类型
            _dataType = packet.Header.DataType;

            // 按数据类型分支处理
            switch (_dataType)
            {
                case PointCloudDataType.Cartesian16Bit:
                case PointCloudDataType.Cartesian32Bit:
                    var points = packet.CartesianDataPoints.ToArray();

                    // 如果坐标转换参数集不为null，则在写入缓冲区前先进行坐标转换
                    if (CoordTransParamSet != null)
                        points = points.TransformPoints(CoordTransParamSet);

                    // 写入线程安全的并发队列
                    foreach (var point in points)
                    {
                        _cartesianRawPointsQueue.Enqueue(point);
                    }
                    break;

                case PointCloudDataType.ImuData:
                    // IMU数据暂不做内部处理，仍通过外部 ImuDataReceived 事件获取
                    break;
            }

            // 更新合并完成时间
            _lastMergedTime = DateTime.Now;

            // 格式化包头信息（调试用）
            _packetHeader = string.Format(
                "time_rcvd: {0:yyyy-MM-dd HH:mm:ss.ffffff}, time_merged: {1:yyyy-MM-dd HH:mm:ss.ffffff}, " +
                "Point cloud timestamp: {2}, udp_counter: {3}, data_num: {4}, data_type: {5}, length: {6}, frame_counter: {7}",
                _lastReceivedTime, _lastMergedTime,
                packet.Header.TimestampNanoSec, packet.Header.UdpCnt, packet.Header.DotNum,
                packet.Header.DataType, packet.Header.Length, packet.Header.FrameCnt);
        }

        /// <summary>
        /// 获取当前帧的笛卡尔坐标点云数据快照（线程安全、非破坏性）
        /// 从并发队列中复制所有点并裁剪到帧容量大小，不修改队列内容
        /// 队列的裁剪仍由后台 MonitorAndTrimCacheAsync 负责
        /// </summary>
        /// <returns>笛卡尔坐标点数组</returns>
        public CartesianDataPoint[] GetCurrentFrameOfRawPoints()
        {
            // 非破坏性快照：复制队列中所有点到数组，不清空队列
            var allPoints = _cartesianRawPointsQueue.ToArray();

            // 按帧大小裁剪（保留最新的 PointsPerFrame 个点）
            // 队列尾部是最新数据（Enqueue追加到尾部，Monitor从头部删除旧数据）
            if (allPoints.Length > _ptsPerFrame)
            {
                var result = new CartesianDataPoint[_ptsPerFrame];
                Array.Copy(allPoints, allPoints.Length - _ptsPerFrame, result, 0, _ptsPerFrame);
                return result;
            }

            return allPoints;
        }

        /// <summary>
        /// 启动缓存监控任务
        /// 定期检查缓存大小，超出帧容量时裁剪旧数据
        /// </summary>
        private void StartCacheMonitor()
        {
            if (_monitorCts != null) return; // 已在运行

            _monitorCts = new CancellationTokenSource();
            _ = MonitorAndTrimCacheAsync(_monitorCts.Token);
        }

        /// <summary>
        /// 停止缓存监控任务
        /// </summary>
        private void StopCacheMonitor()
        {
            _monitorCts?.Cancel();
            _monitorCts?.Dispose();
            _monitorCts = null;
        }

        /// <summary>
        /// 异步监控缓存队列大小并定期裁剪
        /// 每毫秒检测一次，当队列中的点数超过 PointsPerFrame 时裁剪旧数据
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>异步任务</returns>
        private async Task MonitorAndTrimCacheAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(1, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                // 裁剪并发队列：当点数超过帧容量时，丢弃旧数据
                int count = _cartesianRawPointsQueue.Count;
                if (count > _ptsPerFrame)
                {
                    int excess = count - _ptsPerFrame;
                    for (int i = 0; i < excess; i++)
                    {
                        _cartesianRawPointsQueue.TryDequeue(out _);
                    }
                }
            }
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
                // 从进程内全局路由表中注销（在断开连接之前，确保不会再被转发）
                RadarRegistry.Unregister(_udpComm);

                // 释放托管资源
                Disconnect();

                // 停止设备发现
                _discovery?.Dispose();

                // 停止缓存监控
                StopCacheMonitor();

                // 清空点云缓冲区
                while (_cartesianRawPointsQueue.TryDequeue(out _)) { }

                // 停止录制和播放
                _recorder?.Dispose();
                _recorder = null;
                _player?.Dispose();
                _player = null;

                // 停止UDP监听
                _monitorCts?.Cancel();
                _monitorCts?.Dispose();
                _udpComm?.Dispose();
            }

            _disposed = true;
        }

        #endregion

        #region 录制与播放便捷方法

        /// <summary>
        /// 开始录制点云原始数据到 .pcr 文件
        /// 便捷入口，等价于 Recorder.Start(filePath)
        /// </summary>
        /// <param name="filePath">输出文件路径（建议后缀 .pcr）</param>
        public void StartRecording(string filePath)
        {
            Recorder.Start(filePath);
        }

        /// <summary>
        /// 停止录制
        /// 便捷入口，等价于 Recorder.Stop()
        /// </summary>
        public void StopRecording()
        {
            //_recorder?.Stop();
            Recorder?.Stop();
        }

        /// <summary>
        /// 开始模拟播放 .pcr 录制文件（可指定是否循环播放，默认不循环）
        /// 数据通过 UdpCommunicator.InjectPointCloudData 注入，走与网络接收完全一致的流程
        /// </summary>
        /// <param name="filePath">.pcr 录制文件路径</param>
        /// <param name="loop">是否循环播放</param>
        public void StartPlayback(string filePath, bool loop = false)
        {
            if (_udpComm == null)
                throw new InvalidOperationException("UdpCommunicator 未初始化，请先调用 Initialize() 和 Discover()");
            Player.Start(filePath, _udpComm, loop);
        }

        /// <summary>
        /// 停止播放
        /// 便捷入口，等价于 Player.Stop()
        /// </summary>
        public void StopPlayback()
        {
            //_player?.Stop();
            Player?.Stop();
        }

        #endregion
    }
}
