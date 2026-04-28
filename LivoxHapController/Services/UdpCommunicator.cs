using LivoxHapController.Config;
using LivoxHapController.Enums;
using LivoxHapController.Services.Parsers;
using System.Net;
using System.Net.Sockets;
#if NET45_OR_GREATER
using System;
using System.Threading;
#endif

namespace LivoxHapController.Services
{
    /// <summary>
    /// UDP通信处理器
    /// 负责与Livox LiDAR设备的UDP通信，包括命令端口、点云端口和IMU端口的监听与数据分发
    /// 支持接收ACK响应和设备推送消息的区分处理
    /// </summary>
    public class UdpCommunicator : IDisposable
    {
        #region 私有字段

        /// <summary>是否正在监听</summary>
        private bool _isListening;

#if NET45_OR_GREATER
        /// <summary>
        /// 命令端口的UDP客户端
        /// internal访问级别，供LidarCommander复用以发送命令，确保命令和ACK共用同一端口
        /// </summary>
        internal UdpClient CommandClient;

        /// <summary>点云端口的UDP客户端</summary>
        private UdpClient _pointCloudClient;

        /// <summary>IMU端口的UDP客户端</summary>
        private UdpClient _imuClient;

        /// <summary>命令端口监听线程</summary>
        private Thread _listenerThread;

        /// <summary>点云端口监听线程</summary>
        private Thread _listenerThreadCldPnt;

        /// <summary>
        /// 命令客户端的线程同步锁
        /// 保护CommandClient的Send/Receive操作，防止发送线程和接收线程并发访问导致异常
        /// </summary>
        private readonly object _commandLock = new object();
#elif NET9_0_OR_GREATER
        /// <summary>
        /// 命令端口的UDP客户端
        /// internal访问级别，供LidarCommander复用以发送命令，确保命令和ACK共用同一端口
        /// </summary>
        internal UdpClient? CommandClient;

        /// <summary>点云端口的UDP客户端</summary>
        private UdpClient? _pointCloudClient;

        /// <summary>IMU端口的UDP客户端</summary>
        private UdpClient? _imuClient;

        /// <summary>命令端口监听线程</summary>
        private Thread? _listenerThread;

        /// <summary>点云端口监听线程</summary>
        private Thread? _listenerThreadCldPnt;

        /// <summary>
        /// 命令客户端的线程同步锁
        /// 保护CommandClient的Send/Receive操作，防止发送线程和接收线程并发访问导致异常
        /// </summary>
        private readonly Lock _commandLock = new();
#endif

        #endregion

        #region 事件

#if NET45_OR_GREATER
        /// <summary>
        /// 命令端口ACK响应接收事件
        /// 当收到命令端口的ACK响应包时触发（cmd_type=1）
        /// </summary>
        public event EventHandler<byte[]> CommandAckReceived;

        /// <summary>
        /// 命令端口推送消息接收事件
        /// 当收到命令端口的设备推送消息时触发（cmd_type=0, sender_type=1）
        /// </summary>
        public event EventHandler<byte[]> CommandPushReceived;

        /// <summary>
        /// 点云数据接收事件
        /// 当收到点云端口的原始数据时触发
        /// </summary>
        public event EventHandler<byte[]> PointCloudDataReceived;

        /// <summary>
        /// IMU数据接收事件
        /// 当收到IMU端口的原始数据时触发
        /// </summary>
        public event EventHandler<byte[]> ImuDataReceived;

        /// <summary>
        /// 命令端口原始数据接收事件（已弃用，保留向后兼容）
        /// 当收到命令端口的任何数据时触发
        /// </summary>
        [Obsolete("请使用 CommandAckReceived 或 CommandPushReceived 替代")]
        public event EventHandler<byte[]> CommandDataReceived;
#elif NET9_0_OR_GREATER
        /// <summary>
        /// 命令端口ACK响应接收事件
        /// 当收到命令端口的ACK响应包时触发（cmd_type=1）
        /// </summary>
        public event EventHandler<byte[]>? CommandAckReceived;

        /// <summary>
        /// 命令端口推送消息接收事件
        /// 当收到命令端口的设备推送消息时触发（cmd_type=0, sender_type=1）
        /// </summary>
        public event EventHandler<byte[]>? CommandPushReceived;

        /// <summary>
        /// 点云数据接收事件
        /// 当收到点云端口的原始数据时触发
        /// </summary>
        public event EventHandler<byte[]>? PointCloudDataReceived;

        /// <summary>
        /// IMU数据接收事件
        /// 当收到IMU端口的原始数据时触发
        /// </summary>
        public event EventHandler<byte[]>? ImuDataReceived;

        /// <summary>
        /// 命令端口原始数据接收事件（已弃用，保留向后兼容）
        /// 当收到命令端口的任何数据时触发
        /// </summary>
        [Obsolete("请使用 CommandAckReceived 或 CommandPushReceived 替代")]
        public event EventHandler<byte[]>? CommandDataReceived;
#endif

        #endregion

        #region 启动监听

        /// <summary>
        /// 使用主机网络配置信息启动UDP监听服务
        /// </summary>
        /// <param name="hostNetInfo">主机网络配置信息，包含IP和各端口设置</param>
        public void StartListening(HostNetInfo hostNetInfo)
        {
            if (hostNetInfo == null)
                StartListening();
            else
                StartListening(hostNetInfo.HostIp, hostNetInfo.CmdDataPort, hostNetInfo.PointDataPort, hostNetInfo.ImuDataPort);
        }

        /// <summary>
        /// 启动UDP监听服务
        /// 同时启动命令端口、点云端口和IMU端口的监听
        /// </summary>
        /// <param name="hostIp">本机IP地址，为空则绑定所有网络接口</param>
        /// <param name="commandPort">命令端口（默认56000）</param>
        /// <param name="pointCloudPort">点云端口（默认57000）</param>
        /// <param name="imuPort">IMU端口（默认58000）</param>
        public void StartListening(string hostIp = "", int commandPort = 56000, int pointCloudPort = 57000, int imuPort = 58000)
        {
            // 绑定命令端口（用于接收ACK响应和设备推送消息）
            // 同时也作为发送命令的端口，确保雷达的"跟随策略"将ACK回复到同一端口
            CommandClient = string.IsNullOrWhiteSpace(hostIp)
                ? new UdpClient(commandPort)
                : new UdpClient(new IPEndPoint(IPAddress.Parse(hostIp), commandPort));

            // 绑定点云端口（用于接收点云数据）
            _pointCloudClient = string.IsNullOrWhiteSpace(hostIp)
                ? new UdpClient(pointCloudPort)
                : new UdpClient(new IPEndPoint(IPAddress.Parse(hostIp), pointCloudPort));

            // 绑定IMU端口（用于接收IMU数据）
            _imuClient = string.IsNullOrWhiteSpace(hostIp)
                ? new UdpClient(imuPort)
                : new UdpClient(new IPEndPoint(IPAddress.Parse(hostIp), imuPort));

            _isListening = true;

            // 启动命令端口+IMU端口的监听线程
            _listenerThread = new Thread(ListenerWorker)
            {
                IsBackground = true,
                Name = "UdpComm-CommandImu"
            };
            _listenerThread.Start();

            // 启动点云端口的监听线程（点云数据量大，独立线程处理）
            _listenerThreadCldPnt = new Thread(ListenerWorkerCloudPoint)
            {
                IsBackground = true,
                Name = "UdpComm-PointCloud"
            };
            _listenerThreadCldPnt.Start();
        }

        #endregion

        #region 监听工作线程

        /// <summary>
        /// 命令端口和IMU端口的监听工作线程
        /// 负责处理命令端口数据（区分ACK和推送消息）以及IMU端口数据
        /// </summary>
        private void ListenerWorker()
        {
            while (_isListening)
            {
                // 处理命令端口数据
                // 使用lock保护Receive操作，与SendCommand中的Send操作互斥，确保线程安全
                if (CommandClient != null && CommandClient.Available > 0)
                {
#if NET45_OR_GREATER
                    IPEndPoint remoteEP = null;
#elif NET9_0_OR_GREATER
                    IPEndPoint? remoteEP = null;
#endif
                    byte[] data;
                    lock (_commandLock)
                    {
                        data = CommandClient.Receive(ref remoteEP);
                    }

                    // 区分ACK响应和设备推送消息
                    if (data != null && data.Length >= SdkPacketBuilder.HeaderSize)
                    {
                        // packet[10] = cmd_type: 0=命令, 1=应答
                        // packet[11] = sender_type: 0=上位机, 1=雷达
                        byte cmdType = data[10];
                        byte senderType = data[11];

                        if (cmdType == SdkPacketBuilder.CmdTypeAck)
                        {
                            // ACK响应：雷达对上位机命令的应答
                            CommandAckReceived?.Invoke(this, data);
                        }
                        else if (cmdType == SdkPacketBuilder.CmdTypeCommand && senderType == SdkPacketBuilder.SenderLidar)
                        {
                            // 设备推送消息：雷达主动推送的状态信息
                            CommandPushReceived?.Invoke(this, data);
                        }

                        // 向后兼容：同时触发旧的CommandDataReceived事件
//#pragma warning disable 618
                        CommandDataReceived?.Invoke(this, data);
//#pragma warning restore 618
                    }
                }

                // 处理IMU端口
                if (_imuClient != null && _imuClient.Available > 0)
                {
#if NET45_OR_GREATER
                    IPEndPoint remoteEP = null;
#elif NET9_0_OR_GREATER
                    IPEndPoint? remoteEP = null;
#endif
                    byte[] data = _imuClient.Receive(ref remoteEP);
                    ImuDataReceived?.Invoke(this, data);
                }

                Thread.Sleep(1); // 防止CPU占用过高
            }
        }

        /// <summary>
        /// 点云端口的监听工作线程
        /// 独立线程处理点云数据以保证数据接收的实时性
        /// </summary>
        private void ListenerWorkerCloudPoint()
        {
            while (_isListening)
            {
                // 处理点云端口
                if (_pointCloudClient != null && _pointCloudClient.Available > 0)
                {
#if NET45_OR_GREATER
                    IPEndPoint remoteEP = null;
#elif NET9_0_OR_GREATER
                    IPEndPoint? remoteEP = null;
#endif
                    byte[] data = _pointCloudClient.Receive(ref remoteEP);
                    PointCloudDataReceived?.Invoke(this, data);
                }

                Thread.Sleep(1); // 防止CPU占用过高
            }
        }

        #endregion

        #region 发送方法

        /// <summary>
        /// 通过命令端口发送UDP数据
        /// 复用已绑定的CommandClient发送，确保命令发送端口与ACK监听端口一致
        /// 根据Livox协议"跟随策略"，雷达会将ACK回复到命令的源端口，
        /// 因此必须使用同一个UdpClient发送命令和接收ACK
        /// </summary>
        /// <param name="data">要发送的数据字节数组</param>
        /// <param name="target">目标端点（雷达IP和命令端口）</param>
        /// <exception cref="InvalidOperationException">CommandClient未初始化</exception>
        public void SendCommand(byte[] data, IPEndPoint target)
        {
            if (CommandClient == null)
                throw new InvalidOperationException("命令端口未初始化，请先调用StartListening()");

            lock (_commandLock)
            {
                CommandClient.Send(data, data.Length, target);
            }
        }

        /// <summary>
        /// 静态发送UDP数据方法
        /// 提供简单的UDP数据发送功能，不需要UdpCommunicator实例
        /// </summary>
        /// <param name="data">要发送的数据字节数组</param>
        /// <param name="target">目标端点</param>
        /// <param name="isBroadcast">是否为广播发送</param>
        public static void Send(byte[] data, IPEndPoint target, bool isBroadcast = false)
        {
#if NET45_OR_GREATER
            using (var client = new UdpClient())
            {
                if (isBroadcast)
                    client.EnableBroadcast = true;

                client.Send(data, data.Length, target);
            }
#elif NET9_0_OR_GREATER
            using var client = new UdpClient();
            if (isBroadcast)
                client.EnableBroadcast = true;

            client.Send(data, data.Length, target);
#endif
        }

        #endregion

        #region 资源释放

        /// <summary>
        /// 停止监听并释放所有UDP客户端资源
        /// </summary>
        public void Dispose()
        {
            _isListening = false;
            _listenerThreadCldPnt?.Join(500);
            _listenerThread?.Join(500);

            CommandClient?.Close();
            _pointCloudClient?.Close();
            _imuClient?.Close();

            // 调用GC.SuppressFinalize来阻止终结器运行
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}
