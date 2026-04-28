using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LivoxHapController.Config
{
    /// <summary>
    /// 表示网络端口配置信息（HAP设备默认端口）
    /// </summary>
    public class NetInfoConfig
    {
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
