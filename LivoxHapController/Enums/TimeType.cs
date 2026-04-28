namespace LivoxHapController.Enums
{
    /// <summary>
    /// 时间戳类型枚举
    /// 定义时间同步和表示的类型
    /// </summary>
    public enum TimeType : byte
    {
        /// <summary>
        /// 无同步源 (0x00)
        /// 时间戳为雷达开机时间
        /// </summary>
        NoSync = 0x00,

        /// <summary>
        /// gPTP同步 (0x01)
        /// 时间戳为gPTP主时钟源时间
        /// </summary>
        GptpSync = 0x01
    }
}