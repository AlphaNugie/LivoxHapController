using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using Newtonsoft.Json;

namespace LivoxHapController.Config
{
    /// <summary>
    /// 表示完整的应用程序配置
    /// </summary>
    public class AppConfig
    {
        /// <summary>
        /// 应用程序配置实例
        /// </summary>
        public static AppConfig Instance { get; internal set; } = new AppConfig();

        /// <summary>
        /// 是否启用主SDK
        /// </summary>
        [JsonProperty("master_sdk")]
        public bool MasterSdk { get; set; } = true;

        /// <summary>
        /// 是否启用LiDAR日志
        /// </summary>
        [JsonProperty("lidar_log_enable")]
        //public bool LidarLogEnable { get; set; } = true;
        public bool LidarLogEnable { get; set; } = false;

        /// <summary>
        /// LiDAR日志缓存大小（MB）
        /// </summary>
        [JsonProperty("lidar_log_cache_size_MB")]
        public int LidarLogCacheSizeMB { get; set; } = 500;

        /// <summary>
        /// LiDAR日志存储路径
        /// </summary>
        [JsonProperty("lidar_log_path")]
        public string LidarLogPath { get; set; } = @"./";

        /// <summary>
        /// HAP设备配置
        /// </summary>
        [JsonProperty("HAP")]
        public DeviceConfig HapConfig { get; set; } = new DeviceConfig();

        /// <summary>
        /// MID360设备配置
        /// </summary>
        [JsonProperty("MID360")]
        public DeviceConfig Mid360Config { get; set; } = new DeviceConfig();

        /// <summary>
        /// 通过配置文件初始化应用程序配置
        /// 内部使用 AppConfigBuilder 构建配置
        /// </summary>
        /// <param name="configFile">配置文件路径或文件名</param>
        /// <exception cref="ArgumentNullException">配置文件路径为空</exception>
        /// <exception cref="ArgumentException">配置文件不存在</exception>
        public static void Init(string configFile)
        {
            if (string.IsNullOrWhiteSpace(configFile))
                throw new ArgumentNullException(nameof(configFile), "Config file Invalid, must input config file path.");
            string path = configFile.Contains(Path.VolumeSeparatorChar) ? configFile : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, configFile);
            if (!File.Exists(path))
                throw new ArgumentException("Config file does not exist, check again.", nameof(configFile));

            // 使用 AppConfigBuilder 从文件加载并构建配置，同时更新全局单例
            Instance = AppConfigBuilder.FromFile(path).BuildAndSetInstance();
        }

        /// <summary>
        /// 通过AppConfig实体初始化应用程序配置
        /// 可选参数的值将覆盖到appConfig实体对应字段中（仅当可选参数非null/非默认值时覆盖）
        /// 内部使用 AppConfigBuilder 构建配置
        /// </summary>
        /// <param name="appConfig">应用程序配置实体，作为基础配置</param>
        /// <param name="masterSdk">是否启用主SDK（可选，默认null，覆盖appConfig.MasterSdk）</param>
        /// <param name="walkChangeThres">步态切换阈值（可选，默认null，覆盖appConfig.HapConfig.WalkChangedThreshold/appConfig.Mid360Config.WalkChangedThreshold）</param>
        /// <param name="lidarIp">扫描仪ip地址（可选，默认空字符串）</param>
        /// <param name="hostIp">主机ip地址（可选，默认空字符串）</param>
        /// <param name="point_data_port">接收点云的端口号（可选，默认null）</param>
        /// <exception cref="ArgumentNullException">appConfig为空</exception>
        ///// <param name="lidarLogEnable">是否启用LiDAR日志（可选，默认true，覆盖appConfig.LidarLogEnable）</param>
        ///// <param name="lidarLogCacheSizeMB">LiDAR日志缓存大小MB（可选，默认500，覆盖appConfig.LidarLogCacheSizeMB）</param>
        ///// <param name="lidarLogPath">LiDAR日志存储路径（可选，默认"./"，覆盖appConfig.LidarLogPath）</param>
        public static void Init(AppConfig appConfig,
            bool? masterSdk = null, double? walkChangeThres = null, string lidarIp = "", string hostIp = "", int? point_data_port = null)
        {
            // 使用 AppConfigBuilder 从对象构建配置，应用可选参数覆盖，同时更新全局单例
            Instance = AppConfigBuilder.FromConfig(appConfig)
                .WithMasterSdk(masterSdk)
                .WithWalkChangeThreshold(walkChangeThres)
                .WithLidarIp(lidarIp)
                .WithHostIp(hostIp)
                .WithPointDataPort(point_data_port)
                .BuildAndSetInstance();
        }
    }
}
