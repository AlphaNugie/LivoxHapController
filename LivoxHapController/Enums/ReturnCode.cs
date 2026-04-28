namespace LivoxHapController.Enums
{
    /// <summary>
    /// 返回码枚举
    /// 定义命令执行的各种返回状态
    /// </summary>
    public enum ReturnCode : byte
    {
        /// <summary>
        /// 执行成功 (0x00)
        /// 命令成功执行
        /// </summary>
        Success = 0x00,

        /// <summary>
        /// 执行失败 (0x01)
        /// 命令执行失败，原因未知
        /// </summary>
        Failure = 0x01,

        /// <summary>
        /// 当前状态不支持 (0x02)
        /// 设备当前状态不支持该命令
        /// </summary>
        NotPermittedNow = 0x02,

        /// <summary>
        /// 设置值超出范围 (0x03)
        /// 参数值超出允许范围
        /// </summary>
        OutOfRange = 0x03,

        /// <summary>
        /// 参数不支持 (0x20)
        /// 设备不支持该参数
        /// </summary>
        ParamNotSupported = 0x20,

        /// <summary>
        /// 参数需重启生效 (0x21)
        /// 修改的参数需要重启设备才能生效
        /// </summary>
        ParamRebootEffect = 0x21,

        /// <summary>
        /// 参数只读 (0x22)
        /// 参数是只读的，不支持写入
        /// </summary>
        ParamReadOnly = 0x22,

        /// <summary>
        /// 请求参数长度错误 (0x23)
        /// 请求参数长度不正确
        /// </summary>
        ParamInvalidLength = 0x23,

        /// <summary>
        /// 参数key数量错误 (0x24)
        /// key_num和key_list不匹配
        /// </summary>
        ParamKeyNumError = 0x24,

        /// <summary>
        /// 公钥签名验证错误 (0x30)
        /// 固件升级时公钥验证失败
        /// </summary>
        UpgradePubKeyError = 0x30,

        /// <summary>
        /// 固件摘要签名验证错误 (0x31)
        /// 固件摘要签名验证失败
        /// </summary>
        UpgradeDigestError = 0x31,

        /// <summary>
        /// 固件类型不匹配 (0x32)
        /// 固件类型与设备不兼容
        /// </summary>
        UpgradeFwTypeError = 0x32,

        /// <summary>
        /// 固件长度超出范围 (0x33)
        /// 固件大小超出设备支持范围
        /// </summary>
        UpgradeFwOutOfRange = 0x33
    }
}