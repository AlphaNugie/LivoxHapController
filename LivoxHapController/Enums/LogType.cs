namespace LivoxHapController.Enums
{
    /// <summary>
    /// 日志类型枚举
    /// 定义日志文件的类型
    /// </summary>
    public enum LogType : byte
    {
        /// <summary>
        /// 实时日志 (0x00)
        /// 设备运行时生成的实时日志
        /// </summary>
        RealtimeLog = 0x00,

        /// <summary>
        /// 异常日志 (0x01)
        /// 设备发生异常时生成的日志
        /// </summary>
        ExceptionLog = 0x01
    }
}