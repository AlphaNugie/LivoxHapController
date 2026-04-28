using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LivoxHapController.Config
{
    /// <summary>
    /// 表示主机网络配置信息
    /// </summary>
    public class HostNetInfo
    {
        /// <summary>
        /// LiDAR设备IP地址列表
        /// </summary>
        [JsonProperty("lidar_ip")]
#if NET45_OR_GREATER
        public List<string> LidarIp { get; set; } = new List<string>();
#elif NET9_0_OR_GREATER
        public List<string> LidarIp { get; set; } = [];
#endif

        /// <summary>
        /// 主机IP地址
        /// </summary>
        [JsonProperty("host_ip")]
        public string HostIp { get; set; } = string.Empty;

        /// <summary>
        /// 扫描仪帧速率（每帧重复扫描窗口长度，单位：ms，默认500）
        /// </summary>
        [JsonProperty("frame_time")]
        public int FrameTime { get; set; } = 100;

        /// <summary>
        /// 组播IP地址
        /// </summary>
        [JsonProperty("multicast_ip")]
        public string MulticastIp { get; set; } = string.Empty;

        /// <summary>
        /// 命令数据传输端口（默认56000）
        /// </summary>
        [JsonProperty("cmd_data_port")]
        public int CmdDataPort { get; set; } = 56000;

        /// <summary>
        /// 消息推送端口（默认0，表示禁用）
        /// </summary>
        [JsonProperty("push_msg_port")]
        public int PushMsgPort { get; set; }

        /// <summary>
        /// 点云数据传输端口（默认57000）
        /// </summary>
        [JsonProperty("point_data_port")]
        public int PointDataPort { get; set; } = 57000;

        /// <summary>
        /// IMU数据传输端口（默认58000）
        /// </summary>
        [JsonProperty("imu_data_port")]
        public int ImuDataPort { get; set; } = 58000;

        /// <summary>
        /// 日志数据传输端口（默认59000）
        /// </summary>
        [JsonProperty("log_data_port")]
        public int LogDataPort { get; set; } = 59000;
    }
}
