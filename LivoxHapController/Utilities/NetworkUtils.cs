using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace LivoxHapController.Utilities
{
    internal class NetworkUtils
    {
        /// <summary>
        /// 在所有网卡地址中自动查找与目标扫描仪IP同段的IPv4地址（前三段相同）
        /// </summary>
        /// <param name="targetIp">目标扫描仪IP地址</param>
        /// <returns>与目标IP同段的本机IP地址，未找到则返回空字符串</returns>
        public static string GetHostIpInSameSegment(string targetIp)
        {
            if (string.IsNullOrWhiteSpace(targetIp))
                return string.Empty;

            //解析目标IP的前三段
            string[] targetParts = targetIp.Split('.');
            if (targetParts.Length < 3)
                return string.Empty;

            string targetPrefix = string.Format("{0}.{1}.{2}", targetParts[0], targetParts[1], targetParts[2]);

            //遍历所有本机网卡地址，查找与目标IP同段的IPv4地址
            try
            {
                IPHostEntry host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (IPAddress ip in host.AddressList)
                {
                    //仅筛选IPv4地址
                    if (ip.AddressFamily != AddressFamily.InterNetwork)
                        continue;

                    string ipStr = ip.ToString();
                    string[] parts = ipStr.Split('.');
                    if (parts.Length < 3)
                        continue;

                    //前三段相同即为同段
                    string prefix = string.Format("{0}.{1}.{2}", parts[0], parts[1], parts[2]);
                    if (prefix == targetPrefix)
                        return ipStr;
                }
            }
            catch (Exception) { }

            return string.Empty;
        }
    }
}
