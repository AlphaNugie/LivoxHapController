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
    /// 1. 绑定本地56001端口（DetectionListenPort），既用于发送广播，也用于接收响应
    /// 2. 每秒向 255.255.255.255:56000 发送空的搜索命令包
    /// 3. 扫描仪收到广播后，会将响应包发回发送方的源端口（即56001）
    /// 4. 在同一客户端上接收并解析响应，获取设备SN、IP、端口等信息
    /// 
    /// 注意：发送与接收必须使用同一个UdpClient，因为扫描仪会向广播包的源端口回复
    /// </summary>
    public class LidarDiscovery : IDisposable
    {
        #region 私有字段

        /// <summary>是否正在运行</summary>
        private bool _isRunning;

        /// <summary>
        /// 统一的UDP客户端，同时用于发送广播和接收响应
        /// 绑定56001端口，发送广播时源端口为56001，扫描仪回复也发往56001
        /// </summary>
        private UdpClient
            //.net 9框架下使返回对象可为空
#if NET9_0_OR_GREATER
            ?
#endif
        _udpClient;

        /// <summary>
        /// 统一工作线程，在同一循环中交替执行发送广播和接收响应
        /// 合并后不再需要独立的广播线程和监听线程
        /// </summary>
        private Thread
            //.net 9框架下使返回对象可为空
#if NET9_0_OR_GREATER
            ?
#endif
        _workerThread;

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
        /// 绑定56001端口，开始广播搜索并接收设备响应
        /// </summary>
        /// <param name="hostIp">本机IP地址（用于绑定监听端口），为空则绑定所有接口</param>
        public void Start(string hostIp = "")
        {
            if (_isRunning) return;

            _isRunning = true;

            // 创建统一的UDP客户端，绑定56001端口
            // 该客户端既用于发送广播（源端口56001），也用于接收扫描仪的回复（目标端口56001）
            if (string.IsNullOrWhiteSpace(hostIp))
                _udpClient = new UdpClient(SdkPacketBuilder.DetectionListenPort);
            else
                _udpClient = new UdpClient(
                    new IPEndPoint(IPAddress.Parse(hostIp), SdkPacketBuilder.DetectionListenPort));

            // 启用广播发送
            _udpClient.EnableBroadcast = true;
            // 设置发送超时，防止Send阻塞
            _udpClient.Client.SendTimeout = 1000;
            // 设置接收超时，以便在等待响应时能定期检查_isRunning标志
            _udpClient.Client.ReceiveTimeout = 1000;

            // 启动统一工作线程（发送+接收在同一循环中）
            _workerThread = new Thread(DiscoveryWorker)
            {
                IsBackground = true,
                Name = "LidarDiscovery-Worker"
            };
            _workerThread.Start();
        }

        /// <summary>
        /// 停止设备发现
        /// </summary>
        public void Stop()
        {
            _isRunning = false;

            _workerThread?.Join(2000);

            _udpClient?.Close();
            _udpClient = null;
            _workerThread = null;
        }

        #endregion

        #region 私有工作线程

        /// <summary>
        /// 统一工作线程
        /// 在同一循环中交替执行：发送广播 → 接收响应，与C++ DeviceManager::DetectionLidars()逻辑一致
        /// 对应C++中的 while(!is_stop_detection_) { Detection(); 接收响应; sleep(1s); }
        /// </summary>
        private void DiscoveryWorker()
        {
            while (_isRunning)
            {
                // 1. 发送广播搜索包
                try
                {
                    SendDiscoveryBroadcast();
                }
                catch (Exception)
                {
                    // 发送失败时忽略，下一秒重试
                }

                // 2. 尝试接收响应（非阻塞方式，超时后继续下一轮循环）
                ReceiveResponses();

                // 3. 每秒循环一次（与C++一致）
                Thread.Sleep(1000);
            }
        }

        /// <summary>
        /// 接收所有待处理的广播响应
        /// 在发送广播后调用，持续接收直到无更多数据或超时
        /// </summary>
        private void ReceiveResponses()
        {
            while (_isRunning)
            {
                try
                {
                    // 确保 _udpClient 已经被初始化
                    if (_udpClient == null)
                        throw new InvalidOperationException("_udpClient 未初始化");

                    IPEndPoint
                        //.net 9框架下使返回对象可为空
#if NET9_0_OR_GREATER
                        ?
#endif
                    remoteEP = null;
                    byte[] responseData = _udpClient.Receive(ref remoteEP);

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
                            // 将协议中的byte设备类型转换为DeviceType枚举
                            var deviceType = DeviceTypeExtensions.SafeParse(response.DevType)
                                ?? (DeviceType)response.DevType;
                            var args = new DeviceDiscoveredEventArgs(
                                deviceType,
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
                    // ReceiveTimeout到期是正常的，跳出内层循环回到外层
                    break;
                }
                catch (Exception)
                {
                    // 其他异常忽略，继续尝试接收
                    break;
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
            // 扫描仪收到后会将响应包发回本客户端绑定的源端口（56001）
#if NET45_OR_GREATER
            IPEndPoint broadcastEP = new IPEndPoint(IPAddress.Broadcast, SdkPacketBuilder.DetectionPort);
#elif NET9_0_OR_GREATER
            IPEndPoint broadcastEP = new(IPAddress.Broadcast, SdkPacketBuilder.DetectionPort);
#endif
            _udpClient?.Send(packet, packet.Length, broadcastEP);
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
        /// <summary>设备类型枚举（见 DeviceType 枚举定义）</summary>
        public DeviceType DeviceType { get; private set; }

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
        /// <param name="deviceType">设备类型枚举</param>
        /// <param name="serialNumber">序列号（16字节）</param>
        /// <param name="lidarIp">雷达IP（4字节）</param>
        /// <param name="commandPort">命令端口</param>
        /// <param name="remoteEndPoint">响应来源端点</param>
        public DeviceDiscoveredEventArgs(DeviceType deviceType, byte[] serialNumber, byte[] lidarIp,
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
    /// <param name="deviceType">设备类型枚举</param>
    /// <param name="serialNumber">序列号（16字节）</param>
    /// <param name="lidarIp">雷达IP（4字节）</param>
    /// <param name="commandPort">命令端口</param>
    /// <param name="remoteEndPoint">响应来源端点</param>
    public class DeviceDiscoveredEventArgs(DeviceType deviceType, byte[] serialNumber, byte[] lidarIp,
        ushort commandPort, IPEndPoint remoteEndPoint) : EventArgs
    {
        /// <summary>设备类型枚举（见 DeviceType 枚举定义）</summary>
        public DeviceType DeviceType { get; private set; } = deviceType;

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

        /// <summary>
        /// 设备类型名称
        /// 使用 DeviceTypeExtensions.GetDisplayName() 获取可读名称，复用枚举扩展方法
        /// </summary>
        public string DeviceTypeName
        {
            get { return DeviceType.GetDisplayName(); }
        }
    }
}
