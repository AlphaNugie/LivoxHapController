namespace LivoxHapController.Models
{
    /// <summary>
    /// 网络配置模型
    /// </summary>
    public class NetworkConfig
    {
        /// <summary> IP地址 (格式：AA.BB.CC.DD) </summary>
        public byte[] IpAddress { get; set; } = new byte[4];

        /// <summary> 子网掩码 </summary>
        public byte[] SubnetMask { get; set; } = new byte[4];

        /// <summary> 网关地址 </summary>
        public byte[] Gateway { get; set; } = new byte[4];

        /// <summary> 目的端口号 </summary>
        public ushort DestinationPort { get; set; }

        /// <summary> 源端口号 </summary>
        public ushort SourcePort { get; set; }
    }
}