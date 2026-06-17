using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using LivoxHapController.Services;

namespace LivoxHapController.Utilities
{
    /// <summary>
    /// 进程内扫描仪数据路由注册表（全局静态）
    /// 同一进程中多个 LivoxHapRadar 实例共享此注册表，实现跨实例的数据路由与转发：
    /// 当某实例收到非自身配置 IP 来源的数据时，在注册表中检索匹配的目标实例并转发
    /// </summary>
    internal static class RadarRegistry
    {
        #region 私有字段

        /// <summary>
        /// 扫描仪源IP → 负责该IP的 UdpCommunicator 映射表（线程安全）
        /// 键：扫描仪 IP 地址字符串
        /// 值：该扫描仪所属的 UdpCommunicator 实例
        /// </summary>
        private static readonly ConcurrentDictionary<string, UdpCommunicator> _map =
#if NET45_OR_GREATER
            new ConcurrentDictionary<string, UdpCommunicator>(StringComparer.OrdinalIgnoreCase);
#elif NET9_0_OR_GREATER
            new(StringComparer.OrdinalIgnoreCase);
#endif

        /// <summary>
        /// 本机所有网卡 IPv4 地址集合（懒加载，首次访问时获取）
        /// 使用 Lazy 确保线程安全的单次初始化
        /// </summary>
        private static readonly Lazy<HashSet<string>> _localNicIps =
#if NET45_OR_GREATER
            new Lazy<HashSet<string>>(GetLocalNicIps, System.Threading.LazyThreadSafetyMode.PublicationOnly);
#elif NET9_0_OR_GREATER
            new(GetLocalNicIps, LazyThreadSafetyMode.PublicationOnly);
#endif

        #endregion

        #region 注册与注销

        /// <summary>
        /// 注册扫描仪 IP 到 UdpCommunicator 的映射
        /// 将 lidarIps 中的每个 IP 映射到指定的 communicator
        /// 如果某 IP 已存在映射，采用后注册覆盖策略（一个 IP 只能属于一个实例）
        /// </summary>
        /// <param name="lidarIps">扫描仪 IP 地址列表</param>
        /// <param name="communicator">对应的 UdpCommunicator 实例</param>
        public static void Register(IEnumerable<string> lidarIps, UdpCommunicator communicator)
        {
            if (lidarIps == null || communicator == null)
                return;

            foreach (string ip in lidarIps)
            {
                if (!string.IsNullOrWhiteSpace(ip))
                {
                    // 后注册覆盖前注册，确保每个 IP 有唯一的 communicator 映射
                    _map[ip.Trim()] = communicator;
                }
            }
        }

        /// <summary>
        /// 注销指定 UdpCommunicator 的所有映射
        /// 移除所有指向该 communicator 的 IP 条目
        /// </summary>
        /// <param name="communicator">要注销的 UdpCommunicator 实例</param>
        public static void Unregister(UdpCommunicator
            //.net 9框架下使返回对象可为空
#if NET9_0_OR_GREATER
            ?
#endif
  communicator)
        {
            if (communicator == null)
                return;

            // 查找所有映射到该 communicator 的 IP 并移除
            var keysToRemove = _map
                .Where(kvp => kvp.Value == communicator)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (string key in keysToRemove)
            {
                _map.TryRemove(key, out _);
            }
        }

        #endregion

        #region 路由查找

        /// <summary>
        /// 根据来源 IP 查找负责该 IP 的 UdpCommunicator（排除自身）
        /// 用于过滤失败时的数据转发：不在白名单的数据尝试转发给正确的实例
        /// </summary>
        /// <param name="sourceIp">数据来源 IP（扫描仪 IP）</param>
        /// <param name="self">当前 UdpCommunicator 实例（排除自身）</param>
        /// <returns>匹配的 UdpCommunicator 实例，未找到或指向自身时返回 null</returns>
        public static UdpCommunicator
            //.net 9框架下使返回对象可为空
#if NET9_0_OR_GREATER
            ?
#endif
            FindTarget(string sourceIp, UdpCommunicator self)
        {
            if (string.IsNullOrWhiteSpace(sourceIp) || self == null)
                return null;


            if (_map.TryGetValue(sourceIp.Trim(), out UdpCommunicator
                //.net 9框架下使返回对象可为空
#if NET9_0_OR_GREATER
                ?
#endif
                target) && target != self)
                return target;

            return null;
        }

        #endregion

        #region 本机网卡 IP 检测

        /// <summary>
        /// 判断给定 IP 是否为本机任意网卡的 IPv4 地址
        /// 用于 IP 过滤例外：本机网卡 IP 来源的数据不进行过滤（保障跨实例转发数据流通）
        /// </summary>
        /// <param name="ip">要检查的 IP 地址字符串</param>
        /// <returns>true=本机网卡 IP，false=非本机 IP</returns>
        public static bool IsLocalNicIp(string ip)
        {
            if (string.IsNullOrWhiteSpace(ip))
                return false;

            return _localNicIps.Value.Contains(ip.Trim());
        }

        /// <summary>
        /// 获取本机所有网卡的 IPv4 地址集合
        /// 遍历 Dns.GetHostEntry 返回的所有地址，筛选 IPv4 类型
        /// </summary>
        /// <returns>IPv4 地址字符串的 HashSet</returns>
        private static HashSet<string> GetLocalNicIps()
        {
            var ips = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                IPHostEntry host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (IPAddress ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork)
                    {
                        ips.Add(ip.ToString());
                    }
                }
            }
            catch (Exception)
            {
                // 获取网卡信息失败时返回空集合，不影响主流程
            }

            return ips;
        }

        #endregion
    }
}
