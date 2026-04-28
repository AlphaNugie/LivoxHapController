//假如使用双缓冲管理器 Buffer Manager，则取消此注释
//#define BM

using System.Diagnostics;
using LivoxHapController.Enums;
using LivoxHapController.Models;
using LivoxHapController.Models.DataPoints;
using LivoxHapController.Services;
using LivoxHapController.Services.Parsers;
using LivoxHapController.Config;
using System.Collections.Concurrent;
using System.IO;

#if NET45_OR_GREATER
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
#endif

namespace LivoxHapController.Test
{
    /// <summary>
    /// Livox激光扫描仪QuickStart测试
    /// 提供两种使用模式：
    /// 1. 完整流程模式（推荐）：使用 LivoxHapRadar 管理类，自动完成设备发现→连接→配置→扫描
    /// 2. 仅监听模式（兼容旧版）：仅启动UDP监听接收点云数据，不执行设备发现和命令控制
    /// </summary>
    public class LivoxHapQuickStart
    {
        #region 常量定义

        /// <summary>
        /// 每包包含的点数为96
        /// </summary>
        public const int POINTS_PER_PKG = 96;

        /// <summary>
        /// 每毫秒包含的包数为4
        /// </summary>
        public const int PKGS_PER_MILLISEC = 4;

        /// <summary>
        /// 每毫秒包含的点数
        /// </summary>
        public const int POINTS_PER_MILLISEC = POINTS_PER_PKG * PKGS_PER_MILLISEC;

        #endregion

        #region 私有字段

#if NET45_OR_GREATER
        /// <summary>UDP通信处理器（仅监听模式使用）</summary>
        private static readonly UdpCommunicator _udpComm = new UdpCommunicator();
#elif NET9_0_OR_GREATER
        /// <summary>UDP通信处理器（仅监听模式使用）</summary>
        private static readonly UdpCommunicator _udpComm = new();
#endif

        /// <summary>
        /// 同步锁对象（确保跨线程操作原子性）
        /// </summary>
#if NET9_0_OR_GREATER
        private static readonly Lock _syncRoot = new();
#elif NET45_OR_GREATER
        private static readonly object _syncRoot = new object();
#endif

        /// <summary>
        /// LivoxHapRadar 管理类实例（完整流程模式使用）
        /// </summary>
#if NET9_0_OR_GREATER
        private static LivoxHapRadar? _radar;
#elif NET45_OR_GREATER
        private static LivoxHapRadar _radar;
#endif

        /// <summary>
        /// 当前是否使用完整流程模式
        /// </summary>
        private static bool _useFullMode;

        #endregion

        #region 属性

        /// <summary>
        /// 设备的三轴角度旋转与空间位移参数集
        /// </summary>
        public static CoordTransParamSet CoordTransParamSet { get; private set; } = new CoordTransParamSet();

        /// <summary>
        /// 最新一次接收到点云数据的时间（假如慢于当前时间2秒，则认为未接收）
        /// </summary>
        public static DateTime LastReceivedTime { get; private set; } = DateTime.MinValue;

        /// <summary>
        /// 最新一次点云数据接收后处理完成的时间
        /// </summary>
        public static DateTime LastMergedTime { get; private set; } = DateTime.MinValue;

        /// <summary>
        /// 当前是否正在接收点云数据
        /// <para/>true：正在接收点云数据；false：未接收到点云数据
        /// </summary>
        public static bool CurrentlyReceiving
        {
            get { return (DateTime.Now - LastReceivedTime).TotalSeconds < 2; }
        }

        /// <summary>
        /// 双缓冲的基础信息
        /// </summary>
        public static string BufferHeader { get; private set; } = string.Empty;

        /// <summary>
        /// 以太网数据包结构体简略信息
        /// </summary>
        public static string PacketHeader { get; private set; } = string.Empty;

        /// <summary>
        /// 以太网数据包体结构体内的点云数据（16进制字符串）
        /// </summary>
        public static string PacketData { get; private set; } = string.Empty;

        private static int _frameTime = 100;

        /// <summary>
        /// 帧速率（毫秒），因为HAP雷达使用非重复扫描，因此帧速率时间越长，扫描图像细节越丰富
        /// </summary>
        public static int FrameTime
        {
            get { return _frameTime; }
            set
            {
                if (value < 0) return;
                _frameTime = value;
                PkgsPerFrame = PKGS_PER_MILLISEC * _frameTime;
            }
        }

        private static int _pkgsPerFrame = PKGS_PER_MILLISEC * _frameTime;

        /// <summary>
        /// 每帧的包数，HAP雷达使用非重复扫描，因此包数越多，扫描细节越丰富
        /// <para/>每包含96个点，HAP雷达点发送速率为452KHZ，因此当帧速率为1ms时，每帧包数为452K/1K/96=4，当帧速率为1000ms时，每帧包数为4000
        /// </summary>
        public static int PkgsPerFrame
        {
            get { return _pkgsPerFrame; }
            private set
            {
                _pkgsPerFrame = value;
                PointsPerFrame = POINTS_PER_PKG * _pkgsPerFrame;
            }
        }

        private static int _ptsPerFrame = POINTS_PER_MILLISEC * _frameTime;

        /// <summary>
        /// 每帧的点数，每包含96个点，因此每帧点数 = 96 * 每帧包数；当帧速率为1ms时，每帧包数为452K/1K/96=4，每帧点数为96 * 4 = 384
        /// </summary>
        public static int PointsPerFrame
        {
            get { return _ptsPerFrame; }
            private set
            {
                _ptsPerFrame = value;
#if BM
                BufferServiceRawPoints.WindowCapacity = _ptsPerFrame;
#endif
            }
        }

        /// <summary>
        /// 返回的点的数据类型
        /// </summary>
        public static PointCloudDataType DataType { get; private set; }

        /// <summary>
        /// LivoxHapRadar 管理类实例（完整流程模式使用）
        /// </summary>
        public static LivoxHapRadar
             //.net 9框架下使返回对象可为空
#if NET9_0_OR_GREATER
            ?
#endif
            Radar
        { get { return _radar; } }

        #endregion

        #region 点云双缓冲

#if BM
        /// <summary>
        /// 双缓冲服务对象（笛卡尔坐标系高精度坐标点）
        /// </summary>
        public static DataBufferService<CartesianDataPoint> BufferServiceRawPoints { get; private set; } = new DataBufferService<CartesianDataPoint>() { WindowCapacity = _ptsPerFrame };
#else
        /// <summary>
        /// 笛卡尔坐标系高精度坐标点的缓存序列（1mm）
        /// </summary>
#if NET45_OR_GREATER
        public static List<CartesianDataPoint> CartesianRawPoints { get; private set; } = new List<CartesianDataPoint>();
#elif NET9_0_OR_GREATER
        public static List<CartesianDataPoint> CartesianRawPoints { get; private set; } = [];
#endif

        /// <summary>
        /// 笛卡尔坐标系坐标点的缓存队列
        /// </summary>
        public static ConcurrentQueue<CartesianDataPoint> CartesianRawPointsQueue { get; private set; } = new ConcurrentQueue<CartesianDataPoint>();
#endif

        #endregion

        #region 数据处理

        /// <summary>
        /// 异步合并点云数据（解析、坐标变换、写入缓冲区）
        /// </summary>
        /// <param name="e">点云原始字节数据</param>
        private static async Task MergeAsync(byte[] e)
        {
            // 异步解析数据包
            var packet = await Task.Run(() => PointCloudParser.ParsePacket(e));
            if (packet == null) return;

            // 更新状态（需加锁确保线程安全）
            lock (_syncRoot)
            {
                LastReceivedTime = DateTime.Now;
                // 取280个字节的数据并转换为16进制字符串
                int datalen = 280;
                PacketData = e.Take(datalen).Aggregate("", (current, b) => current + b.ToString("X2") + " ").Trim();
            }

            DataType = packet.Header.DataType;
            switch (DataType)
            {
                case PointCloudDataType.Cartesian16Bit:
                case PointCloudDataType.Cartesian32Bit:
                    var points = packet.CartesianDataPoints.ToArray();
                    // 假如坐标转换参数集不为null，则进行坐标转换
                    // 为增加性能，在将点云数据插入缓存前，先进行坐标转换
                    if (CoordTransParamSet != null)
                        points = await Task.Run(() =>
                            points.TransformPoints(CoordTransParamSet)
                        ).ConfigureAwait(false);

                    // 异步写入缓冲区
#if BM
                    await Task.Run(() => BufferServiceRawPoints.AddDataChunk(points));
#else
                    // 使用高效队列替代List
                    foreach (var point in points)
                    {
                        CartesianRawPointsQueue.Enqueue(point);
                    }
#endif
                    break;
                case PointCloudDataType.ImuData:
                    break;
            }
            LastMergedTime = DateTime.Now;
            PacketHeader = string.Format("time: {0:yyyy-MM-dd HH:mm:ss.fff}, Point cloud timestamp: {1}, udp_counter: {2}, data_num: {3}, data_type: {4}, length: {5}, frame_counter: {6}",
                LastReceivedTime, packet.Header.TimestampNanoSec, packet.Header.UdpCnt, packet.Header.DotNum, packet.Header.DataType, packet.Header.Length, packet.Header.FrameCnt);
        }

        /// <summary>
        /// 同步合并点云数据（解析、坐标变换、写入缓冲区）
        /// </summary>
        /// <param name="e">点云原始字节数据</param>
        private static void Merge(byte[] e)
        {
            var packet = PointCloudParser.ParsePacket(e);
            if (packet == null) return;
            LastReceivedTime = DateTime.Now;
            // 取280个字节的数据并转换为16进制字符串
            int datalen = 280;
            PacketData = e.Take(datalen).Aggregate("", (current, b) => current + b.ToString("X2") + " ").Trim();

            Console.WriteLine(PacketHeader);
            Debug.WriteLine(PacketHeader);

            DataType = packet.Header.DataType;
            switch (DataType)
            {
                case PointCloudDataType.Cartesian16Bit:
                case PointCloudDataType.Cartesian32Bit:
                    var points = packet.CartesianDataPoints.ToArray();
                    // 假如坐标转换参数集不为null，则进行坐标转换
                    // 为增加性能，在将点云数据插入缓存前，先进行坐标转换
                    if (CoordTransParamSet != null)
                        points = points.TransformPoints(CoordTransParamSet);
#if BM
                    // 双缓冲
                    BufferServiceRawPoints.AddDataChunk(points);
#else
                    // 点云List
                    lock (_syncRoot)
                    {
                        CartesianRawPoints.InsertRange(0, points);
                    }
#endif
                    break;
                case PointCloudDataType.ImuData:
                    break;
            }
            LastMergedTime = DateTime.Now;
            PacketHeader = string.Format("time_rcvd: {0:yyyy-MM-dd HH:mm:ss.ffffff}, time_merged: {1:yyyy-MM-dd HH:mm:ss.ffffff}, Point cloud timestamp: {2}, udp_counter: {3}, data_num: {4}, data_type: {5}, length: {6}, frame_counter: {7}",
                LastReceivedTime, LastMergedTime, packet.Header.TimestampNanoSec, packet.Header.UdpCnt, packet.Header.DotNum, packet.Header.DataType, packet.Header.Length, packet.Header.FrameCnt);
        }

        #endregion

        #region 事件 / 回调函数

        /// <summary>
        /// 点云数据接收回调（仅监听模式）
        /// </summary>
#if NET45_OR_GREATER
        private static async void UdpComm_PointCloudDataReceived(object sender, byte[] e)
#elif NET9_0_OR_GREATER
        private static async void UdpComm_PointCloudDataReceived(object? sender, byte[] e)
#endif
        {
            await Task.Run(() => Merge(e));
        }

        /// <summary>
        /// 点云数据接收回调（完整流程模式，来自LivoxHapRadar）
        /// </summary>
#if NET45_OR_GREATER
        private static async void Radar_PointCloudDataReceived(object sender, byte[] e)
#elif NET9_0_OR_GREATER
        private static async void Radar_PointCloudDataReceived(object? sender, byte[] e)
#endif
        {
            await Task.Run(() => Merge(e));
        }

        #endregion

        #region 启动与停止（仅监听模式 - 兼容旧版）

#if NET9_0_OR_GREATER
        private static CancellationTokenSource? _cancellationTokenSource;
#elif NET45_OR_GREATER
        private static CancellationTokenSource _cancellationTokenSource;
#endif

        /// <summary>
        /// 以配置文件启动并获取点云数据（仅监听模式，兼容旧版）
        /// 仅启动UDP监听接收点云数据，不执行设备发现和命令控制
        /// </summary>
        /// <param name="configFile">配置文件名称</param>
        /// <param name="coordTransParamSet">坐标转换参数集</param>
#if NET9_0_OR_GREATER
        public static void Start(string configFile = "hap_config.json", CoordTransParamSet? coordTransParamSet = null)
#elif NET45_OR_GREATER
        public static void Start(string configFile = "hap_config.json", CoordTransParamSet coordTransParamSet = null)
#endif
        {
            Start(configFile, out _, coordTransParamSet);
        }

        /// <summary>
        /// 以配置文件启动并获取点云数据（仅监听模式，兼容旧版）
        /// 仅启动UDP监听接收点云数据，不执行设备发现和命令控制
        /// </summary>
        /// <param name="configFile">配置文件路径或名称</param>
        /// <param name="msg">输出的消息</param>
        /// <param name="coordTransParamSet">坐标转换参数集</param>
#if NET9_0_OR_GREATER
        public static void Start(string configFile, out string msg, CoordTransParamSet? coordTransParamSet = null)
#elif NET45_OR_GREATER
        public static void Start(string configFile, out string msg, CoordTransParamSet coordTransParamSet = null)
#endif
        {
            _useFullMode = false;
            msg = string.Empty;
            if (string.IsNullOrWhiteSpace(configFile))
            {
                msg = "Config file Invalid, must input config file path.";
                goto ERROR;
            }
            string path = configFile.Contains(Path.VolumeSeparatorChar) ? configFile : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, configFile);
            if (!File.Exists(path))
            {
                msg = "Config file does not exist, check again.";
                goto ERROR;
            }
            AppConfig.Init(path);

            if (coordTransParamSet != null)
                CoordTransParamSet = coordTransParamSet;
            FrameTime = AppConfig.Instance.HapConfig.HostNetInfo[0].FrameTime;
            _udpComm.PointCloudDataReceived += new EventHandler<byte[]>(UdpComm_PointCloudDataReceived);
            _udpComm.StartListening(AppConfig.Instance.HapConfig.HostNetInfo[0]);

            // 创建 CancellationTokenSource
            _cancellationTokenSource = new CancellationTokenSource();
            // 启动异步监控缓存
            _ = MonitorAndTrimCacheAsync(_cancellationTokenSource.Token);

            msg = "Livox Quick Start Demo Start! (Listen-only mode)";

        ERROR:
            Console.WriteLine(msg);
        }

        /// <summary>
        /// 结束并停止获取点云
        /// </summary>
        public static void Stop()
        {
            // 取消 MonitorAndTrimCacheAsync
            _cancellationTokenSource?.Cancel();

            if (_useFullMode && _radar != null)
            {
                // 完整流程模式：通过LivoxHapRadar停止
                _radar.Dispose();
                _radar = null;
            }
            else
            {
                // 仅监听模式：直接停止UDP通信
                _udpComm.Dispose();
            }

            Console.WriteLine("Livox Quick Start Demo End!");
        }

        #endregion

        #region 启动与停止（完整流程模式 - 推荐）

        /// <summary>
        /// 使用完整流程模式启动（推荐）
        /// 自动完成：加载配置 → 设备发现 → 连接 → 配置 → 启动扫描
        /// 
        /// 使用流程：
        /// 1. 调用此方法启动完整流程
        /// 2. 可通过 DeviceDiscovered 事件获知发现的设备
        /// 3. 首个设备被发现后将自动连接和配置
        /// 4. 点云数据通过 GetCurrentFrameOfRawPoints() 获取
        /// </summary>
        /// <param name="configFile">配置文件路径或名称</param>
        /// <param name="coordTransParamSet">坐标转换参数集（可选）</param>
        /// <param name="autoStartScan">是否在设备连接后自动启动扫描（默认true）</param>
#if NET9_0_OR_GREATER
        public static void StartFull(string configFile = "hap_config.json", CoordTransParamSet? coordTransParamSet = null, bool autoStartScan = true)
#elif NET45_OR_GREATER
        public static void StartFull(string configFile = "hap_config.json", CoordTransParamSet coordTransParamSet = null, bool autoStartScan = true)
#endif
        {
            _useFullMode = true;
            _radar = new LivoxHapRadar();

            if (coordTransParamSet != null)
                CoordTransParamSet = coordTransParamSet;

            // 订阅点云数据事件
            _radar.PointCloudDataReceived += Radar_PointCloudDataReceived;

            // 订阅设备发现事件：首个设备发现后自动连接并配置
            _radar.DeviceDiscovered += (sender, device) =>
            {
                Console.WriteLine(string.Format("设备发现: {0}", device));

                // 首个设备自动连接
                if (!_radar.IsConnected)
                {
                    _radar.Connect(device);
                    Console.WriteLine(string.Format("已连接设备: {0}", device.LidarIpString));

                    // 配置设备：设置主机IP等
                    try
                    {
                        _radar.ConfigureFromConfig();
                        Console.WriteLine("设备配置完成");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(string.Format("设备配置失败: {0}", ex.Message));
                    }

                    // 自动启动扫描
                    if (autoStartScan)
                    {
                        try
                        {
                            _radar.StartScan();
                            Console.WriteLine("扫描已启动");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(string.Format("启动扫描失败: {0}", ex.Message));
                        }
                    }
                }
            };

            // 订阅ACK响应事件
            _radar.AckResponseReceived += (sender, response) =>
            {
                Console.WriteLine(string.Format("ACK响应: RetCode={0}, IsSuccess={1}", response.RetCode, response.IsSuccess));
            };

            // 订阅设备状态推送事件
            _radar.DeviceStatusUpdated += (sender, info) =>
            {
                Console.WriteLine(string.Format("设备状态更新: RetCode={0}, ParamCount={1}", info.RetCode,
                    info.ParamResults != null ? info.ParamResults.Length : 0));
            };

            // 初始化
            _radar.Initialize(configFile, coordTransParamSet);

            // 设置帧时间
            if (AppConfig.Instance?.HapConfig?.HostNetInfo?.Count > 0)
                FrameTime = AppConfig.Instance.HapConfig.HostNetInfo[0].FrameTime;

            // 启动设备发现（同时启动UDP监听）
            string hostIp = AppConfig.Instance?.HapConfig?.HostNetInfo?.Count > 0
                ? AppConfig.Instance.HapConfig.HostNetInfo[0].HostIp
                : "";
            _radar.Discover(hostIp);

            // 创建 CancellationTokenSource
            _cancellationTokenSource = new CancellationTokenSource();
            // 启动异步监控缓存
            _ = MonitorAndTrimCacheAsync(_cancellationTokenSource.Token);

            Console.WriteLine("Livox Quick Start Demo Start! (Full mode)");
        }

        /// <summary>
        /// 获取已发现的所有设备列表
        /// </summary>
        /// <returns>设备信息列表</returns>
        public static List<LidarDeviceInfo> GetDiscoveredDevices()
        {
            if (_radar != null)
                return _radar.GetDiscoveredDevices();
#if NET45_OR_GREATER
            return new List<LidarDeviceInfo>();
#elif NET9_0_OR_GREATER
            return [];
#endif
        }

        #endregion

        #region 获取数据快照

        /// <summary>
        /// 获取当前帧的数据快照（线程安全）（笛卡尔坐标系高精度坐标点，精度1mm）
        /// </summary>
        public static CartesianDataPoint[] GetCurrentFrameOfRawPoints()
        {
#if BM
            return BufferServiceRawPoints.SwapAndGetSnapshot();
#else
            lock (_syncRoot)
            {
#if NET45_OR_GREATER
                return CartesianRawPoints.ToArray();
#elif NET9_0_OR_GREATER
                return [.. CartesianRawPoints];
#endif
            }
#endif
        }

        #endregion

        #region 缓存监控

        /// <summary>
        /// 检测缓存列表的长度，当超过PkgsPerFrame后，在缓存列表末尾移除超出的部分，并把原始列表内的所有元素替换为缓存列表内的所有剩余元素
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>异步任务</returns>
        public static async Task MonitorAndTrimCacheAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(1, cancellationToken); // 每毫秒检测一次

#if BM
                BufferHeader = string.Format("time: {0:yyyy-MM-dd HH:mm:ss.fff}, Frame time(ms): {1}, points / Frame: {2}, actual points (high): {3}",
                    DateTime.Now, FrameTime, PointsPerFrame, BufferServiceRawPoints.SwapAndGetSnapshot().Length);
#else
                lock (_syncRoot)
                {
                    switch (DataType)
                    {
                        case PointCloudDataType.Cartesian16Bit:
                        case PointCloudDataType.Cartesian32Bit:
                            if (CartesianRawPoints.Count > PointsPerFrame)
                                CartesianRawPoints.RemoveRange(PointsPerFrame, CartesianRawPoints.Count - PointsPerFrame);
                            break;
                        default:
                            break;
                    }
                }

                BufferHeader = string.Format("time: {0:yyyy-MM-dd HH:mm:ss.fff}, Frame time(ms): {1}, data type: {2}, points / Frame: {3}, cartesian points: {4}, imu points: {5}",
                    DateTime.Now, FrameTime, DataType, PointsPerFrame, GetCurrentFrameOfRawPoints().Length, GetCurrentFrameOfRawPoints().Length);
#endif
            }
        }

        #endregion
    }
}
