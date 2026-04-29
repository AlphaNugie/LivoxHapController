using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LivoxHapController.Config
{
    /// <summary>
    /// AppConfig 构建器（Builder模式）
    /// 将配置的"创建→覆盖→验证"逻辑从 AppConfig.Init 中抽取为独立类，
    /// 提供 Fluent API 风格的链式调用，使 LivoxHapRadar 的多个 Initialize 重载
    /// 都能通过统一的 Builder 获得最终的 AppConfig，不再依赖全局单例 AppConfig.Instance
    /// </summary>
    public class AppConfigBuilder
    {
        #region 私有字段

        /// <summary>待构建的配置对象</summary>
        private readonly AppConfig _config;

        #endregion

        #region 构造函数

        /// <summary>
        /// 私有构造函数，通过静态工厂方法创建Builder实例
        /// </summary>
        /// <param name="config">初始配置对象</param>
        private AppConfigBuilder(AppConfig config)
        {
            _config = config;
        }

        #endregion

        #region 静态工厂方法

        /// <summary>
        /// 从现有AppConfig对象创建Builder
        /// </summary>
        /// <param name="config">基础配置对象，为null时使用默认AppConfig</param>
        /// <returns>Builder实例</returns>
        public static AppConfigBuilder FromConfig(AppConfig config)
        {
#if NET45_OR_GREATER
            if (config == null)
                config = new AppConfig();
#elif NET9_0_OR_GREATER
            config ??= new AppConfig();
#endif
            return new AppConfigBuilder(config);
        }

        /// <summary>
        /// 从JSON配置文件创建Builder
        /// </summary>
        /// <param name="filePath">JSON配置文件路径（支持绝对路径和相对路径）</param>
        /// <returns>Builder实例</returns>
        /// <exception cref="ArgumentNullException">文件路径为空</exception>
        /// <exception cref="ArgumentException">文件不存在</exception>
        public static AppConfigBuilder FromFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentNullException(nameof(filePath), "配置文件路径不能为空");

            string path = filePath.Contains(Path.VolumeSeparatorChar)
                ? filePath
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, filePath);

            if (!File.Exists(path))
                throw new ArgumentException("配置文件不存在: " + path, nameof(filePath));

            var config = ConfigLoader.LoadConfig(path);
            return new AppConfigBuilder(config);
        }

        #endregion

        #region 覆盖方法（Fluent API）

        /// <summary>
        /// 覆盖 MasterSdk 设置
        /// </summary>
        /// <param name="value">是否启用主SDK，null时不覆盖</param>
        /// <returns>Builder实例（支持链式调用）</returns>
        public AppConfigBuilder WithMasterSdk(bool? value)
        {
            if (value.HasValue)
                _config.MasterSdk = value.Value;
            return this;
        }

        /// <summary>
        /// 覆盖步态切换阈值（同时设置HAP和MID360设备）
        /// </summary>
        /// <param name="value">步态切换阈值，null时不覆盖</param>
        /// <returns>Builder实例（支持链式调用）</returns>
        public AppConfigBuilder WithWalkChangeThreshold(double? value)
        {
            if (value.HasValue)
            {
                _config.HapConfig.WalkChangedThreshold = value.Value;
                _config.Mid360Config.WalkChangedThreshold = value.Value;
            }
            return this;
        }

        /// <summary>
        /// 覆盖LiDAR设备IP地址（同时设置HAP和MID360设备的第一个HostNetInfo）
        /// </summary>
        /// <param name="lidarIp">LiDAR设备IP地址，空字符串或null时不覆盖</param>
        /// <returns>Builder实例（支持链式调用）</returns>
        public AppConfigBuilder WithLidarIp(string lidarIp)
        {
            if (!string.IsNullOrWhiteSpace(lidarIp))
            {
                _config.HapConfig.EnsureHostNetInfo();
                _config.Mid360Config.EnsureHostNetInfo();
#if NET45_OR_GREATER
                _config.HapConfig.HostNetInfo[0].LidarIp = new List<string> { lidarIp };
                _config.Mid360Config.HostNetInfo[0].LidarIp = new List<string> { lidarIp };
#elif NET9_0_OR_GREATER
                _config.HapConfig.HostNetInfo[0].LidarIp = [lidarIp];
                _config.Mid360Config.HostNetInfo[0].LidarIp = [lidarIp];
#endif
            }
            return this;
        }

        /// <summary>
        /// 覆盖主机IP地址（同时设置HAP和MID360设备的第一个HostNetInfo）
        /// </summary>
        /// <param name="hostIp">主机IP地址，空字符串或null时不覆盖</param>
        /// <returns>Builder实例（支持链式调用）</returns>
        public AppConfigBuilder WithHostIp(string hostIp)
        {
            if (!string.IsNullOrWhiteSpace(hostIp))
            {
                _config.HapConfig.EnsureHostNetInfo();
                _config.Mid360Config.EnsureHostNetInfo();
                _config.HapConfig.HostNetInfo[0].HostIp = hostIp;
                _config.Mid360Config.HostNetInfo[0].HostIp = hostIp;
            }
            return this;
        }

        /// <summary>
        /// 覆盖点云数据端口（同时设置HAP和MID360设备的第一个HostNetInfo）
        /// </summary>
        /// <param name="port">点云数据端口号，null时不覆盖</param>
        /// <returns>Builder实例（支持链式调用）</returns>
        public AppConfigBuilder WithPointDataPort(int? port)
        {
            if (port.HasValue)
            {
                _config.HapConfig.EnsureHostNetInfo();
                _config.Mid360Config.EnsureHostNetInfo();
                _config.HapConfig.HostNetInfo[0].PointDataPort = port.Value;
                _config.Mid360Config.HostNetInfo[0].PointDataPort = port.Value;
            }
            return this;
        }

        #endregion

        #region 构建方法

        /// <summary>
        /// 构建最终的AppConfig对象
        /// 确保所有子对象不为空、HostNetInfo列表至少有一个默认元素
        /// </summary>
        /// <returns>完整且有效的AppConfig对象</returns>
        public AppConfig Build()
        {
            // 确保子对象不为空
#if NET45_OR_GREATER
            if (_config.HapConfig == null)
                _config.HapConfig = new DeviceConfig();
            if (_config.Mid360Config == null)
                _config.Mid360Config = new DeviceConfig();
#elif NET9_0_OR_GREATER
            _config.HapConfig ??= new DeviceConfig();
            _config.Mid360Config ??= new DeviceConfig();
#endif

            // 确保HostNetInfo列表至少包含一个默认元素
            _config.HapConfig.EnsureHostNetInfo();
            _config.Mid360Config.EnsureHostNetInfo();

            return _config;
        }

        /// <summary>
        /// 构建AppConfig并同步更新全局单例 AppConfig.Instance
        /// 用于向后兼容：旧的 AppConfig.Init() 调用需要更新全局单例
        /// </summary>
        /// <returns>完整且有效的AppConfig对象</returns>
        public AppConfig BuildAndSetInstance()
        {
            var config = Build();
            AppConfig.Instance = config;
            return config;
        }

        #endregion
    }
}
