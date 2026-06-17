#if NET45_OR_GREATER
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
#endif
using System.Diagnostics;

using System.Linq.Expressions;

namespace LivoxHapController.Services
{
    /// <summary>
    /// 点云数据录制器
    /// 将扫描仪原始 UDP 点云数据包按接收顺序写入 .pcr 文件
    /// 支持暂停/恢复、最大时长/截止时间限制、属性绑定暂停
    /// </summary>
    /// <remarks>
    /// 文件格式 (.pcr)：
    /// 每包结构：SegmentFlag(4B) + TimestampTicks(8B) + DataLength(4B) + RawData(N B)
    /// - SegmentFlag: 0xFFFFFFFF=新段开始，0x00000000=普通包
    /// - TimestampTicks: DateTime.UtcNow.Ticks，仅新段标识时有效
    /// - DataLength: 后续原始数据长度
    /// - RawData: 原始 UDP 点云数据
    /// </remarks>
    public class PointCloudRecorder : IDisposable
    {
        #region 常量

        /// <summary>新段开始标识</summary>
        private const uint SegmentFlagNew = 0xFFFFFFFF;

        /// <summary>普通数据包标识</summary>
        private const uint SegmentFlagData = 0x00000000;

        #endregion

        #region 私有字段

        /// <summary>文件写入流</summary>
#if NET9_0_OR_GREATER
        private FileStream? _fileStream;
        private BinaryWriter? _writer;
        private Timer? _pauseBindingTimer;
        private Timer? _limitCheckTimer;
#elif NET45_OR_GREATER
        private FileStream _fileStream;
        private BinaryWriter _writer;
        private Timer _pauseBindingTimer;
        private Timer _limitCheckTimer;
#endif

        /// <summary>同步锁</summary>
#if NET9_0_OR_GREATER
        private readonly Lock _syncRoot = new();
#elif NET45_OR_GREATER
        private readonly object _syncRoot = new object();
#endif

        /// <summary>是否正在录制</summary>
        private volatile bool _isRecording;

        /// <summary>是否已暂停</summary>
        private volatile bool _isPaused;

        /// <summary>是否已释放</summary>
        private volatile bool _disposed;

        /// <summary>累计录制包数</summary>
        private int _totalPackets;

        /// <summary>当前录制段起始时间</summary>
        private DateTime _segmentStartTime;

        /// <summary>实际录制时长计时器（排除暂停时间），仅在非暂停时运行</summary>
        private Stopwatch
            //.net 9框架下使返回对象可为空
#if NET9_0_OR_GREATER
            ?
#endif
            _activeRecordingStopwatch;

        /// <summary>暂停绑定信息</summary>
        private PauseBinding
            //.net 9框架下使返回对象可为空
#if NET9_0_OR_GREATER
            ?
#endif
            _pauseBinding;

        #endregion

        #region 属性

        /// <summary>是否正在录制</summary>
        public bool IsRecording { get { return _isRecording; } }

        /// <summary>是否已暂停</summary>
        public bool IsPaused { get { return _isPaused; } }

        /// <summary>当前录制段开始时间</summary>
        public DateTime SegmentStartTime { get { return _segmentStartTime; } }

        /// <summary>累计录制包数</summary>
        public int TotalRecordedPackets { get { return _totalPackets; } }

        /// <summary>已录制总时长（排除暂停时间，录制中返回实时值）</summary>
        public TimeSpan Elapsed
        {
            get
            {
                if (_activeRecordingStopwatch != null && _activeRecordingStopwatch.IsRunning)
                    return _activeRecordingStopwatch.Elapsed;
                return _activeRecordingStopwatch != null ? _activeRecordingStopwatch.Elapsed : TimeSpan.Zero;
            }
        }

        /// <summary>最大持续时长（超限自动停止），null=不限制</summary>
        public TimeSpan? MaxDuration { get; set; }

        /// <summary>截止时间（超限自动停止），null=不限制</summary>
        public DateTime? Deadline { get; set; }

        /// <summary>当前录制文件路径</summary>
        public string
#if NET9_0_OR_GREATER
            ?
#endif
            FilePath { get; private set; }

        #endregion

        #region 事件

        /// <summary>达到最大录制时长时触发</summary>
        public event EventHandler
#if NET9_0_OR_GREATER
            ?
#endif
            MaxDurationReached;

        /// <summary>达到截止时间时触发</summary>
        public event EventHandler
#if NET9_0_OR_GREATER
            ?
#endif
            DeadlineReached;

        /// <summary>录制停止时触发（含手动停止和自动停止）</summary>
        public event EventHandler
#if NET9_0_OR_GREATER
            ?
#endif
            RecordingStopped;

        #endregion

        #region 录制控制

        /// <summary>
        /// 开始录制
        /// </summary>
        /// <param name="filePath">输出文件路径（建议后缀 .pcr）</param>
        /// <exception cref="InvalidOperationException">已在录制中</exception>
        /// <exception cref="ArgumentNullException">filePath 为空</exception>
        public void Start(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentNullException(nameof(filePath));
            if (_isRecording)
                throw new InvalidOperationException("已在录制中，请先调用 Stop()");

            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            _fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read);
            _writer = new BinaryWriter(_fileStream);
            FilePath = filePath;
            _totalPackets = 0;
            _isPaused = false;
            _isRecording = true;

            // 创建并启动实际录制时长计时器（排除暂停时间）
            _activeRecordingStopwatch = new Stopwatch();
            _activeRecordingStopwatch.Start();

            // 写入段开始标识+时间戳
            WriteSegmentHeader();

            // 启动时间限制检查（100ms 周期）
            if (MaxDuration != null || Deadline != null)
                _limitCheckTimer = new Timer(OnLimitCheck, null, 100, 100);

            // 如果有暂停绑定，启动属性轮询
            if (_pauseBinding != null)
                _pauseBindingTimer = new Timer(OnPauseBindingCheck, null, 100, 100);
        }

        /// <summary>
        /// 停止录制
        /// </summary>
        public void Stop()
        {
            lock (_syncRoot)
            {
                if (!_isRecording) return;

                _isRecording = false;

                // 停止定时器
                _limitCheckTimer?.Dispose();
                _limitCheckTimer = null;
                _pauseBindingTimer?.Dispose();
                _pauseBindingTimer = null;

                // 停止实际录制时长计时器
                _activeRecordingStopwatch?.Stop();

                // 关闭文件流
                _writer?.Flush();
                _writer?.Dispose();
                _writer = null;
                _fileStream?.Dispose();
                _fileStream = null;
            }

            RecordingStopped?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 暂停录制（同时暂停实际录制时长计时器）
        /// </summary>
        public void Pause()
        {
            _isPaused = true;
            _activeRecordingStopwatch?.Stop();
        }

        /// <summary>
        /// 恢复录制（写入新段标识+时间戳，同时恢复实际录制时长计时器）
        /// </summary>
        public void Resume()
        {
            if (!_isRecording || !_isPaused) return;

            _isPaused = false;
            _activeRecordingStopwatch?.Start();
            WriteSegmentHeader();
        }

        #endregion

        #region 数据写入

        /// <summary>
        /// 记录一个点云数据包
        /// 暂停状态或未录制时忽略
        /// </summary>
        /// <param name="rawData">原始 UDP 点云数据</param>
        public void Record(byte[] rawData)
        {
            if (!_isRecording || _isPaused || rawData == null || rawData.Length == 0)
                return;

            lock (_syncRoot)
            {
                if (!_isRecording || _writer == null) return;

                try
                {
                    // SegmentFlag: 普通数据包
                    _writer.Write(SegmentFlagData);
                    // TimestampTicks: 0（普通包不使用）
                    _writer.Write(0L);
                    // DataLength
                    _writer.Write(rawData.Length);
                    // RawData
                    _writer.Write(rawData);

                    _totalPackets++;
                }
                catch (ObjectDisposedException)
                {
                    // 文件流已关闭，忽略
                }
            }
        }

        #endregion

        #region 暂停绑定

        /// <summary>
        /// 将暂停动作绑定到指定对象的 bool 属性
        /// 属性变为 true → Pause()；变为 false → Resume()
        /// 支持多个绑定（后续绑定覆盖前一个）
        /// <para/> BindPauseToProperty(viewModel, vm => vm.ShouldPause);
        /// </summary>
        /// <typeparam name="T">目标对象类型</typeparam>
        /// <param name="target">目标对象实例</param>
        /// <param name="propertySelector">bool 属性选择器 <para/>BindPauseToProperty(viewModel, vm => vm.ShouldPause);</param>
        /// <exception cref="ArgumentNullException">target 或 propertySelector 为 null</exception>
        public void BindPauseToProperty<T>(T target, Expression<Func<T, bool>> propertySelector) where T : class
        {
#if NET45_OR_GREATER
            if (target == null)
                throw new ArgumentNullException(nameof(target));
            if (propertySelector == null)
                throw new ArgumentNullException(nameof(propertySelector));
#elif NET9_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(propertySelector);
#endif

            // 解析属性名
#if NET45_OR_GREATER
            if (!(propertySelector.Body is MemberExpression memberExpr))
#elif NET9_0_OR_GREATER
            if (propertySelector.Body is not MemberExpression memberExpr)
#endif
                throw new ArgumentException("表达式必须是属性选择器，如 vm => vm.IsPaused");

            string propertyName = memberExpr.Member.Name;

            _pauseBinding = new PauseBinding
            {
                Target = target,
                PropertyName = propertyName,
                //LastValue = GetPropertyValue(target, propertyName)
            };

            // 如果已在录制，启动轮询
            if (_isRecording && _pauseBindingTimer == null)
            {
                _pauseBindingTimer = new Timer(OnPauseBindingCheck, null, 100, 100);
            }
        }

        /// <summary>
        /// 解除暂停绑定
        /// </summary>
        public void UnbindPause()
        {
            _pauseBinding = null;
            _pauseBindingTimer?.Dispose();
            _pauseBindingTimer = null;
        }

        /// <summary>
        /// 暂停绑定轮询回调（100ms）
        /// </summary>
        private void OnPauseBindingCheck(object
            //.net 9框架下使返回对象可为空
#if NET9_0_OR_GREATER
            ?
#endif
            state)
        {
            if (_pauseBinding == null || !_isRecording) return;

            try
            {
                bool? currentValue = GetPropertyValue(_pauseBinding.Target, _pauseBinding.PropertyName);
                // 如果属性值为 null（适用于 bool? 类型），则不进行状态切换
                if (!currentValue.HasValue)
                    return;

                //// 首次检测时同步初始值，避免 LastValue=null 导致的误触发
                //if (!_pauseBinding.LastValue.HasValue)
                //{
                //    _pauseBinding.LastValue = currentValue;
                //    return;
                //}

                if (currentValue != _pauseBinding.LastValue)
                {
                    _pauseBinding.LastValue = currentValue;
                    if (currentValue.Value)
                        Pause();
                    else
                        Resume();
                }
            }
            catch
            {
                // 目标对象可能已释放，忽略异常
            }
        }

        /// <summary>
        /// 通过反射获取对象属性值
        /// 当获取到的属性值不是 bool 或 bool? 类型时抛出异常
        /// <para/> 假如获取到的属性值为null，则返回 null（适用于 bool? 类型的属性）
        /// </summary>
#if NET45_OR_GREATER
        private static bool? GetPropertyValue(object target, string propertyName)
#elif NET9_0_OR_GREATER
        private static bool? GetPropertyValue(object? target, string? propertyName)
#endif
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target), "目标对象不能为空");

            if (propertyName == null)
                throw new ArgumentNullException(nameof(propertyName), "属性名称不能为空");

            var prop = target.GetType().GetProperty(propertyName);
            if (prop == null || (prop.PropertyType != typeof(bool) && prop.PropertyType != typeof(bool?)))
                throw new ArgumentException(string.Format("属性 {0} 不存在或不是 bool 类型", propertyName));

            return (bool?)prop.GetValue(target);
        }
        //private static bool GetPropertyValue(object target, string propertyName)
        //{
        //    var prop = target.GetType().GetProperty(propertyName);
        //    if (prop == null || prop.PropertyType != typeof(bool))
        //        throw new ArgumentException(string.Format("属性 {0} 不存在或不是 bool 类型", propertyName));

        //    return (bool)prop.GetValue(target);
        //}

        #endregion

        #region 时间限制检查

        /// <summary>
        /// 时间限制检查回调（100ms）
        /// 使用 Stopwatch 计量实际录制时长（排除暂停时间），与 MaxDuration 比较
        /// </summary>
        private void OnLimitCheck(object
            //.net 9框架下使返回对象可为空
#if NET9_0_OR_GREATER
            ?
#endif
            state)
        {
            if (!_isRecording) return;

            // 检查最大时长：使用 Stopwatch 的实际录制时长（已排除暂停时间）
            if (MaxDuration != null && _activeRecordingStopwatch != null)
            {
                if (_activeRecordingStopwatch.Elapsed >= MaxDuration)
                {
                    MaxDurationReached?.Invoke(this, EventArgs.Empty);
                    Stop();
                    return;
                }
            }

            // 检查截止时间（绝对时间，与暂停无关）
            if (Deadline != null && DateTime.Now >= Deadline)
            {
                DeadlineReached?.Invoke(this, EventArgs.Empty);
                Stop();
                return;
            }
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 写入新段标识头
        /// </summary>
        private void WriteSegmentHeader()
        {
            lock (_syncRoot)
            {
                if (_writer == null) return;

                _segmentStartTime = DateTime.UtcNow;
                _writer.Write(SegmentFlagNew);
                _writer.Write(_segmentStartTime.Ticks);
                _writer.Write(0); // DataLength=0 表示段头无数据
            }
        }

        #endregion

        #region 资源释放

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            Stop();
            UnbindPause();

            // 添加此行以防止执行终结器
            GC.SuppressFinalize(this);
        }

        #endregion

        #region 内部数据结构

        /// <summary>
        /// 暂停绑定信息
        /// </summary>
        private class PauseBinding
        {
            /// <summary>绑定目标对象</summary>
            public object
            //.net 9框架下使返回对象可为空
#if NET9_0_OR_GREATER
            ?
#endif
 Target;

            /// <summary>绑定的属性名</summary>
            public string
            //.net 9框架下使返回对象可为空
#if NET9_0_OR_GREATER
            ?
#endif
 PropertyName;

            /// <summary>上次检测的属性值</summary>
            //public bool LastValue;
            public bool? LastValue;
        }

        #endregion
    }
}
