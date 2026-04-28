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
        public static AppConfig Instance { get; private set; } = new AppConfig();

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

            Instance = ConfigLoader.LoadConfig(path);
        }

        /// <summary>
        /// 通过AppConfig实体初始化应用程序配置
        /// 可选参数的值将覆盖到appConfig实体对应字段中（仅当可选参数非null/非默认值时覆盖）
        /// </summary>
        /// <param name="appConfig">应用程序配置实体，作为基础配置</param>
        /// <param name="masterSdk">是否启用主SDK（可选，默认true，覆盖appConfig.MasterSdk）</param>
        /// <param name="walkChangeThres">步态切换阈值（可选，默认1，覆盖appConfig.HapConfig.WalkChangedThreshold/appConfig.Mid360Config.WalkChangedThreshold）</param>
        /// <param name="lidarIp">扫描仪ip地址</param>
        /// <param name="hostIp">主机ip地址</param>
        /// <param name="point_data_port">接收点云的端口号（可选，默认57000）</param>
        /// <exception cref="ArgumentNullException">appConfig为空</exception>
        ///// <param name="lidarLogEnable">是否启用LiDAR日志（可选，默认true，覆盖appConfig.LidarLogEnable）</param>
        ///// <param name="lidarLogCacheSizeMB">LiDAR日志缓存大小MB（可选，默认500，覆盖appConfig.LidarLogCacheSizeMB）</param>
        ///// <param name="lidarLogPath">LiDAR日志存储路径（可选，默认"./"，覆盖appConfig.LidarLogPath）</param>
        public static void Init(AppConfig appConfig,
            bool? masterSdk = true,
            //bool? lidarLogEnable = true,
            //int? lidarLogCacheSizeMB = 500,
            //string lidarLogPath = @"./",
            double? walkChangeThres = 1, string lidarIp = "", string hostIp = "", int? point_data_port = 57000)
        {
            //if (appConfig == null)
            //    throw new ArgumentNullException(nameof(appConfig), "AppConfig实例不能为空");
#if NET45_OR_GREATER
            if (appConfig == null)
                appConfig = new AppConfig();
#elif NET9_0_OR_GREATER
            appConfig ??= new AppConfig();
#endif

            // 将可选参数的值覆盖到appConfig实体中
            if (masterSdk.HasValue)
                appConfig.MasterSdk = masterSdk.Value;
            //if (lidarLogEnable.HasValue)
            //    appConfig.LidarLogEnable = lidarLogEnable.Value;
            //if (lidarLogCacheSizeMB.HasValue)
            //    appConfig.LidarLogCacheSizeMB = lidarLogCacheSizeMB.Value;
            //if (!string.IsNullOrEmpty(lidarLogPath))
            //    appConfig.LidarLogPath = lidarLogPath;
#if NET45_OR_GREATER
            if (appConfig.HapConfig == null)
                appConfig.HapConfig = new DeviceConfig();
            if (appConfig.Mid360Config == null)
                appConfig.Mid360Config = new DeviceConfig();
#elif NET9_0_OR_GREATER
            appConfig.HapConfig ??= new DeviceConfig();
            appConfig.Mid360Config ??= new DeviceConfig();
#endif

            // 确保HostNetInfo列表至少包含一个默认元素（属性初始化时不再预填，避免JSON反序列化追加重复）
            appConfig.HapConfig.EnsureHostNetInfo();
            appConfig.Mid360Config.EnsureHostNetInfo();

            if (walkChangeThres.HasValue)
                appConfig.HapConfig.WalkChangedThreshold = appConfig.Mid360Config.WalkChangedThreshold = walkChangeThres.Value;
            if (!string.IsNullOrWhiteSpace(lidarIp))
                appConfig.HapConfig.HostNetInfo[0].LidarIp = appConfig.Mid360Config.HostNetInfo[0].LidarIp =
#if NET45_OR_GREATER
                    new List<string> { lidarIp };
#elif NET9_0_OR_GREATER
                    [lidarIp];
#endif
            if (!string.IsNullOrWhiteSpace(hostIp))
                appConfig.HapConfig.HostNetInfo[0].HostIp = appConfig.Mid360Config.HostNetInfo[0].HostIp = hostIp;
            if (point_data_port.HasValue)
                appConfig.HapConfig.HostNetInfo[0].PointDataPort = appConfig.Mid360Config.HostNetInfo[0].PointDataPort = point_data_port.Value;

            Instance = appConfig;
        }
    }
}
