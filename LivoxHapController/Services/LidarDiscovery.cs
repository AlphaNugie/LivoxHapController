using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using LivoxHapController.Enums;

namespace LivoxHapController.Services
{
    /// <summary>
    /// LiDAR设备发现服务
    /// 通过UDP广播搜索网络中的Livox LiDAR设备
    /// 对应C++ DeviceManager::Detection() + DetectionLidars()
    /// 
    /// 工作原理：
    /// 1. 每秒向 255.255.255.255:56000 发送空的搜索命令包
    /// 2. 监听 56001 端口接收设备的广播响应
    /// 3. 解析响应获取设备SN、IP、端口等信息
    /// </summary>
    public class LidarDiscovery : IDisposable
    {
        #region 私有字段

        /// <summary>是否正在运行</summary>
        private bool _isRunning;

        /// <summary>发现线程</summary>
        private Thread
            //.net 9框架下使返回对象可为空
#if NET9_0_OR_GREATER
            ?
#endif
      _discoveryThread;

        /// <summary>广播搜索用的UDP客户端</summary>
        private UdpClient
            //.net 9框架下使返回对象可为空
#if NET9_0_OR_GREATER
            ?
#endif
      _broadcastClient;

        /// <summary>监听响应的UDP客户端</summary>
        private UdpClient
            //.net 9框架下使返回对象可为空
#if NET9_0_OR_GREATER
            ?
#endif
      _listenClient;

        /// <summary>监听线程</summary>
        private Thread
            //.net 9框架下使返回对象可为空
#if NET9_0_OR_GREATER
            ?
#endif
      _listenThread;

        #endregion

        #region 事件

        /// <summary>
        /// 发现设备事件
        /// 当收到雷达的广播响应时触发
        /// </summary>
        public event EventHandler<DeviceDiscoveredEventArgs>
            //.net 9框架下使返回对象可为空
#if NET9_0_OR_GREATER
            ?
#endif
      DeviceDiscovered;

        #endregion

        #region 公开属性

        /// <summary>是否正在运行</summary>
        public bool IsRunning { get { return _isRunning; } }

        #endregion

        #region 构造与析构

        /// <summary>
        /// 构造LiDAR设备发现服务
        /// </summary>
        public LidarDiscovery()
        {
            _isRunning = false;
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            Stop();
            GC.SuppressFinalize(this);
        }

        #endregion

        #region 启动与停止

        /// <summary>
        /// 启动设备发现
        /// 开始每秒广播搜索，并监听设备响应
        /// </summary>
        /// <param name="hostIp">本机IP地址（用于绑定监听端口），为空则绑定所有接口</param>
        public void Start(string hostIp = "")
        {
            if (_isRunning) return;

            _isRunning = true;

            // 创建广播发送客户端
//#if NET45_OR_GREATER
//            _broadcastClient = new UdpClient();
//#elif NET9_0_OR_GREATER
//            _broadcastClient = new();
//#endif
            if (string.IsNullOrWhiteSpace(hostIp))
                _broadcastClient = new UdpClient(SdkPacketBuilder.DetectionListenPort + 1);
            else
                _broadcastClient = new UdpClient(
                    new IPEndPoint(IPAddress.Parse(hostIp), SdkPacketBuilder.DetectionListenPort + 1));

            _broadcastClient.EnableBroadcast = true;
            // 设置发送超时，防止Send阻塞
            _broadcastClient.Client.SendTimeout = 1000;

            // 创建响应监听客户端（绑定56001端口）
            if (string.IsNullOrWhiteSpace(hostIp))
                _listenClient = new UdpClient(SdkPacketBuilder.DetectionListenPort);
            else
                _listenClient = new UdpClient(
                    new IPEndPoint(IPAddress.Parse(hostIp), SdkPacketBuilder.DetectionListenPort));

            // 启动监听线程
            _listenThread = new Thread(ListenWorker)
            {
                IsBackground = true,
                Name = "LidarDiscovery-Listen"
            };
            _listenThread.Start();

            // 启动广播线程
            _discoveryThread = new Thread(DiscoveryWorker)
            {
                IsBackground = true,
                Name = "LidarDiscovery-Broadcast"
            };
            _discoveryThread.Start();
        }

        /// <summary>
        /// 停止设备发现
        /// </summary>
        public void Stop()
        {
            _isRunning = false;

            _discoveryThread?.Join(2000);
            _listenThread?.Join(2000);

            _broadcastClient?.Close();
            _listenClient?.Close();

            _broadcastClient = null;
            _listenClient = null;
            _discoveryThread = null;
            _listenThread = null;
        }

        #endregion

        #region 私有工作线程

        /// <summary>
        /// 广播搜索工作线程
        /// 每秒向 255.255.255.255:56000 发送搜索命令包
        /// 对应C++ DeviceManager::DetectionLidars() 中的 while(!is_stop_detection_) { Detection(); sleep(1s); }
        /// </summary>
        private void DiscoveryWorker()
        {
            while (_isRunning)
            {
                try
                {
                    SendDiscoveryBroadcast();
                }
                catch (Exception)
                {
                    // 发送失败时忽略，下一秒重试
                }

                // 每秒发送一次（与C++一致）
                Thread.Sleep(1000);
            }
        }

        /// <summary>
        /// 响应监听工作线程
        /// 持续监听56001端口，解析收到的广播响应
        /// </summary>
        private void ListenWorker()
        {
            if (_listenClient == null) return;
            _listenClient.Client.ReceiveTimeout = 1000; // 1秒超时，以便检查_isRunning

            while (_isRunning)
            {
                try
                {
                    IPEndPoint
            //.net 9框架下使返回对象可为空
#if NET9_0_OR_GREATER
            ?
#endif
      remoteEP = null;
                    byte[] responseData = _listenClient.Receive(ref remoteEP);

                    if (responseData != null && responseData.Length >= 24)
                    {
                        // 使用已有方法解析广播响应
                        var packetHeader = Parsers.ProtocolParser.ParseControlHeader(responseData);
                        int dataLength = packetHeader.Length - SdkPacketBuilder.HeaderSize;

                        if (dataLength >= 24 && packetHeader.CmdId == CommandType.BroadcastDiscovery)
                        {
                            // 提取数据段（偏移24开始）
                            byte[] dataSegment = new byte[dataLength];
                            Buffer.BlockCopy(responseData, SdkPacketBuilder.HeaderSize, dataSegment, 0, dataLength);

                            // 解析广播响应数据段
                            var response = Parsers.ProtocolParser.ParseBroadcastResponse(dataSegment);

                            // 触发设备发现事件
                            var args = new DeviceDiscoveredEventArgs(
                                response.DevType,
                                response.SerialNumber,
                                response.LidarIp,
                                response.CmdPort,
                                remoteEP);
                            DeviceDiscovered?.Invoke(this, args);
                        }
                    }
                }
                catch (SocketException)
                {
                    // ReceiveTimeout到期是正常的，继续循环
                }
                catch (Exception)
                {
                    // 其他异常忽略
                }
            }
        }

        /// <summary>
        /// 发送单次广播搜索包
        /// 对应C++ DeviceManager::Detection() 的完整逻辑：
        /// 1. 构建空的SdkPacket命令包（cmd_id=0x0000, data_len=0）
        /// 2. 通过UDP发送到 255.255.255.255:56000
        /// </summary>
        private void SendDiscoveryBroadcast()
        {
            // 构建空数据段的搜索命令包
            // cmd_id = 0x0000 (kCommandIDLidarSearch)
            // cmd_type = 0 (kCommandTypeCmd)
            // sender_type = 0 (kHostSend)
            // data = null (空数据段)
            byte[] packet = SdkPacketBuilder.BuildEmptyCommand(CommandType.BroadcastDiscovery);

            // 发送到广播地址的56000端口
#if NET45_OR_GREATER
            IPEndPoint broadcastEP = new IPEndPoint(IPAddress.Broadcast, SdkPacketBuilder.DetectionPort);
#elif NET9_0_OR_GREATER
            IPEndPoint broadcastEP = new(IPAddress.Broadcast, SdkPacketBuilder.DetectionPort);
#endif
            _broadcastClient?.Send(packet, packet.Length, broadcastEP);
        }

        #endregion
    }

#if NET45_OR_GREATER
    /// <summary>
    /// 设备发现事件参数
    /// 当发现新的LiDAR设备时传递设备信息
    /// </summary>
    public class DeviceDiscoveredEventArgs : EventArgs
    {
        /// <summary>设备类型（见LivoxLidarDeviceType枚举）</summary>
        public byte DeviceType { get; private set; }

        /// <summary>设备序列号（16字节ASCII）</summary>
        public byte[] SerialNumber { get; private set; }

        /// <summary>雷达IP地址（4字节）</summary>
        public byte[] LidarIp { get; private set; }

        /// <summary>雷达命令端口</summary>
        public ushort CommandPort { get; private set; }

        /// <summary>响应来源的远程端点</summary>
        public IPEndPoint RemoteEndPoint { get; private set; }

        /// <summary>
        /// 构造设备发现事件参数
        /// </summary>
        /// <param name="deviceType">设备类型</param>
        /// <param name="serialNumber">序列号（16字节）</param>
        /// <param name="lidarIp">雷达IP（4字节）</param>
        /// <param name="commandPort">命令端口</param>
        /// <param name="remoteEndPoint">响应来源端点</param>
        public DeviceDiscoveredEventArgs(byte deviceType, byte[] serialNumber, byte[] lidarIp,
            ushort commandPort, IPEndPoint remoteEndPoint)
        {
            DeviceType = deviceType;
            SerialNumber = serialNumber;
            LidarIp = lidarIp;
            CommandPort = commandPort;
            RemoteEndPoint = remoteEndPoint;
        }
#elif NET9_0_OR_GREATER
    /// <summary>
    /// 设备发现事件参数
    /// 当发现新的LiDAR设备时传递设备信息
    /// </summary>
    /// <remarks>
    /// 构造设备发现事件参数
    /// </remarks>
    /// <param name="deviceType">设备类型</param>
    /// <param name="serialNumber">序列号（16字节）</param>
    /// <param name="lidarIp">雷达IP（4字节）</param>
    /// <param name="commandPort">命令端口</param>
    /// <param name="remoteEndPoint">响应来源端点</param>
    public class DeviceDiscoveredEventArgs(byte deviceType, byte[] serialNumber, byte[] lidarIp,
        ushort commandPort, IPEndPoint remoteEndPoint) : EventArgs
    {
        /// <summary>设备类型（见LivoxLidarDeviceType枚举）</summary>
        public byte DeviceType { get; private set; } = deviceType;

        /// <summary>设备序列号（16字节ASCII）</summary>
        public byte[] SerialNumber { get; private set; } = serialNumber;

        /// <summary>雷达IP地址（4字节）</summary>
        public byte[] LidarIp { get; private set; } = lidarIp;

        /// <summary>雷达命令端口</summary>
        public ushort CommandPort { get; private set; } = commandPort;

        /// <summary>响应来源的远程端点</summary>
        public IPEndPoint RemoteEndPoint { get; private set; } = remoteEndPoint;
#endif

        /// <summary>雷达IP地址的点分十进制字符串</summary>
        public string LidarIpString
        {
            get
            {
                if (LidarIp == null || LidarIp.Length < 4) return "0.0.0.0";
                return string.Format("{0}.{1}.{2}.{3}", LidarIp[0], LidarIp[1], LidarIp[2], LidarIp[3]);
            }
        }

        /// <summary>设备序列号的字符串形式</summary>
        public string SerialNumberString
        {
            get
            {
                if (SerialNumber == null) return string.Empty;
                // 截取到第一个'\0'字符
                int len = Array.IndexOf(SerialNumber, (byte)0);
                if (len < 0) len = SerialNumber.Length;
                return System.Text.Encoding.ASCII.GetString(SerialNumber, 0, len);
            }
        }

        /// <summary>设备类型名称</summary>
        public string DeviceTypeName
        {
            get
            {
#if NET45_OR_GREATER
                switch (DeviceType)
                {
                    case 1: return "Mid40";
                    case 6: return "Mid70";
                    case 7: return "Avia";
                    case 9: return "Mid360";
                    case 10: return "IndustrialHAP";
                    case 15: return "HAP";
                    case 16: return "PA";
                    default: return string.Format("Unknown({0})", DeviceType);
                }
#elif NET9_0_OR_GREATER
                return DeviceType switch
                {
                    1 => "Mid40",
                    6 => "Mid70",
                    7 => "Avia",
                    9 => "Mid360",
                    10 => "IndustrialHAP",
                    15 => "HAP",
                    16 => "PA",
                    _ => string.Format("Unknown({0})", DeviceType),
                };
#endif
            }
        }
    }
}
