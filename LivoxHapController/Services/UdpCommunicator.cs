using LivoxHapController.Config;
using LivoxHapController.Enums;
using LivoxHapController.Services.Parsers;
using LivoxHapController.Utilities;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

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

        /// <summary>
        /// 点云/IMU 来源IP过滤器
        /// 非空时只接受列表中IP发送的数据，空时不过滤接受所有来源
        /// 由 LivoxHapRadar 初始化后从配置中的 LidarIp 填充
        /// </summary>
        private HashSet<string>
  //.net 9框架下使返回对象可为空
#if NET9_0_OR_GREATER
            ?
#endif
  _pointCloudSourceIpFilter;

        /// <summary>
        /// 命令端口的UDP客户端
        /// internal访问级别，供LidarCommander复用以发送命令，确保命令和ACK共用同一端口
        /// </summary>
        internal UdpClient
  //.net 9框架下使返回对象可为空
#if NET9_0_OR_GREATER
            ?
#endif
  CommandClient;

        /// <summary>点云端口的UDP客户端</summary>
        private UdpClient
  //.net 9框架下使返回对象可为空
#if NET9_0_OR_GREATER
            ?
#endif
  _pointCloudClient;

        /// <summary>IMU端口的UDP客户端</summary>
        private UdpClient
  //.net 9框架下使返回对象可为空
#if NET9_0_OR_GREATER
            ?
#endif
  _imuClient;

        /// <summary>命令端口监听线程</summary>
        private Thread
  //.net 9框架下使返回对象可为空
#if NET9_0_OR_GREATER
            ?
#endif
  _listenerThread;

        /// <summary>点云端口监听线程</summary>
        private Thread
  //.net 9框架下使返回对象可为空
#if NET9_0_OR_GREATER
            ?
#endif
  _listenerThreadCldPnt;

        /// <summary>
        /// 命令客户端的线程同步锁
        /// 保护CommandClient的Send/Receive操作，防止发送线程和接收线程并发访问导致异常
        /// </summary>
#if NET9_0_OR_GREATER
        private readonly Lock _commandLock = new();
#elif NET45_OR_GREATER
        private readonly object _commandLock = new object();
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
        /// 当端口被占用时，等待0.5秒后重试，最长等待10秒，超时后上报错误
        /// 三个端口的绑定重试逻辑互相隔离，已成功的端口不受其他端口失败影响
        /// </summary>
        /// <param name="hostIp">本机IP地址，为空则绑定所有网络接口</param>
        /// <param name="commandPort">命令端口（默认56000）</param>
        /// <param name="pointCloudPort">点云端口（默认57000）</param>
        /// <param name="imuPort">IMU端口（默认58000）</param>
        /// <exception cref="InvalidOperationException">等待10秒后端口仍被占用时抛出</exception>
        public void StartListening(string hostIp = "", int commandPort = 56000, int pointCloudPort = 57000, int imuPort = 58000)
        {
            // 端口绑定重试参数
            const int retryIntervalMs = 500;   // 每次重试间隔0.5秒
            //const int maxRetryTimeMs = 10000;   // 最长等待10秒
            const int maxRetryTimeMs = 2000;   // 最长等待时间（毫秒）

            // 收集最终绑定失败的端口信息（用于统一上报）
            var failedPorts = new List<string>();

            // 三个端口独立重试绑定，互不影响：
            // 已成功绑定的端口不会被其他端口的失败所干扰

            // 尝试绑定命令端口（用于接收ACK响应和设备推送消息）
            // 同时也作为发送命令的端口，确保雷达的"跟随策略"将ACK回复到同一端口
            if (!TryBindPortWithRetry(
                    port => CommandClient = CreateUdpClient(hostIp, port),
                    commandPort, retryIntervalMs, maxRetryTimeMs, "命令端口", failedPorts))
            {
                //// 命令端口绑定失败，清理并上报
                // 命令端口绑定失败，清理但不上报
                ////CleanupClients();
                //CommandClient?.Close();
                //CommandClient = null;
                CleanupCommandClient();
                //throw new InvalidOperationException(
                    //string.Format("等待端口释放超时（{1}秒），命令端口 {0} 仍被占用，请检查是否有其他程序或Livox实例正在运行", commandPort, maxRetryTimeMs / 1000));
            }

            // 尝试绑定点云端口（用于接收点云数据）
            if (!TryBindPortWithRetry(
                    port => _pointCloudClient = CreateUdpClient(hostIp, port),
                    pointCloudPort, retryIntervalMs, maxRetryTimeMs, "点云端口", failedPorts))
            {
                // 点云端口绑定失败，清理并上报
                //CleanupClients();
                _pointCloudClient?.Close();
                _pointCloudClient = null;
                throw new InvalidOperationException(
                    string.Format("等待端口释放超时（{1}秒），点云端口 {0} 仍被占用，请检查是否有其他程序或Livox实例正在运行", pointCloudPort, maxRetryTimeMs / 1000));
            }

            // 尝试绑定IMU端口（用于接收IMU数据）
            if (!TryBindPortWithRetry(
                    port => _imuClient = CreateUdpClient(hostIp, port),
                    imuPort, retryIntervalMs, maxRetryTimeMs, "IMU端口", failedPorts))
            {
                // IMU端口绑定失败，清理并上报
                //CleanupClients();
                _imuClient?.Close();
                _imuClient = null;
                throw new InvalidOperationException(
                    string.Format("等待端口释放超时（{1}秒），IMU端口 {0} 仍被占用，请检查是否有其他程序或Livox实例正在运行", imuPort, maxRetryTimeMs / 1000));
            }

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

        /// <summary>
        /// 创建绑定到指定IP和端口的UdpClient
        /// 封装了hostIp为空和不为空两种绑定方式
        /// </summary>
        /// <param name="hostIp">本机IP地址，为空则绑定所有网络接口</param>
        /// <param name="port">要绑定的端口号</param>
        /// <returns>已绑定的UdpClient实例</returns>
        //private static UdpClient CreateUdpClient(string hostIp, int port)
        internal static UdpClient CreateUdpClient(string hostIp, int port)
        {
            return string.IsNullOrWhiteSpace(hostIp)
                ? new UdpClient(port)
                : new UdpClient(new IPEndPoint(IPAddress.Parse(hostIp), port));
        }

        /// <summary>
        /// 尝试绑定单个端口，带等待重试逻辑
        /// 当端口被占用时等待0.5秒后重试，最长等待10秒
        /// 此方法独立处理单个端口的绑定，不影响其他端口
        /// </summary>
        /// <param name="bindAction">绑定动作，接收端口号，成功时设置对应的UdpClient字段</param>
        /// <param name="port">要绑定的端口号</param>
        /// <param name="retryIntervalMs">重试间隔（毫秒）</param>
        /// <param name="maxRetryTimeMs">最长等待时间（毫秒）</param>
        /// <param name="portName">端口名称（用于错误信息）</param>
        /// <param name="failedPorts">失败端口收集列表（用于汇总报告）</param>
        /// <returns>true=绑定成功，false=超时仍被占用</returns>
        private static bool TryBindPortWithRetry(Action<int> bindAction, int port,
            int retryIntervalMs, int maxRetryTimeMs, string portName, List<string> failedPorts)
        {
            int totalWaitedMs = 0;

            while (true)
            {
                try
                {
                    bindAction(port);
                    return true; // 绑定成功
                }
                catch (SocketException)
                {
                    // 端口被占用
                }

                // 超过最大等待时间，记录失败并返回
                if (totalWaitedMs >= maxRetryTimeMs)
                {
                    failedPorts.Add(string.Format("{0} {1}", portName, port));
                    return false;
                }

                // 等待0.5秒后重试
                Thread.Sleep(retryIntervalMs);
                totalWaitedMs += retryIntervalMs;
            }
        }

        /// <summary>
        /// 清理已创建的命令客户端资源（UDP）
        /// </summary>
        internal void CleanupCommandClient()
        {
            CommandClient?.Close();
            CommandClient = null;
        }

        ///// <summary>
        ///// 清理所有已创建的UDP客户端资源
        ///// 在绑定失败时调用，确保不遗留部分绑定的端口
        ///// </summary>
        //private void CleanupClients()
        //{
        //    CommandClient?.Close();
        //    CommandClient = null;
        //    _pointCloudClient?.Close();
        //    _pointCloudClient = null;
        //    _imuClient?.Close();
        //    _imuClient = null;
        //}

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

                    // 来源IP过滤：如果设置了过滤器且来源不在白名单内
                    if (remoteEP != null && !IsPointCloudSourceAccepted(remoteEP))
                    {
                        // 不在白名单 → 尝试转发到匹配的其他 UdpCommunicator 实例
                        var target = RadarRegistry.FindTarget(remoteEP.Address.ToString(), this);
                        target?.InjectImuData(data);
                        continue;
                    }

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

                    // 来源IP过滤：如果设置了过滤器且来源不在白名单内
                    if (remoteEP != null && !IsPointCloudSourceAccepted(remoteEP))
                    {
                        // 不在白名单 → 尝试转发到匹配的其他 UdpCommunicator 实例
                        var target = RadarRegistry.FindTarget(remoteEP.Address.ToString(), this);
                        target?.InjectPointCloudData(data);
                        continue;
                    }

                    PointCloudDataReceived?.Invoke(this, data);
                }

                Thread.Sleep(1); // 防止CPU占用过高
            }
        }

        #endregion

        #region 来源IP过滤

        /// <summary>
        /// 设置点云/IMU 数据的来源IP白名单
        /// 设置后仅接受列表中IP地址发送的点云和IMU数据
        /// 传入空列表或null时取消过滤，接受所有来源
        /// </summary>
        /// <param name="lidarIps">允许的雷达IP地址列表，null或空列表=不过滤</param>
        public void SetPointCloudSourceIpFilter(List<string> lidarIps)
        {
            if (lidarIps == null || lidarIps.Count == 0)
            {
                // 无过滤条件，接受所有来源
                _pointCloudSourceIpFilter = null;
            }
            else
            {
                // 构建IP白名单（HashSet实现O(1)查找）
                _pointCloudSourceIpFilter = new HashSet<string>(lidarIps, StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// 检查给定的远程端点IP是否被白名单接受
        /// 过滤器为null时表示不过滤，接受所有来源
        /// 例外：本机网卡IP来源的数据始终放行（保障跨实例转发数据流通）
        /// </summary>
        /// <param name="remoteEP">远程端点</param>
        /// <returns>true=接受该来源的数据，false=应丢弃（但会在监听线程中尝试转发）</returns>
        private bool IsPointCloudSourceAccepted(IPEndPoint remoteEP)
        {
            // 未设置过滤器 = 不过滤，接受所有来源
            if (_pointCloudSourceIpFilter == null)
                return true;

            string ip = remoteEP.Address.ToString();

            // 例外：本机网卡IP → 放行（保障跨实例转发数据流通）
            if (RadarRegistry.IsLocalNicIp(ip))
                return true;

            // 在白名单内 = 接受
            return _pointCloudSourceIpFilter.Contains(ip);
        }

        /// <summary>
        /// 接收其他实例转发的点云数据（绕过自身过滤逻辑，直接触发事件）
        /// 模拟接收线程收到了数据，下游 LivoxHapRadar.OnPointCloudDataReceived → MergePointCloudData 正常处理
        /// </summary>
        /// <param name="data">原始点云字节数据</param>
        internal void InjectPointCloudData(byte[] data)
        {
            PointCloudDataReceived?.Invoke(this, data);
        }

        /// <summary>
        /// 接收其他实例转发的IMU数据（绕过自身过滤逻辑，直接触发事件）
        /// 模拟接收线程收到了数据，下游 LivoxHapRadar.OnImuDataReceived 正常处理
        /// </summary>
        /// <param name="data">原始IMU字节数据</param>
        internal void InjectImuData(byte[] data)
        {
            ImuDataReceived?.Invoke(this, data);
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
