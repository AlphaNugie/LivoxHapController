#if NET45_OR_GREATER
using System;
#endif

namespace LivoxHapController.Enums
{
    /// <summary>
    /// 返回码枚举
    /// 定义命令执行的各种返回状态
    /// 对应协议附录 RETURN CODE 部分
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
        /// 请求参数长度不正确，或ACK数据包超过最大长度
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

    /// <summary>
    /// ReturnCode 枚举的扩展方法类
    /// 提供返回码的描述文本获取功能
    /// </summary>
    public static class ReturnCodeExtensions
    {
        /// <summary>
        /// 获取返回码的中文描述
        /// 根据协议附录RETURN CODE定义返回对应的描述文本
        /// </summary>
        /// <param name="code">返回码枚举值</param>
        /// <returns>返回码对应的中文描述，未知值返回"未知错误码"</returns>
        public static string GetDescription(this ReturnCode code)
        {
#if NET45_OR_GREATER
            switch (code)
            {
                case ReturnCode.Success: return "执行成功";
                case ReturnCode.Failure: return "执行失败";
                case ReturnCode.NotPermittedNow: return "当前状态不支持";
                case ReturnCode.OutOfRange: return "设置值超出范围";
                case ReturnCode.ParamNotSupported: return "参数不支持";
                case ReturnCode.ParamRebootEffect: return "参数需重启生效";
                case ReturnCode.ParamReadOnly: return "参数只读，不支持写入";
                case ReturnCode.ParamInvalidLength: return "请求参数长度错误，或ACK数据包超过最大长度";
                case ReturnCode.ParamKeyNumError: return "参数key_num和key_list不匹配";
                case ReturnCode.UpgradePubKeyError: return "公钥签名验证错误";
                case ReturnCode.UpgradeDigestError: return "固件摘要签名验证错误";
                case ReturnCode.UpgradeFwTypeError: return "固件类型不匹配";
                case ReturnCode.UpgradeFwOutOfRange: return "固件长度超出范围";
                default: return "未知错误码";
            }
#elif NET9_0_OR_GREATER
            return code switch
            {
                ReturnCode.Success => "执行成功",
                ReturnCode.Failure => "执行失败",
                ReturnCode.NotPermittedNow => "当前状态不支持",
                ReturnCode.OutOfRange => "设置值超出范围",
                ReturnCode.ParamNotSupported => "参数不支持",
                ReturnCode.ParamRebootEffect => "参数需重启生效",
                ReturnCode.ParamReadOnly => "参数只读，不支持写入",
                ReturnCode.ParamInvalidLength => "请求参数长度错误，或ACK数据包超过最大长度",
                ReturnCode.ParamKeyNumError => "参数key_num和key_list不匹配",
                ReturnCode.UpgradePubKeyError => "公钥签名验证错误",
                ReturnCode.UpgradeDigestError => "固件摘要签名验证错误",
                ReturnCode.UpgradeFwTypeError => "固件类型不匹配",
                ReturnCode.UpgradeFwOutOfRange => "固件长度超出范围",
                _ => "未知错误码"
            };
#endif
        }

        /// <summary>
        /// 将字节值解析为ReturnCode枚举并获取描述
        /// 用于解析ACK包中的ret_code字段
        /// </summary>
        /// <param name="retCodeByte">ACK包中的ret_code原始字节值</param>
        /// <returns>格式化的返回码描述字符串，如 "0x20 - 参数不支持"</returns>
        public static string GetDescription(byte retCodeByte)
        {
            var code = (ReturnCode)retCodeByte;
            // 检查是否为已定义的枚举值
            if (Enum.IsDefined(typeof(ReturnCode), retCodeByte))
                return $"0x{retCodeByte:X2} - {code.GetDescription()}";
            else
                return $"0x{retCodeByte:X2} - 未知错误码";
        }
    }
}