using System;

namespace LivoxHapController.Utilities
{
    /// <summary>
    /// 点云着色工具类
    /// 基于 Livox Viewer 着色策略实现三种着色模式：
    /// 反射率着色（Reflectivity）、深度着色（Depth）、高度着色（Elevation）
    /// 来源：https://github.com/Livox-SDK/Livox-SDK2/wiki/Livox-Viewer#1-color-coding-strategy
    /// 
    /// 注意：本类不依赖任何 UI 框架（WPF/WinForms），返回纯 (R,G,B) 元组，
    /// 调用方可自行转换为目标框架的颜色类型。
    /// </summary>
    public static class ColorGradient
    {
        #region 公共着色方法

        /// <summary>
        /// 根据反射率生成 RGB 颜色（Livox Viewer 反射率着色算法）
        /// 颜色变化规律：蓝→青→绿→黄→红（低反射率到高反射率）
        /// </summary>
        /// <param name="reflectivity">反射率值（0-255）</param>
        /// <returns>(R, G, B) 颜色元组，各分量范围 0-255</returns>
        public static (byte R, byte G, byte B) GetReflectivityColor(byte reflectivity)
        {
            int cur = reflectivity;
            int resultR, resultG, resultB;

            // 反射率着色算法（来源：Livox-SDK2 Wiki — Livox Viewer 着色策略）
            // 分4段线性映射，每段控制两个颜色通道，形成全光谱渐变
            if (cur < 30)
            {
                // 反射率 0-30：蓝色过渡到青色
                // R 保持0（无红色分量），G 从0线性增至255，B 保持255
                resultR = 0;
                resultG = (cur * 255 / 30) & 0xFF;
                resultB = 0xFF;
            }
            else if (cur < 90)
            {
                // 反射率 30-90：青色过渡到绿色
                // R 保持0，G 保持255，B 从255线性减至0
                resultR = 0;
                resultG = 0xFF;
                resultB = ((90 - cur) * 255 / 60) & 0xFF;
            }
            else if (cur < 150)
            {
                // 反射率 90-150：绿色过渡到黄色
                // R 从0线性增至255，G 保持255，B 保持0
                resultR = ((cur - 90) * 255 / 60) & 0xFF;
                resultG = 0xFF;
                resultB = 0;
            }
            else
            {
                // 反射率 150-255：黄色过渡到红色
                // R 保持255，G 从255线性减至0，B 保持0
                resultR = 0xFF;
                resultG = ((255 - cur) * 255 / (256 - 150)) & 0xFF;
                resultB = 0;
            }

            return ((byte)resultR, (byte)resultG, (byte)resultB);
        }

        /// <summary>
        /// 根据距离（深度）生成 RGB 颜色（Livox Viewer 深度着色算法）
        /// 颜色变化规律：红色（近）→ 蓝色（远），在 HSV 色彩空间中线性插值
        /// </summary>
        /// <param name="distance">当前点的距离值</param>
        /// <param name="minDistance">最近距离（对应红色 Hue=0.0）</param>
        /// <param name="maxDistance">最远距离（对应蓝色 Hue=0.33）</param>
        /// <returns>(R, G, B) 颜色元组，各分量范围 0-255</returns>
        /// <remarks>
        /// 算法来源：Livox-SDK2 Wiki — Depth 着色
        /// HSV 空间：Hue 从 0.0（红）线性过渡到 0.33（蓝），S=1.0, V=1.0 固定
        /// </remarks>
        public static (byte R, byte G, byte B) GetDepthColor(float distance, float minDistance, float maxDistance)
        {
            // HSV 色彩空间参数（固定）
            const float minColorHue = 0.0f;   // 最近距离对应色相（红色）
            const float maxColorHue = 0.33f;  // 最远距离对应色相（蓝色）
            const float saturation = 1.0f;    // 饱和度固定
            const float value = 1.0f;         // 明度固定

            // 按距离线性插值获取色相值
            float resultHue = (distance - minDistance) / (maxDistance - minDistance)
                            * (maxColorHue - minColorHue) + minColorHue;
            resultHue = Clamp(resultHue, minColorHue, maxColorHue);

            // 将 HSV 转换为 RGB
            return HsvToRgb(resultHue, saturation, value);
        }

        /// <summary>
        /// 根据高度（俯仰角）生成 RGB 颜色（Livox Viewer 高度着色算法）
        /// 颜色变化规律：品红色（低）→ 黄色（高），在 RGB 空间中线性插值
        /// </summary>
        /// <param name="elevation">当前点的高度值</param>
        /// <param name="minElevation">最低高度</param>
        /// <param name="maxElevation">最高高度</param>
        /// <returns>(R, G, B) 颜色元组，各分量范围 0-255</returns>
        /// <remarks>
        /// 算法来源：Livox-SDK2 Wiki — Elevation 着色
        /// RGB 空间：R=1.0 固定，B=1.0 固定，G 从 1.0（低处）线性过渡到 0.0（高处）
        /// 低处 RGB=(1,1,1)=品红色，高处 RGB=(1,0,1)=黄色
        /// </remarks>
        public static (byte R, byte G, byte B) GetElevationColor(float elevation, float minElevation, float maxElevation)
        {
            // RGB 色彩空间参数（固定）
            const float minColorG = 1.0f;  // 最低处绿色值（品红色：R=1, G=1, B=1）
            const float maxColorG = 0.0f;  // 最高处绿色值（黄色：R=1, G=0, B=1）
            const float colorR = 1.0f;     // 红色值固定
            const float colorB = 1.0f;     // 蓝色值固定

            // 按高度线性插值获取绿色分量值
            float resultG = (elevation - minElevation) / (maxElevation - minElevation)
                          * (maxColorG - minColorG) + minColorG;
            resultG = Clamp(resultG, maxColorG, minColorG);

            // 将归一化 RGB [0,1] 转换为 [0,255]
            byte r = (byte)(colorR * 255);
            byte g = (byte)(resultG * 255);
            byte b = (byte)(colorB * 255);

            return (r, g, b);
        }

        #endregion

        #region 颜色空间转换辅助方法

        /// <summary>
        /// 将 HSV 颜色转换为 RGB 颜色
        /// </summary>
        /// <param name="h">色相（Hue），范围 [0, 1]</param>
        /// <param name="s">饱和度（Saturation），范围 [0, 1]</param>
        /// <param name="v">明度（Value），范围 [0, 1]</param>
        /// <returns>(R, G, B) 颜色元组，各分量范围 0-255</returns>
        /// <remarks>
        /// 标准 HSV→RGB 转换算法：
        /// 1. 计算色相扇区索引（0-5）
        /// 2. 根据扇区计算临时 RGB (r', g', b')
        /// 3. 加上明度偏移 m = v - c 得到最终 RGB
        /// </remarks>
        private static (byte R, byte G, byte B) HsvToRgb(float h, float s, float v)
        {
            // 饱和度为零时，返回中性灰色
            if (s <= 0f)
            {
                byte gray = (byte)(v * 255);
                return (gray, gray, gray);
            }

            // 计算色度 c = v * s
            float c = v * s;
            // 计算色相扇区：h * 6 映射到 [0, 6)
            float h6 = h * 6f;
            int sector = (int)h6; // 扇区索引 0-5
            // 扇区内偏移量 x = c * (1 - |(h6 mod 2) - 1|)
            float x = c * (1f - Math.Abs(h6 % 2f - 1f));
            // 明度偏移
            float m = v - c;

            float tempR, tempG, tempB;

            // 根据色相扇区确定 (r', g', b')
            switch (sector)
            {
                case 0: tempR = c; tempG = x; tempB = 0f; break;
                case 1: tempR = x; tempG = c; tempB = 0f; break;
                case 2: tempR = 0f; tempG = c; tempB = x; break;
                case 3: tempR = 0f; tempG = x; tempB = c; break;
                case 4: tempR = x; tempG = 0f; tempB = c; break;
                default: tempR = c; tempG = 0f; tempB = x; break; // case 5: 或越界兜底
            }

            // 加上明度偏移并转为 [0,255]
            byte r = (byte)((tempR + m) * 255);
            byte g = (byte)((tempG + m) * 255);
            byte b = (byte)((tempB + m) * 255);

            return (r, g, b);
        }

        #endregion

        #region 通用辅助方法

        /// <summary>
        /// 限制值在指定范围内
        /// </summary>
        /// <param name="value">要限制的值</param>
        /// <param name="min">最小值</param>
        /// <param name="max">最大值</param>
        /// <returns>限制后的值</returns>
        private static float Clamp(float value, float min, float max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        #endregion
    }
}
