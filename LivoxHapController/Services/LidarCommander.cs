using System;
using System.Net;
using System.Net.Sockets;
using LivoxHapController.Enums;

namespace LivoxHapController.Services
{
#if NET45_OR_GREATER
    /// <summary>
    /// LiDAR命令控制器
    /// 封装所有Livox LiDAR参数配置和信息查询命令的发送逻辑
    /// 对应C++ command_impl.cpp 中的所有 CommandImpl::Set/Query 方法
    /// 
    /// 使用方式：
    /// 1. 创建实例时传入UdpCommunicator，复用其命令端口发送命令
    /// 2. 调用各种 Set/Query/Enable/Disable 方法发送命令
    /// 3. 通过 UdpCommunicator 接收并处理ACK响应
    /// 
    /// 重要：必须通过UdpCommunicator的命令端口发送命令，因为Livox协议的"跟随策略"
    /// 会让雷达将ACK回复到命令的源端口。如果使用独立UdpClient发送，
    /// ACK会回到该独立客户端的随机源端口，而UdpCommunicator绑定在固定端口上监听，导致收不到ACK。
    /// </summary>
    /// <remarks>
    /// 构造LiDAR命令控制器
    /// </remarks>
    public class LidarCommander : IDisposable
    {
        #region 私有字段

        /// <summary>目标雷达的IP地址</summary>
        private readonly string _lidarIp;

        /// <summary>目标雷达的命令端口</summary>
        private readonly int _commandPort;

        /// <summary>
        /// UDP通信处理器引用
        /// 复用其命令端口发送命令，确保命令和ACK共用同一端口
        /// </summary>
        private readonly UdpCommunicator _udpComm;

        /// <summary>最大重试次数</summary>
        private readonly int _maxRetries;

        /// <summary>重试间隔（毫秒）</summary>
        private readonly int _retryInterval;

        #endregion
#elif NET9_0_OR_GREATER
    /// <summary>
    /// LiDAR命令控制器
    /// 封装所有Livox LiDAR参数配置和信息查询命令的发送逻辑
    /// 对应C++ command_impl.cpp 中的所有 CommandImpl::Set/Query 方法
    /// 
    /// 使用方式：
    /// 1. 创建实例时传入UdpCommunicator，复用其命令端口发送命令
    /// 2. 调用各种 Set/Query/Enable/Disable 方法发送命令
    /// 3. 通过 UdpCommunicator 接收并处理ACK响应
    /// 
    /// 重要：必须通过UdpCommunicator的命令端口发送命令，因为Livox协议的"跟随策略"
    /// 会让雷达将ACK回复到命令的源端口。如果使用独立UdpClient发送，
    /// ACK会回到该独立客户端的随机源端口，而UdpCommunicator绑定在固定端口上监听，导致收不到ACK。
    /// </summary>
    /// <remarks>
    /// 构造LiDAR命令控制器
    /// </remarks>
    /// <param name="lidarIp">目标雷达的IP地址</param>
    /// <param name="commandPort">目标雷达的命令端口</param>
    /// <param name="udpComm">UDP通信处理器，复用其命令端口发送命令</param>
    /// <param name="maxRetries">发送失败时的最大重试次数（默认1）</param>
    /// <param name="retryInterval">重试间隔毫秒数（默认500）</param>
    public class LidarCommander(string lidarIp, int commandPort, UdpCommunicator udpComm, int maxRetries = 1, int retryInterval = 500) : IDisposable
    {
        #region 私有字段

        /// <summary>目标雷达的IP地址</summary>
        private readonly string _lidarIp = lidarIp;

        /// <summary>目标雷达的命令端口</summary>
        private readonly int _commandPort = commandPort;

        /// <summary>
        /// UDP通信处理器引用
        /// 复用其命令端口发送命令，确保命令和ACK共用同一端口
        /// </summary>
        private readonly UdpCommunicator _udpComm = udpComm;

        /// <summary>最大重试次数</summary>
        private readonly int _maxRetries = maxRetries;

        /// <summary>重试间隔（毫秒）</summary>
        private readonly int _retryInterval = retryInterval;

        #endregion
#endif
        #region 构造与析构

#if NET45_OR_GREATER
        /// <summary>
        /// 构造LiDAR命令控制器
        /// </summary>
        /// <param name="lidarIp">目标雷达的IP地址</param>
        /// <param name="commandPort">目标雷达的命令端口</param>
        /// <param name="udpComm">UDP通信处理器，复用其命令端口发送命令</param>
        /// <param name="maxRetries">发送失败时的最大重试次数（默认1）</param>
        /// <param name="retryInterval">重试间隔毫秒数（默认500）</param>
        public LidarCommander(string lidarIp, int commandPort, UdpCommunicator udpComm, int maxRetries = 1, int retryInterval = 500)
        {
            _lidarIp = lidarIp;
            _commandPort = commandPort;
            _udpComm = udpComm;
            _maxRetries = maxRetries;
            _retryInterval = retryInterval;
        }
#endif

        /// <summary>
        /// 释放资源
        /// LidarCommander不再拥有UdpClient，无需关闭网络资源
        /// UdpCommunicator的生命周期由LivoxHapRadar管理
        /// </summary>
        public void Dispose()
        {
            // LidarCommander不再拥有独立的UdpClient，无需关闭网络资源
            // UdpCommunicator的生命周期由LivoxHapRadar管理，不应在此处释放
            GC.SuppressFinalize(this);
        }

        #endregion

        #region 属性

        /// <summary>目标雷达的IP地址</summary>
        public string LidarIp { get { return _lidarIp; } }

        /// <summary>目标雷达的命令端口</summary>
        public int CommandPort { get { return _commandPort; } }

        #endregion

        #region 核心发送方法

        /// <summary>
        /// 发送命令包到目标雷达
        /// 对应C++ GeneralCommandHandler::SendCommand() 的底层UDP发送
        /// </summary>
        /// <param name="cmdId">命令ID</param>
        /// <param name="data">数据段内容</param>
        /// <returns>实际发送的字节数</returns>
        public int SendCommand(CommandType cmdId, byte[] data)
        {
            byte[] packet = SdkPacketBuilder.BuildCommand(cmdId, data);
            return SendWithRetry(packet);
        }

        /// <summary>
        /// 发送带重试的UDP数据包
        /// 通过UdpCommunicator.SendCommand()发送，确保使用命令端口发送
        /// </summary>
        /// <param name="packet">完整的数据包</param>
        /// <returns>实际发送的字节数</returns>
        private int SendWithRetry(byte[] packet)
        {
#if NET45_OR_GREATER
            IPEndPoint target = new IPEndPoint(IPAddress.Parse(_lidarIp), _commandPort);
#elif NET9_0_OR_GREATER
            IPEndPoint target = new(IPAddress.Parse(_lidarIp), _commandPort);
#endif

            for (int attempt = 0; attempt <= _maxRetries; attempt++)
            {
                try
                {
                    // 通过UdpCommunicator的命令端口发送，确保雷达ACK回到同一端口
                    _udpComm.SendCommand(packet, target);
                    return packet.Length;
                }
                catch (Exception)
                {
                    if (attempt < _maxRetries)
                        System.Threading.Thread.Sleep(_retryInterval);
                    else
                        throw;
                }
            }

            return 0;
        }

        #endregion

        #region 工作模式控制

        /// <summary>
        /// 编码WorkModeControl命令的数据段
        /// 对应C++ command_impl.cpp 中各 Set 函数的数据段构建逻辑
        /// 
        /// 数据段格式：
        /// [0-1]  key_num  : uint16_le  — KeyValue参数个数
        /// [2-3]  rsvd     : uint16_le  — 保留字段，始终为0
        /// [4+]   kv_list  : KeyValue[] — 连续排列的KeyValueParam块
        /// </summary>
        /// <param name="kv">KeyValue参数数组</param>
        /// <returns>完整的数据段字节数组</returns>
        private static byte[] GetEncodedWorkModeControlData(byte[] kv)
        {
            return KeyValueCodec.EncodeWorkModeControlData(
#if NET45_OR_GREATER
                new byte[][] { kv }
#elif NET9_0_OR_GREATER
                [kv]
#endif
                );
        }

        /// <summary>
        /// 设置工作模式
        /// 对应C++ CommandImpl::SetLivoxLidarWorkMode()
        /// kCommandIDLidarWorkModeControl (0x0100) + kKeyWorkMode (0x001A)
        /// </summary>
        /// <param name="workMode">工作模式（1=正常, 2=唤醒, 3=休眠）</param>
        public void SetWorkMode(byte workMode)
        {
            byte[] kv = KeyValueCodec.EncodeByte(KeyType.WorkTargetMode, workMode);
            byte[] data = GetEncodedWorkModeControlData(kv);
            SendCommand(CommandType.ParameterConfiguration, data);
        }

        /// <summary>
        /// 启动正常扫描（工作模式设为Normal = 0x01）
        /// </summary>
        public void StartNormalScan()
        {
            SetWorkMode(0x01); // kLivoxLidarNormal
        }

        /// <summary>
        /// 设置待机模式（Standby = 0x02）
        /// HAP(TX)停止扫描时应使用此模式
        /// </summary>
        public void SetStandbyMode()
        {
            SetWorkMode(0x02); // kLivoxLidarStandby
        }

        /// <summary>
        /// 休眠雷达（Sleep = 0x03）
        /// 注意：HAP(TX)版本不支持休眠状态，仅HAP(T1)版本支持
        /// </summary>
        public void SetSleepMode()
        {
            SetWorkMode(0x03); // kLivoxLidarSleep
        }

        /// <summary>
        /// 设置开机后的工作模式
        /// 对应C++ CommandImpl::SetLivoxLidarWorkModeAfterBoot()
        /// </summary>
        /// <param name="workMode">开机工作模式（0=默认, 1=正常, 2=唤醒）</param>
        public void SetWorkModeAfterBoot(byte workMode)
        {
            byte[] kv = KeyValueCodec.EncodeByte(KeyType.WorkmodeAfterBoot, workMode);
            byte[] data = GetEncodedWorkModeControlData(kv);
            SendCommand(CommandType.ParameterConfiguration, data);
        }

        #endregion

        #region 数据配置

        /// <summary>
        /// 设置点云数据类型
        /// 对应C++ CommandImpl::SetLivoxLidarPclDataType()
        /// </summary>
        /// <param name="dataType">数据类型（0=IMU, 1=笛卡尔高精度, 2=笛卡尔低精度, 3=球坐标）</param>
        public void SetPclDataType(byte dataType)
        {
            byte[] kv = KeyValueCodec.EncodeByte(KeyType.PclDataType, dataType);
            byte[] data = GetEncodedWorkModeControlData(kv);
            SendCommand(CommandType.ParameterConfiguration, data);
        }

        /// <summary>
        /// 设置扫描模式
        /// 对应C++ CommandImpl::SetLivoxLidarScanPattern()
        /// </summary>
        /// <param name="pattern">扫描模式（0=非重复, 1=重复, 2=重复低帧率）</param>
        public void SetScanPattern(byte pattern)
        {
            byte[] kv = KeyValueCodec.EncodeByte(KeyType.PatternMode, pattern);
            byte[] data = GetEncodedWorkModeControlData(kv);
            SendCommand(CommandType.ParameterConfiguration, data);
        }

        /// <summary>
        /// 设置双发射模式
        /// 对应C++ CommandImpl::SetLivoxLidarDualEmit()
        /// 使用 kKeyDualEmit (0x0002) 键
        /// </summary>
        /// <param name="enable">是否启用</param>
        public void SetDualEmit(bool enable)
        {
            byte[] kv = KeyValueCodec.EncodeByte(KeyType.DualEmitEnable, enable ? (byte)0x01 : (byte)0x00);
            byte[] data = GetEncodedWorkModeControlData(kv);
            SendCommand(CommandType.ParameterConfiguration, data);
        }

        /// <summary>
        /// 启用点云发送
        /// 对应C++ CommandImpl::EnableLivoxLidarPointSend()
        /// </summary>
        public void EnablePointSend()
        {
            byte[] kv = KeyValueCodec.EncodeByte(KeyType.PointSendEnable, 0x00);
            byte[] data = GetEncodedWorkModeControlData(kv);
            SendCommand(CommandType.ParameterConfiguration, data);
        }

        /// <summary>
        /// 禁用点云发送
        /// 对应C++ CommandImpl::DisableLivoxLidarPointSend()
        /// </summary>
        public void DisablePointSend()
        {
            byte[] kv = KeyValueCodec.EncodeByte(KeyType.PointSendEnable, 0x01);
            byte[] data = GetEncodedWorkModeControlData(kv);
            SendCommand(CommandType.ParameterConfiguration, data);
        }

        #endregion

        #region 网络配置

        /// <summary>
        /// 设置雷达IP地址
        /// 对应C++ CommandImpl::SetLivoxLidarIp()
        /// </summary>
        /// <param name="ipAddr">雷达IP地址（点分十进制）</param>
        /// <param name="netMask">子网掩码（点分十进制）</param>
        /// <param name="gwAddr">网关地址（点分十进制）</param>
        public void SetLidarIp(string ipAddr, string netMask, string gwAddr)
        {
            byte[] value = KeyValueCodec.EncodeLidarIpInfoValue(ipAddr, netMask, gwAddr);
            byte[] kv = KeyValueCodec.Encode(KeyType.LidarIpConfig, value);
            byte[] data = GetEncodedWorkModeControlData(kv);
            SendCommand(CommandType.ParameterConfiguration, data);
        }

        /// <summary>
        /// 设置状态信息主机IP配置
        /// 对应C++ CommandImpl::SetLivoxLidarStateInfoHostIPCfg()
        /// 使用 kKeyStateInfoHostIpCfg (0x0005) 键
        /// <para/>HAP (TX) 暂不支持
        /// </summary>
        /// <param name="hostIp">主机IP地址</param>
        /// <param name="hostPort">主机端口</param>
        /// <param name="lidarPort">雷达端口</param>
        public void SetStateInfoHostIp(string hostIp, ushort hostPort, ushort lidarPort)
        {
            byte[] value = KeyValueCodec.EncodeHostIpInfoValue(hostIp, hostPort, lidarPort);
            byte[] kv = KeyValueCodec.Encode(KeyType.StateInfoHostIpConfig, value);
            byte[] data = GetEncodedWorkModeControlData(kv);
            SendCommand(CommandType.ParameterConfiguration, data);
        }

        /// <summary>
        /// 设置点云数据主机IP配置
        /// 对应C++ CommandImpl::SetLivoxLidarPointDataHostIPCfg()
        /// </summary>
        /// <param name="hostIp">主机IP地址</param>
        /// <param name="hostPort">主机端口</param>
        /// <param name="lidarPort">雷达端口</param>
        public void SetPointCloudHostIp(string hostIp, ushort hostPort, ushort lidarPort)
        {
            byte[] value = KeyValueCodec.EncodeHostIpInfoValue(hostIp, hostPort, lidarPort);
            byte[] kv = KeyValueCodec.Encode(KeyType.PointCloudHostIpConfig, value);
            byte[] data = GetEncodedWorkModeControlData(kv);
            SendCommand(CommandType.ParameterConfiguration, data);
        }

        /// <summary>
        /// 设置IMU数据主机IP配置
        /// 对应C++ CommandImpl::SetLivoxLidarImuDataHostIPCfg()
        /// </summary>
        /// <param name="hostIp">主机IP地址</param>
        /// <param name="hostPort">主机端口</param>
        /// <param name="lidarPort">雷达端口</param>
        public void SetImuHostIp(string hostIp, ushort hostPort, ushort lidarPort)
        {
            byte[] value = KeyValueCodec.EncodeHostIpInfoValue(hostIp, hostPort, lidarPort);
            byte[] kv = KeyValueCodec.Encode(KeyType.ImuHostIpConfig, value);
            byte[] data = GetEncodedWorkModeControlData(kv);
            SendCommand(CommandType.ParameterConfiguration, data);
        }

        #endregion

        #region 空间配置

        /// <summary>
        /// 设置安装姿态
        /// 对应C++ CommandImpl::SetLivoxLidarInstallAttitude()
        /// </summary>
        /// <param name="roll">横滚角（度）</param>
        /// <param name="pitch">俯仰角（度）</param>
        /// <param name="yaw">偏航角（度）</param>
        /// <param name="x">X偏移（mm）</param>
        /// <param name="y">Y偏移（mm）</param>
        /// <param name="z">Z偏移（mm）</param>
        public void SetInstallAttitude(float roll, float pitch, float yaw, int x, int y, int z)
        {
            byte[] value = KeyValueCodec.EncodeInstallAttitude(roll, pitch, yaw, x, y, z);
            byte[] kv = KeyValueCodec.Encode(KeyType.InstallAttitude, value);
            byte[] data = GetEncodedWorkModeControlData(kv);
            SendCommand(CommandType.ParameterConfiguration, data);
        }

        /// <summary>
        /// 设置FOV配置0
        /// 对应C++ CommandImpl::SetLivoxLidarFovCfg0()
        /// 使用 kKeyFovCfg0 (0x0015) 键
        /// </summary>
        public void SetFovConfig0(int yawStart, int yawStop, int pitchStart, int pitchStop)
        {
            byte[] value = KeyValueCodec.EncodeFovConfig(yawStart, yawStop, pitchStart, pitchStop);
            byte[] kv = KeyValueCodec.Encode(KeyType.FovConfig0, value);
            byte[] data = GetEncodedWorkModeControlData(kv);
            SendCommand(CommandType.ParameterConfiguration, data);
        }

        /// <summary>
        /// 设置FOV配置1
        /// 对应C++ CommandImpl::SetLivoxLidarFovCfg1()
        /// 使用 kKeyFovCfg1 (0x0016) 键
        /// </summary>
        public void SetFovConfig1(int yawStart, int yawStop, int pitchStart, int pitchStop)
        {
            byte[] value = KeyValueCodec.EncodeFovConfig(yawStart, yawStop, pitchStart, pitchStop);
            byte[] kv = KeyValueCodec.Encode(KeyType.FovConfig1, value);
            byte[] data = GetEncodedWorkModeControlData(kv);
            SendCommand(CommandType.ParameterConfiguration, data);
        }

        /// <summary>
        /// 设置盲区
        /// 对应C++ CommandImpl::SetLivoxLidarBlindSpot()
        /// </summary>
        /// <param name="blindSpot">盲区距离（单位cm，范围50-200）</param>
        public void SetBlindSpot(uint blindSpot)
        {
            byte[] kv = KeyValueCodec.EncodeUInt32(KeyType.BlindSpotSet, blindSpot);
            byte[] data = GetEncodedWorkModeControlData(kv);
            SendCommand(CommandType.ParameterConfiguration, data);
        }

        #endregion

        #region IMU / FUSA / 加热

        /// <summary>
        /// 启用IMU数据
        /// 对应C++ CommandImpl::EnableLivoxLidarImuData()
        /// </summary>
        public void EnableImuData()
        {
            byte[] kv = KeyValueCodec.EncodeByte(KeyType.ImuDataEnable, 0x01);
            byte[] data = GetEncodedWorkModeControlData(kv);
            SendCommand(CommandType.ParameterConfiguration, data);
        }

        /// <summary>
        /// 禁用IMU数据
        /// 对应C++ CommandImpl::DisableLivoxLidarImuData()
        /// </summary>
        public void DisableImuData()
        {
            byte[] kv = KeyValueCodec.EncodeByte(KeyType.ImuDataEnable, 0x00);
            byte[] data = GetEncodedWorkModeControlData(kv);
            SendCommand(CommandType.ParameterConfiguration, data);
        }

        /// <summary>
        /// 启用窗口加热
        /// 对应C++ CommandImpl::EnableLivoxLidarGlassHeat()
        /// </summary>
        public void EnableGlassHeat()
        {
            byte[] kv = KeyValueCodec.EncodeByte(KeyType.GlassHeatSupport, 0x01);
            byte[] data = GetEncodedWorkModeControlData(kv);
            SendCommand(CommandType.ParameterConfiguration, data);
        }

        /// <summary>
        /// 禁用窗口加热
        /// 对应C++ CommandImpl::DisableLivoxLidarGlassHeat()
        /// </summary>
        public void DisableGlassHeat()
        {
            byte[] kv = KeyValueCodec.EncodeByte(KeyType.GlassHeatSupport, 0x00);
            byte[] data = GetEncodedWorkModeControlData(kv);
            SendCommand(CommandType.ParameterConfiguration, data);
        }

        /// <summary>
        /// 启用强制加热
        /// 对应C++ CommandImpl::StartForcedHeating()
        /// </summary>
        public void StartForcedHeating()
        {
            byte[] kv = KeyValueCodec.EncodeByte(KeyType.ForceHeatEnable, 0x01);
            byte[] data = GetEncodedWorkModeControlData(kv);
            SendCommand(CommandType.ParameterConfiguration, data);
        }

        /// <summary>
        /// 停止强制加热
        /// 对应C++ CommandImpl::StopForcedHeating()
        /// </summary>
        public void StopForcedHeating()
        {
            byte[] kv = KeyValueCodec.EncodeByte(KeyType.ForceHeatEnable, 0x00);
            byte[] data = GetEncodedWorkModeControlData(kv);
            SendCommand(CommandType.ParameterConfiguration, data);
        }

        /// <summary>
        /// 启用功能安全
        /// 对应C++ CommandImpl::EnableLivoxLidarFusaFunciont()
        /// </summary>
        public void EnableFusa()
        {
            byte[] kv = KeyValueCodec.EncodeByte(KeyType.FusaEnable, 0x01);
            byte[] data = GetEncodedWorkModeControlData(kv);
            SendCommand(CommandType.ParameterConfiguration, data);
        }

        /// <summary>
        /// 禁用功能安全
        /// 对应C++ CommandImpl::DisableLivoxLidarFusaFunciont()
        /// </summary>
        public void DisableFusa()
        {
            byte[] kv = KeyValueCodec.EncodeByte(KeyType.FusaEnable, 0x00);
            byte[] data = GetEncodedWorkModeControlData(kv);
            SendCommand(CommandType.ParameterConfiguration, data);
        }

        #endregion

        #region 信息查询

        /// <summary>
        /// 查询设备内部信息（批量）
        /// 对应C++ CommandImpl::QueryLivoxLidarInternalInfo()
        /// </summary>
        /// <param name="keys">要查询的参数键列表</param>
        public void QueryInternalInfo(KeyType[] keys)
        {
            byte[] data = KeyValueCodec.EncodeQueryData(keys);
            SendCommand(CommandType.RadarInfoQuery, data);
        }

        /// <summary>
        /// 查询固件类型
        /// 对应C++ CommandImpl::QueryLivoxLidarFwType()
        /// </summary>
        public void QueryFirmwareType()
        {
            QueryInternalInfo(
#if NET45_OR_GREATER
                new KeyType[] { KeyType.FirmwareType }
#elif NET9_0_OR_GREATER
                [KeyType.FirmwareType]
#endif
                );
        }

        /// <summary>
        /// 查询固件版本
        /// 对应C++ CommandImpl::QueryLivoxLidarFirmwareVer()
        /// </summary>
        public void QueryFirmwareVersion()
        {
            QueryInternalInfo(
#if NET45_OR_GREATER
                new KeyType[] { KeyType.VersionApp }
#elif NET9_0_OR_GREATER
                [KeyType.VersionApp]
#endif
                );
        }

        #endregion

        #region 设备控制

        /// <summary>
        /// 重启设备
        /// 对应C++ CommandImpl::LivoxLidarRequestReboot()
        /// kCommandIDLidarRebootDevice (0x0200)
        /// </summary>
        /// <param name="timeoutMs">延迟重启时间（毫秒），范围100~2000</param>
        public void RebootDevice(ushort timeoutMs = 100)
        {
            byte[] data = new byte[2];
            SdkPacketBuilder.WriteUInt16Le(data, 0, timeoutMs);
            SendCommand(CommandType.DeviceReboot, data);
        }

        #endregion

        #region 通用KeyValue设置

        /// <summary>
        /// 发送单个KeyValue控制命令（通用方法）
        /// 对应C++ CommandImpl::SendSingleControlCommand()
        /// 适用于简单的单键值对设置命令
        /// </summary>
        /// <param name="key">参数键</param>
        /// <param name="value">参数值（单字节）</param>
        public void SendSingleControl(KeyType key, byte value)
        {
            byte[] kv = KeyValueCodec.EncodeByte(key, value);
            byte[] data = GetEncodedWorkModeControlData(kv);
            SendCommand(CommandType.ParameterConfiguration, data);
        }

        #endregion
    }
}
