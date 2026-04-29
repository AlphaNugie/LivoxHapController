using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LivoxHapController.Utilities
{
    /// <summary>
    /// 涉及16进制字符串与byte数组转换的工具类
    /// </summary>
    public class HexUtils
    {
        #region 16进制转byte
        /// <summary>
        /// 将16进制格式字符串数组转换为byte数组
        /// </summary>
        /// <param name="hexStrings">16进制格式字符串数组，如[ "FE", "FE", ... ]</param>
        /// <returns>返回byte数组</returns>
        public static byte[] HexStringArray2Bytes(IEnumerable<string> hexStrings)
        {
            if (hexStrings == null || !hexStrings.Any())
#if NET45_OR_GREATER
                return null;
#elif NET9_0_OR_GREATER
                return [];
#endif

            var ienum = hexStrings.Select(p => string.IsNullOrWhiteSpace(p) ? (byte)0 : Convert.ToByte(p, 16));
            //return hexStrings.Select(p => string.IsNullOrWhiteSpace(p) ? (byte)0 : Convert.ToByte(p, 16)).ToArray();
            return
#if NET45_OR_GREATER
                ienum.ToArray();
#elif NET9_0_OR_GREATER
                [.. ienum];
#endif
        }

        /// <summary>
        /// 将16进制格式字符串转换为byte数组
        /// </summary>
        /// <param name="hexString">16进制格式字符串，如"FE FE FE ..."</param>
        /// <returns>返回byte数组</returns>
        public static byte[] HexString2Bytes(string hexString)
        {
            if (string.IsNullOrWhiteSpace(hexString))
#if NET45_OR_GREATER
                return null;
#elif NET9_0_OR_GREATER
                return [];
#endif

                return HexStringArray2Bytes(
#if NET45_OR_GREATER
                hexString.Split(new char[] { ' ' },
#elif NET9_0_OR_GREATER
                hexString.Split([' '],
#endif
                    StringSplitOptions.RemoveEmptyEntries));
        }
        #endregion
    }
}
