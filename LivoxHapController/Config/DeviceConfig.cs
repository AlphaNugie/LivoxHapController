using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LivoxHapController.Config
{
    /// <summary>
    /// 表示设备（HAP/MID360）的完整配置
    /// </summary>
    public class DeviceConfig
    {
        /// <summary>
        /// 行走位置的变化阈值，单位为米，默认值为4.0
        /// </summary>
        [JsonProperty("walk_change_thres")]
        public double WalkChangedThreshold { get; set; } = 1.0;

        /// <summary>
        /// LiDAR网络配置信息
        /// </summary>
        [JsonProperty("lidar_net_info")]
        public NetInfoConfig LidarNetInfo { get; set; } = new NetInfoConfig();

        /// <summary>
        /// 主机网络配置信息列表
        /// 注意：此列表不在属性初始化时预填默认元素，以避免JSON反序列化时追加导致元素重复
        /// </summary>
        [JsonProperty("host_net_info")]
#if NET45_OR_GREATER
        public List<HostNetInfo> HostNetInfo { get; set; } = new List<HostNetInfo>();
#elif NET9_0_OR_GREATER
        public List<HostNetInfo> HostNetInfo { get; set; } = [];
#endif

        /// <summary>
        /// 确保HostNetInfo列表至少包含一个默认的HostNetInfo元素
        /// 当列表为空时（如JSON中未配置该字段），自动补充一个默认实例
        /// </summary>
        /// <returns>始终返回至少包含一个元素的HostNetInfo列表</returns>
        public List<HostNetInfo> EnsureHostNetInfo()
        {
            if (HostNetInfo == null || HostNetInfo.Count == 0)
            {
#if NET45_OR_GREATER
                HostNetInfo = new List<HostNetInfo> { new HostNetInfo() };
#elif NET9_0_OR_GREATER
                HostNetInfo = [new HostNetInfo()];
#endif
            }
            return HostNetInfo;
        }
    }
}
