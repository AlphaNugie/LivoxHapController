using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.IO;

namespace LivoxHapController.Config
{
    /// <summary>
    /// 配置加载器
    /// </summary>
    public static class ConfigLoader
    {
        /// <summary>
        /// 从JSON文件加载配置
        /// </summary>
        /// <param name="filePath">JSON配置文件路径</param>
        /// <returns>反序列化后的AppConfig对象</returns>
        /// <exception cref="FileNotFoundException">当文件不存在时抛出</exception>
        /// <exception cref="JsonException">当JSON格式错误时抛出</exception>
        public static AppConfig LoadConfig(string filePath)
        {
            // 验证文件是否存在
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"配置文件不存在: {filePath}");
            }

            // 读取文件内容
            string jsonContent = File.ReadAllText(filePath);

            try
            {
                // 反序列化JSON内容
                // 检查配置文件是否为空，若为空则返回一个新的AppConfig对象
                var config = JsonConvert.DeserializeObject<AppConfig>(jsonContent) ?? new AppConfig();

                // 确保各DeviceConfig的HostNetInfo列表至少有一个默认元素
                // JSON.NET对List属性会追加到已有集合，因此属性初始化时不能预填元素，
                // 需要在反序列化后补充默认值
                config.HapConfig?.EnsureHostNetInfo();
                config.Mid360Config?.EnsureHostNetInfo();

                return config;
            }
            catch (JsonException ex)
            {
                // 封装并重新抛出更明确的异常
                throw new JsonException($"JSON解析错误: {ex.Message}", ex);
            }
        }
    }
}
