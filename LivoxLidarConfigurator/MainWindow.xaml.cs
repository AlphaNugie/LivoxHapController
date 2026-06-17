using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using LivoxHapController.Config;
using LivoxHapController.Enums;
using LivoxHapController.Models;
using LivoxHapController.Services;
using LivoxHapController.Services.Parsers;
using Microsoft.Win32;

namespace LivoxLidarConfigurator
{
    /// <summary>
    /// MainWindow 的交互逻辑
    /// 提供 Livox HAP LiDAR 设备发现、连接、配置、扫描的完整生命周期管理界面
    /// </summary>
    public partial class MainWindow : Window
    {
        #region 字段

        /// <summary>LivoxHapRadar 上层管理类实例，封装设备发现、连接、配置、扫描的完整流程</summary>
        private LivoxHapRadar? _radar;

        /// <summary>已发现设备的可观察集合，用于 ListBox 数据绑定</summary>
        private readonly ObservableCollection<DeviceListItem> _devices = [];

        /// <summary>点云数据接收计数器，用于统计收到的点云包数</summary>
        private long _pclPacketCount;

        /// <summary>点云数据接收总字节数</summary>
        private long _pclTotalBytes;

        /// <summary>是否正在录制点云数据（用于切换按钮文字和行为）</summary>
        private bool _isRecording;

        /// <summary>是否正在发现设备</summary>
        private bool _isDiscovering;

        #endregion

        #region 构造函数

        /// <summary>
        /// 构造 MainWindow
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();
            LstDevices.ItemsSource = _devices;
            Log("应用程序已启动，请先初始化并发现设备。");
        }

        #endregion

        #region 初始化与发现

        /// <summary>
        /// 浏览选择配置文件
        /// </summary>
        private void BtnBrowseConfig_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "JSON配置文件|*.json|所有文件|*.*",
                Title = "选择配置文件"
            };
            if (dlg.ShowDialog() == true)
                TxtConfigFile.Text = dlg.FileName;
        }

        /// <summary>
        /// 初始化雷达管理器（从配置文件加载）
        /// </summary>
        private void BtnInitialize_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _radar = new LivoxHapRadar();

                // 订阅事件
                SubscribeRadarEvents();

                // 从配置文件初始化
                _radar.Initialize(TxtConfigFile.Text.Trim());
                Log($"初始化成功，配置文件: {TxtConfigFile.Text.Trim()}");

                // 更新UI状态
                UpdateUiAfterInit("配置文件");
            }
            catch (Exception ex)
            {
                LogError($"初始化失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 直接初始化雷达管理器（不依赖配置文件）
        /// 使用当前主机IP文本框的值和配置面板中的端口参数，通过 AppConfigBuilder 创建配置进行初始化
        /// </summary>
        private void BtnInitDirect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string hostIp = TxtHostIp.Text.Trim();
                if (string.IsNullOrWhiteSpace(hostIp))
                {
                    LogError("请输入主机IP地址");
                    return;
                }

                // 验证并解析端口号（仅当用户已输入非空值时才覆盖默认配置）
                int? cmdPort = TryParsePort(TxtCfgCmdPort.Text, "命令端口");
                int? pointPort = TryParsePort(TxtCfgPointPort.Text, "点云端口");
                int? imuPort = TryParsePort(TxtCfgImuPort.Text, "IMU端口");
                int? pushPort = TryParsePort(TxtCfgPushPort.Text, "推送端口");
                // 如果任一端口验证失败，终止初始化
                if (cmdPort == null || pointPort == null || imuPort == null || pushPort == null)
                    return;

                _radar = new LivoxHapRadar();

                // 订阅事件
                SubscribeRadarEvents();

                // 使用 AppConfigBuilder 构建配置，应用用户在配置面板中输入的端口参数
                var config = AppConfigBuilder.FromConfig(new AppConfig())
                    .WithHostIp(hostIp)
                    .WithPointDataPort(pointPort)
                    .Build();

                // 手动覆盖 AppConfigBuilder 尚未支持的端口字段（命令端口、IMU端口、推送端口）
                if (config.HapConfig.HostNetInfo.Count > 0)
                {
                    config.HapConfig.HostNetInfo[0].CmdDataPort = cmdPort.Value;
                    config.HapConfig.HostNetInfo[0].ImuDataPort = imuPort.Value;
                    config.HapConfig.HostNetInfo[0].PushMsgPort = pushPort.Value;
                }
                if (config.Mid360Config.HostNetInfo.Count > 0)
                {
                    config.Mid360Config.HostNetInfo[0].CmdDataPort = cmdPort.Value;
                    config.Mid360Config.HostNetInfo[0].ImuDataPort = imuPort.Value;
                    config.Mid360Config.HostNetInfo[0].PushMsgPort = pushPort.Value;
                }

                // 使用构建好的 AppConfig 对象直接初始化
                _radar.Initialize(appConfig: config);
                Log($"直接初始化成功，主机IP: {hostIp}");

                // 更新UI状态
                UpdateUiAfterInit("直接");
            }
            catch (Exception ex)
            {
                LogError($"直接初始化失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 订阅雷达管理器事件
        /// 两种初始化方式共用此方法，避免重复代码
        /// </summary>
        private void SubscribeRadarEvents()
        {
            if (_radar == null) return;

            _radar.DeviceDiscovered += Radar_DeviceDiscovered;
            _radar.AckResponseReceived += Radar_AckResponseReceived;
            _radar.PushMessageReceived += Radar_PushMessageReceived;
            _radar.DeviceStatusUpdated += Radar_DeviceStatusUpdated;
            _radar.PointCloudDataReceived += Radar_PointCloudDataReceived;
            _radar.ImuDataReceived += Radar_ImuDataReceived;
        }

        /// <summary>
        /// 初始化成功后更新UI状态
        /// 两种初始化方式共用此方法
        /// </summary>
        /// <param name="initMode">初始化模式名称（用于日志）</param>
        private void UpdateUiAfterInit(string initMode)
        {
            BtnInitialize.IsEnabled = false;
            BtnInitDirect.IsEnabled = false;
            BtnDiscover.IsEnabled = true;
            TxtConfigFile.IsEnabled = false;

            // 从当前配置中读取端口值回填到网络配置面板，使UI与实际配置一致
            FillNetworkConfigFromRadar();
        }

        /// <summary>
        /// 开始广播发现设备
        /// </summary>
        private void BtnDiscover_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_radar == null || !_radar.IsInitialized)
                {
                    LogError("请先初始化");
                    return;
                }

                _radar.Discover(TxtHostIp.Text.Trim());
                _isDiscovering = true;

                BtnDiscover.IsEnabled = false;
                BtnStopDiscover.IsEnabled = true;
                Log($"开始发现设备，主机IP: {TxtHostIp.Text.Trim()}");
            }
            catch (Exception ex)
            {
                LogError($"启动发现失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 停止设备发现
        /// </summary>
        private void BtnStopDiscover_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _isDiscovering = false;
                // 注意：当前LivoxHapRadar未提供单独停止发现的方法，停止需要Dispose
                BtnDiscover.IsEnabled = true;
                BtnStopDiscover.IsEnabled = false;
                Log("已停止发现新设备（已有设备仍可连接）");
            }
            catch (Exception ex)
            {
                LogError($"停止发现失败: {ex.Message}");
            }
        }

        #endregion

        #region 设备列表与连接

        /// <summary>
        /// 设备列表选中变更事件处理
        /// 更新设备信息面板和连接按钮状态
        /// </summary>
        private void LstDevices_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            var item = LstDevices.SelectedItem as DeviceListItem;
            bool selected = item != null;

            BtnConnect.IsEnabled = selected && _radar is { IsConnected: false };
            UpdateDeviceInfoPanel(item?.DeviceInfo);
        }

        /// <summary>
        /// 连接到选中设备
        /// </summary>
        private void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (LstDevices.SelectedItem is not DeviceListItem item || _radar == null) return;

                _radar.Connect(item.DeviceInfo);
                item.IsConnected = true;

                // 通知UI更新连接状态指示灯颜色
                item.NotifyStateChanged();

                // 更新UI
                UpdateConnectionState(true);
                PnlDeviceInfo.IsEnabled = true;
                PnlConfig.IsEnabled = true;
                Log($"已连接设备: {item.DeviceInfo.LidarIpString} (SN: {item.DeviceInfo.SerialNumberString})");
            }
            catch (Exception ex)
            {
                LogError($"连接失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 断开当前设备连接
        /// 断开后允许重新初始化，支持完整生命周期管理
        /// </summary>
        private void BtnDisconnect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_radar == null) return;

                _radar.Disconnect();
                // 更新所有设备的连接状态，并通知UI刷新指示灯颜色
                foreach (var d in _devices)
                {
                    d.IsConnected = false;
                    d.NotifyStateChanged();
                }

                UpdateConnectionState(false);
                PnlConfig.IsEnabled = false;

                // 断开连接后重置录制状态
                ResetRecordingState();

                // 断开连接后重新允许初始化，支持重复初始化场景
                BtnInitialize.IsEnabled = true;
                BtnInitDirect.IsEnabled = true;
                BtnDiscover.IsEnabled = false;
                TxtConfigFile.IsEnabled = true;

                Log("已断开设备连接，可重新初始化");
            }
            catch (Exception ex)
            {
                LogError($"断开失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 更新连接状态的UI显示
        /// </summary>
        /// <param name="connected">是否已连接</param>
        private void UpdateConnectionState(bool connected)
        {
            ConnIndicator.Fill = connected ? Brushes.LimeGreen : Brushes.Gray;
            TxtConnStatus.Text = connected ? "已连接" : "未连接";
            TxtConnStatus.Foreground = connected ? Brushes.LimeGreen : Brushes.Gray;
            BtnConnect.IsEnabled = !connected && LstDevices.SelectedItem != null;
            BtnDisconnect.IsEnabled = connected;
        }

        /// <summary>
        /// 更新设备信息面板显示
        /// </summary>
        /// <param name="device">设备信息，为null时清空</param>
        private void UpdateDeviceInfoPanel(LidarDeviceInfo? device)
        {
            if (device == null)
            {
                TxtDeviceType.Text = "";
                TxtSerialNumber.Text = "";
                TxtLidarIp.Text = "";
                TxtCommandPort.Text = "";
                TxtDiscoveredTime.Text = "";
                TxtConnectedState.Text = "";
                return;
            }

            TxtDeviceType.Text = device.DeviceTypeName;
            TxtSerialNumber.Text = device.SerialNumberString;
            TxtLidarIp.Text = device.LidarIpString;
            TxtCommandPort.Text = device.CommandPort.ToString();
            TxtDiscoveredTime.Text = device.DiscoveredTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            TxtConnectedState.Text = device.IsConnected ? "已连接" : "未连接";
            TxtConnectedState.Foreground = device.IsConnected ? Brushes.LimeGreen : Brushes.Gray;
        }

        #endregion

        #region 扫描控制

        /// <summary>
        /// 启动正常扫描
        /// </summary>
        private void BtnStartScan_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                EnsureRadarConnected();
                _radar!.StartScan();

                ScanIndicator.Fill = Brushes.LimeGreen;
                TxtScanStatus.Text = "扫描中";
                TxtScanStatus.Foreground = Brushes.LimeGreen;
                _pclPacketCount = 0;
                _pclTotalBytes = 0;
                Log("扫描已启动");
            }
            catch (Exception ex)
            {
                LogError($"启动扫描失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 停止扫描（进入休眠模式）
        /// 停止时同步清零点云统计数据并刷新UI
        /// </summary>
        private void BtnStopScan_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                EnsureRadarConnected();
                _radar!.StopScan();

                ScanIndicator.Fill = Brushes.Gray;
                TxtScanStatus.Text = "未扫描";
                TxtScanStatus.Foreground = Brushes.Gray;

                // 清零点云统计数据并刷新UI显示
                _pclPacketCount = 0;
                _pclTotalBytes = 0;
                TxtPclStats.Text = "";

                Log("扫描已停止");
            }
            catch (Exception ex)
            {
                LogError($"停止扫描失败: {ex.Message}");
            }
        }

        #endregion

        #region 录制控制

        /// <summary>
        /// 录制按钮点击事件处理
        /// 首次点击：弹出保存文件对话框选择 .pcr 文件路径，开始录制点云数据
        /// 再次点击：停止录制，按钮恢复为"开始录制"
        /// 录制功能依赖于 _radar.Recorder（PointCloudRecorder），由 LivoxHapRadar 内部自动在收到点云时调用 Record()
        /// </summary>
        private void BtnRecord_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_radar == null)
                {
                    LogError("请先初始化雷达管理器");
                    return;
                }

                if (!_isRecording)
                {
                    // 开始录制：弹出保存文件对话框
                    var dlg = new SaveFileDialog
                    {
                        Filter = "PCR点云录制文件|*.pcr|所有文件|*.*",
                        Title = "选择录制文件保存路径",
                        DefaultExt = ".pcr",
                        FileName = $"LivoxHap_{DateTime.Now:yyyyMMdd_HHmmss}.pcr"
                    };

                    if (dlg.ShowDialog() == true)
                    {
                        _radar.StartRecording(dlg.FileName);
                        _isRecording = true;

                        // 更新UI：按钮文字、指示灯、状态文本
                        BtnRecord.Content = "停止录制";
                        BtnRecord.Style = (Style)FindResource("DangerButton");
                        RecordIndicator.Fill = System.Windows.Media.Brushes.Red;
                        TxtRecordStatus.Text = $"录制中: {System.IO.Path.GetFileName(dlg.FileName)}";
                        TxtRecordStatus.Foreground = System.Windows.Media.Brushes.Red;

                        Log($"开始录制点云数据: {dlg.FileName}");
                    }
                }
                else
                {
                    // 停止录制
                    _radar.StopRecording();
                    _isRecording = false;

                    // 恢复UI：按钮文字、指示灯、状态文本
                    BtnRecord.Content = "开始录制";
                    BtnRecord.Style = (Style)FindResource("ActionButton");
                    RecordIndicator.Fill = System.Windows.Media.Brushes.Gray;
                    TxtRecordStatus.Text = "未录制";
                    TxtRecordStatus.Foreground = System.Windows.Media.Brushes.Gray;

                    Log($"停止录制点云数据（共录制 {_radar.Recorder.TotalRecordedPackets} 包）");
                }
            }
            catch (Exception ex)
            {
                LogError($"录制操作失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 重置录制状态（断开连接或停止扫描时调用）
        /// 如果正在录制则先停止，然后恢复UI为初始状态
        /// </summary>
        private void ResetRecordingState()
        {
            if (_isRecording)
            {
                _radar?.StopRecording();
                _isRecording = false;
            }

            // 恢复录制UI为初始状态
            BtnRecord.Content = "开始录制";
            BtnRecord.Style = (Style)FindResource("ActionButton");
            RecordIndicator.Fill = System.Windows.Media.Brushes.Gray;
            TxtRecordStatus.Text = "未录制";
            TxtRecordStatus.Foreground = System.Windows.Media.Brushes.Gray;
        }

        #endregion

        #region 网络配置

        /// <summary>
        /// 手动应用网络配置
        /// 带输入验证：检查IP地址格式和端口范围
        /// </summary>
        private void BtnApplyNetwork_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                EnsureRadarConnected();

                // 验证主机IP地址格式
                string hostIp = TxtCfgHostIp.Text.Trim();
                if (!ValidateIpAddress(hostIp, "主机IP"))
                    return;

                // 验证并解析各端口号
                if (!ValidateAndParseUshort(TxtCfgCmdPort.Text.Trim(), "命令端口", out ushort cmdPort)) return;
                if (!ValidateAndParseUshort(TxtCfgPointPort.Text.Trim(), "点云端口", out ushort pointPort)) return;
                if (!ValidateAndParseUshort(TxtCfgImuPort.Text.Trim(), "IMU端口", out ushort imuPort)) return;
                if (!ValidateAndParseUshort(TxtCfgPushPort.Text.Trim(), "推送端口", out ushort pushPort)) return;

                _radar!.Configure(hostIp, cmdPort, pointPort, imuPort, pushPort);
                Log($"网络配置已发送: HostIp={hostIp}");
            }
            catch (Exception ex)
            {
                LogError($"应用网络配置失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 从配置文件应用网络配置
        /// </summary>
        private void BtnApplyConfigFile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                EnsureRadarConnected();
                _radar!.ConfigureFromConfig();
                Log("已从配置文件应用网络配置");
            }
            catch (Exception ex)
            {
                LogError($"应用配置文件失败: {ex.Message}");
            }
        }

        #endregion

        #region 点云数据配置

        /// <summary>
        /// 设置点云数据类型
        /// </summary>
        private void BtnSetPclDataType_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                EnsureRadarConnected();
                byte val = byte.Parse(((System.Windows.Controls.ComboBoxItem)CmbPclDataType.SelectedItem).Tag.ToString()!);
                _radar!.SetPclDataType(val);
                Log($"已设置点云数据类型: {CmbPclDataType.Text}");
            }
            catch (Exception ex)
            {
                LogError($"设置点云数据类型失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 设置扫描模式
        /// </summary>
        private void BtnSetScanPattern_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                EnsureRadarConnected();
                byte val = byte.Parse(((System.Windows.Controls.ComboBoxItem)CmbScanPattern.SelectedItem).Tag.ToString()!);
                _radar!.SetScanPattern(val);
                Log($"已设置扫描模式: {CmbScanPattern.Text}");
            }
            catch (Exception ex)
            {
                LogError($"设置扫描模式失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 设置双发射模式
        /// </summary>
        private void BtnSetDualEmit_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                EnsureRadarConnected();
                bool enable = ChkDualEmit.IsChecked == true;
                _radar!.SetDualEmit(enable);
                Log($"已设置双发射: {(enable ? "启用" : "禁用")}");
            }
            catch (Exception ex)
            {
                LogError($"设置双发射失败: {ex.Message}");
            }
        }

        #endregion

        #region IMU 控制

        /// <summary>
        /// 启用IMU数据
        /// </summary>
        private void BtnEnableImu_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                EnsureRadarConnected();
                _radar!.EnableImuData();
                Log("已发送启用IMU数据命令");
            }
            catch (Exception ex)
            {
                LogError($"启用IMU失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 禁用IMU数据
        /// </summary>
        private void BtnDisableImu_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                EnsureRadarConnected();
                _radar!.DisableImuData();
                Log("已发送禁用IMU数据命令");
            }
            catch (Exception ex)
            {
                LogError($"禁用IMU失败: {ex.Message}");
            }
        }

        #endregion

        #region 安装姿态

        /// <summary>
        /// 设置安装姿态（Roll/Pitch/Yaw角度 + X/Y/Z偏移）
        /// 带输入验证：检查角度和偏移值是否为合法数值
        /// </summary>
        private void BtnSetAttitude_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                EnsureRadarConnected();

                // 验证并解析各安装姿态参数
                if (!ValidateAndParseFloat(TxtAttRoll.Text.Trim(), "Roll角度", out float roll)) return;
                if (!ValidateAndParseFloat(TxtAttPitch.Text.Trim(), "Pitch角度", out float pitch)) return;
                if (!ValidateAndParseFloat(TxtAttYaw.Text.Trim(), "Yaw角度", out float yaw)) return;
                if (!ValidateAndParseInt(TxtAttX.Text.Trim(), "X偏移", out int x)) return;
                if (!ValidateAndParseInt(TxtAttY.Text.Trim(), "Y偏移", out int y)) return;
                if (!ValidateAndParseInt(TxtAttZ.Text.Trim(), "Z偏移", out int z)) return;

                _radar!.SetInstallAttitude(roll, pitch, yaw, x, y, z);
                Log($"已设置安装姿态: R={roll}, P={pitch}, Y={yaw}, X={x}, Y={y}, Z={z}");
            }
            catch (Exception ex)
            {
                LogError($"设置安装姿态失败: {ex.Message}");
            }
        }

        #endregion

        #region FOV 配置

        /// <summary>
        /// 设置FOV配置0
        /// 带输入验证：检查各角度值是否为合法整数
        /// </summary>
        private void BtnSetFov0_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                EnsureRadarConnected();

                // 验证并解析FOV0各角度参数
                if (!ValidateAndParseInt(TxtFov0YawStart.Text.Trim(), "FOV0 Yaw起始", out int yawStart)) return;
                if (!ValidateAndParseInt(TxtFov0YawStop.Text.Trim(), "FOV0 Yaw结束", out int yawStop)) return;
                if (!ValidateAndParseInt(TxtFov0PitchStart.Text.Trim(), "FOV0 Pitch起始", out int pitchStart)) return;
                if (!ValidateAndParseInt(TxtFov0PitchStop.Text.Trim(), "FOV0 Pitch结束", out int pitchStop)) return;

                _radar!.SetFovConfig0(yawStart, yawStop, pitchStart, pitchStop);
                Log($"已设置FOV配置0: Yaw=[{yawStart},{yawStop}] Pitch=[{pitchStart},{pitchStop}]");
            }
            catch (Exception ex)
            {
                LogError($"设置FOV0失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 设置FOV配置1
        /// 使用独立的FOV1输入框，带输入验证
        /// </summary>
        private void BtnSetFov1_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                EnsureRadarConnected();

                // 验证并解析FOV1各角度参数（使用FOV1专属输入框）
                if (!ValidateAndParseInt(TxtFov1YawStart.Text.Trim(), "FOV1 Yaw起始", out int yawStart)) return;
                if (!ValidateAndParseInt(TxtFov1YawStop.Text.Trim(), "FOV1 Yaw结束", out int yawStop)) return;
                if (!ValidateAndParseInt(TxtFov1PitchStart.Text.Trim(), "FOV1 Pitch起始", out int pitchStart)) return;
                if (!ValidateAndParseInt(TxtFov1PitchStop.Text.Trim(), "FOV1 Pitch结束", out int pitchStop)) return;

                _radar!.SetFovConfig1(yawStart, yawStop, pitchStart, pitchStop);
                Log($"已设置FOV配置1: Yaw=[{yawStart},{yawStop}] Pitch=[{pitchStart},{pitchStop}]");
            }
            catch (Exception ex)
            {
                LogError($"设置FOV1失败: {ex.Message}");
            }
        }

        #endregion

        #region 盲区设置

        /// <summary>
        /// 设置盲区距离
        /// 带输入验证：检查盲区值是否为合法数值且在50-200范围内
        /// </summary>
        private void BtnSetBlindSpot_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                EnsureRadarConnected();

                // 验证盲区距离为合法数值
                if (!ValidateAndParseUInt(TxtBlindSpot.Text.Trim(), "盲区距离", out uint val)) return;

                // 验证盲区范围（50-200cm）
                if (val < 50 || val > 200)
                {
                    LogError($"盲区距离超出范围：{val}cm，有效范围为50-200cm");
                    return;
                }

                _radar!.SetBlindSpot(val);
                Log($"已设置盲区: {val}cm");
            }
            catch (Exception ex)
            {
                LogError($"设置盲区失败: {ex.Message}");
            }
        }

        #endregion

        #region 加热控制

        /// <summary>
        /// 启用窗口加热
        /// </summary>
        private void BtnEnableGlassHeat_Click(object sender, RoutedEventArgs e)
        {
            ExecuteCommand("启用窗口加热", r => r.EnableGlassHeat());
        }

        /// <summary>
        /// 禁用窗口加热
        /// </summary>
        private void BtnDisableGlassHeat_Click(object sender, RoutedEventArgs e)
        {
            ExecuteCommand("禁用窗口加热", r => r.DisableGlassHeat());
        }

        /// <summary>
        /// 启用强制加热
        /// </summary>
        private void BtnStartForceHeat_Click(object sender, RoutedEventArgs e)
        {
            ExecuteCommand("启用强制加热", r => r.StartForcedHeating());
        }

        /// <summary>
        /// 停止强制加热
        /// </summary>
        private void BtnStopForceHeat_Click(object sender, RoutedEventArgs e)
        {
            ExecuteCommand("停止强制加热", r => r.StopForcedHeating());
        }

        #endregion

        #region 开机模式

        /// <summary>
        /// 设置开机工作模式
        /// </summary>
        private void BtnSetBootMode_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                EnsureRadarConnected();
                byte val = byte.Parse(((System.Windows.Controls.ComboBoxItem)CmbBootMode.SelectedItem).Tag.ToString()!);
                _radar!.SetWorkModeAfterBoot(val);
                Log($"已设置开机工作模式: {CmbBootMode.Text}");
            }
            catch (Exception ex)
            {
                LogError($"设置开机模式失败: {ex.Message}");
            }
        }

        #endregion

        #region 设备控制

        /// <summary>
        /// 重启设备
        /// </summary>
        private void BtnReboot_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("确定要重启设备吗？", "确认重启", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            try
            {
                EnsureRadarConnected();
                _radar!.RebootDevice(500);
                Log("已发送重启设备命令（延迟500ms）");
            }
            catch (Exception ex)
            {
                LogError($"重启设备失败: {ex.Message}");
            }
        }

        #endregion

        #region 信息查询

        /// <summary>
        /// 查询固件类型
        /// </summary>
        private void BtnQueryFwType_Click(object sender, RoutedEventArgs e)
        {
            ExecuteQuery("固件类型", KeyType.FirmwareType);
        }

        /// <summary>
        /// 查询固件版本
        /// </summary>
        private void BtnQueryFwVer_Click(object sender, RoutedEventArgs e)
        {
            ExecuteQuery("固件版本", KeyType.VersionApp);
        }

        /// <summary>
        /// 查询序列号
        /// </summary>
        private void BtnQuerySN_Click(object sender, RoutedEventArgs e)
        {
            ExecuteQuery("序列号", KeyType.SerialNumber);
        }

        /// <summary>
        /// 查询MAC地址
        /// </summary>
        private void BtnQueryMAC_Click(object sender, RoutedEventArgs e)
        {
            ExecuteQuery("MAC地址", KeyType.MacAddress);
        }

        /// <summary>
        /// 查询工作状态
        /// </summary>
        private void BtnQueryWorkState_Click(object sender, RoutedEventArgs e)
        {
            ExecuteQuery("工作状态", KeyType.CurrentWorkState);
        }

        /// <summary>
        /// 查询核心温度
        /// </summary>
        private void BtnQueryCoreTemp_Click(object sender, RoutedEventArgs e)
        {
            ExecuteQuery("核心温度", KeyType.CoreTemp);
        }

        /// <summary>
        /// 查询状态码
        /// </summary>
        private void BtnQueryStatusCode_Click(object sender, RoutedEventArgs e)
        {
            ExecuteQuery("状态码", KeyType.StatusCode);
        }

        /// <summary>
        /// 执行单个键值的查询命令
        /// </summary>
        /// <param name="queryName">查询名称（用于日志显示）</param>
        /// <param name="key">要查询的参数键</param>
        private void ExecuteQuery(string queryName, KeyType key)
        {
            try
            {
                EnsureRadarConnected();
                //_radar!.QueryInternalInfo(new KeyType[] { key });
                _radar!.QueryInternalInfo([key]);
                Log($"已发送查询命令: {queryName}");
            }
            catch (Exception ex)
            {
                LogError($"查询{queryName}失败: {ex.Message}");
            }
        }

        #endregion

        #region 事件处理

        /// <summary>
        /// 设备发现事件处理：将新设备添加到列表
        /// </summary>
        private void Radar_DeviceDiscovered(object? sender, LidarDeviceInfo device)
        {
            // 跨线程更新UI
            Dispatcher.Invoke(() =>
            {
                // 避免重复添加
                if (_devices.Any(d => d.DeviceInfo.SerialNumberString == device.SerialNumberString))
                    return;

                _devices.Add(new DeviceListItem(device));
                Log($"发现设备: {device.DeviceTypeName} SN={device.SerialNumberString} IP={device.LidarIpString}");
            });
        }

        /// <summary>
        /// ACK响应事件处理：显示命令执行结果，包含Return Code的详细描述
        /// </summary>
        private void Radar_AckResponseReceived(object? sender, AsyncControlResponse response)
        {
            Dispatcher.Invoke(() =>
            {
                if (response.IsSuccess)
                {
                    Log($"ACK: 命令执行成功 (RetCode=0x00 - 执行成功)");
                }
                else
                {
                    // 使用ReturnCodeExtensions获取返回码描述
                    string retCodeDesc = ReturnCodeExtensions.GetDescription(response.RetCode);
                    LogError($"ACK: 命令执行失败 (RetCode={retCodeDesc}, ErrorKey=0x{response.ErrorKey:X4})");
                }
            });
        }

        /// <summary>
        /// 设备推送消息事件处理：显示原始推送数据
        /// </summary>
        private void Radar_PushMessageReceived(object? sender, byte[] data)
        {
            Dispatcher.Invoke(() =>
            {
                Log($"收到推送消息: {data.Length}字节, 命令ID=0x{BitConverter.ToUInt16(data, 8):X4}");
            });
        }

        /// <summary>
        /// 设备状态更新事件处理：显示查询结果，包含Return Code的详细描述
        /// </summary>
        private void Radar_DeviceStatusUpdated(object? sender, InternalInfoResponse info)
        {
            Dispatcher.Invoke(() =>
            {
                if (info.IsSuccess && info.ParamResults != null && info.ParamResults.Length > 0)
                {
                    // 格式化显示查询结果
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine($"[查询成功] 共{info.ParamResults.Length}个参数:");
                    foreach (var kv in info.ParamResults)
                    {
                        sb.AppendLine($"  Key=0x{(ushort)kv.Key:X4} ({kv.Key})");
                        sb.AppendLine($"    原始值: {BitConverter.ToString(kv.Value).Replace("-", " ")}");
                        // 根据Key类型智能解析
                        sb.AppendLine($"    解析值: {ParseKeyValueResult(kv)}");
                    }
                    TxtQueryResult.Text = sb.ToString();
                    Log($"设备状态更新: 查询到{info.ParamResults.Length}个参数");
                }
                else
                {
                    // 查询失败时，显示Return Code的详细描述
                    string retCodeDesc = ReturnCodeExtensions.GetDescription(info.RetCode);
                    LogError($"设备状态查询失败: RetCode={retCodeDesc}");
                    TxtQueryResult.Text = $"查询失败: {retCodeDesc}";
                }
            });
        }

        /// <summary>
        /// 点云数据接收事件处理：显示点云数据统计和摘要
        /// </summary>
        private void Radar_PointCloudDataReceived(object? sender, byte[] data)
        {
            _pclPacketCount++;
            _pclTotalBytes += data.Length;

            // 只在UI线程可见时更新（降低UI刷新频率）
            if (_pclPacketCount % 10 == 0)
            {
                Dispatcher.Invoke(() =>
                {
                    TxtPclStats.Text = $"包数: {_pclPacketCount} | 总字节: {_pclTotalBytes / 1024.0:F1}KB";

                    // 如果启用点云数据显示，解析并显示摘要
                    if (ChkShowPclData.IsChecked == true && _pclPacketCount % 100 == 0)
                    {
                        try
                        {
                            var packet = PointCloudParser.ParsePacket(data);
                            var header = packet.Header;
                            string pclInfo = $"[{DateTime.Now:HH:mm:ss.fff}] " +
                                             $"Ts={header.Timestamp:HH:mm:ss.fff} " +
                                             $"DotNum={header.DotNum} " +
                                             $"UdpCnt={header.UdpCnt} " +
                                             $"DataType={header.DataType}" + Environment.NewLine;

                            // 仅显示前3个点的坐标
                            if (packet.CartesianDataPoints.Count > 0)
                            {
                                int showCount = Math.Min(3, packet.CartesianDataPoints.Count);
                                for (int i = 0; i < showCount; i++)
                                {
                                    var p = packet.CartesianDataPoints[i];
                                    pclInfo += $"  P{i}: X={p.X:F3} Y={p.Y:F3} Z={p.Z:F3} Ref={p.Reflectivity}" + Environment.NewLine;
                                }
                                if (packet.CartesianDataPoints.Count > 3)
                                    pclInfo += $"  ... 共{packet.CartesianDataPoints.Count}个点" + Environment.NewLine;
                            }

                            AppendTextBox(TxtPointCloud, pclInfo, ChkAutoScrollPcl.IsChecked == true);
                        }
                        catch (Exception ex)
                        {
                            AppendTextBox(TxtPointCloud, $"[解析错误] {ex.Message}" + Environment.NewLine, false);
                        }
                    }
                });
            }
        }

        /// <summary>
        /// IMU数据接收事件处理：显示IMU数据摘要
        /// </summary>
        private void Radar_ImuDataReceived(object? sender, byte[] data)
        {
            // IMU数据频率较低，每次都显示
            Dispatcher.Invoke(() =>
            {
                if (ChkShowPclData.IsChecked == true)
                {
                    try
                    {
                        var packet = PointCloudParser.ParsePacket(data);
                        if (packet.ImuDataPoints.Count > 0)
                        {
                            var imu = packet.ImuDataPoints[0];
                            string imuInfo = $"[{DateTime.Now:HH:mm:ss.fff}] IMU: " +
                                             $"Gyro=({imu.GyroX:F3},{imu.GyroY:F3},{imu.GyroZ:F3}) " +
                                             $"Acc=({imu.AccX:F3},{imu.AccY:F3},{imu.AccZ:F3})" + Environment.NewLine;
                            AppendTextBox(TxtPointCloud, imuInfo, ChkAutoScrollPcl.IsChecked == true);
                        }
                    }
                    catch { /* 忽略IMU解析错误 */ }
                }
            });
        }

        #endregion

        #region 辅助方法

        /// <summary>
        /// 确保雷达管理器已初始化且设备已连接
        /// </summary>
        /// <exception cref="InvalidOperationException">雷达未初始化或设备未连接</exception>
        private void EnsureRadarConnected()
        {
            if (_radar == null)
                throw new InvalidOperationException("请先初始化雷达管理器");
            if (!_radar.IsConnected)
                throw new InvalidOperationException("请先连接设备");
        }

        /// <summary>
        /// 通用命令执行方法，统一处理异常和日志
        /// </summary>
        /// <param name="commandName">命令名称（用于日志）</param>
        /// <param name="action">要执行的操作</param>
        private void ExecuteCommand(string commandName, Action<LivoxHapRadar> action)
        {
            try
            {
                EnsureRadarConnected();
                action(_radar!);
                Log($"已发送{commandName}命令");
            }
            catch (Exception ex)
            {
                LogError($"{commandName}失败: {ex.Message}");
            }
        }

        #region 输入验证辅助方法

        /// <summary>
        /// 验证并解析 ushort 类型端口号
        /// </summary>
        /// <param name="text">输入文本</param>
        /// <param name="fieldName">字段名称（用于错误提示）</param>
        /// <param name="result">解析结果</param>
        /// <returns>是否验证通过</returns>
        private bool ValidateAndParseUshort(string text, string fieldName, out ushort result)
        {
            if (!ushort.TryParse(text, out result))
            {
                LogError($"\"{fieldName}\"输入无效：\"{text}\" 不是有效的端口号（范围 0-65535）");
                return false;
            }
            return true;
        }

        /// <summary>
        /// 尝试解析端口号（不输出错误日志，用于非必填场景）
        /// </summary>
        /// <param name="text">输入文本</param>
        /// <param name="fieldName">字段名称（用于错误提示）</param>
        /// <returns>解析结果，失败时返回null</returns>
        private int? TryParsePort(string text, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            if (ushort.TryParse(text.Trim(), out ushort port))
                return port;
            LogError($"\"{fieldName}\"输入无效：\"{text}\" 不是有效的端口号（范围 0-65535）");
            return null;
        }

        /// <summary>
        /// 验证并解析 int 类型整数值
        /// </summary>
        /// <param name="text">输入文本</param>
        /// <param name="fieldName">字段名称（用于错误提示）</param>
        /// <param name="result">解析结果</param>
        /// <returns>是否验证通过</returns>
        private bool ValidateAndParseInt(string text, string fieldName, out int result)
        {
            if (!int.TryParse(text, out result))
            {
                LogError($"\"{fieldName}\"输入无效：\"{text}\" 不是有效的整数");
                return false;
            }
            return true;
        }

        /// <summary>
        /// 验证并解析 float 类型浮点数值
        /// </summary>
        /// <param name="text">输入文本</param>
        /// <param name="fieldName">字段名称（用于错误提示）</param>
        /// <param name="result">解析结果</param>
        /// <returns>是否验证通过</returns>
        private bool ValidateAndParseFloat(string text, string fieldName, out float result)
        {
            if (!float.TryParse(text, out result))
            {
                LogError($"\"{fieldName}\"输入无效：\"{text}\" 不是有效的数值");
                return false;
            }
            return true;
        }

        /// <summary>
        /// 验证并解析 uint 类型无符号整数值
        /// </summary>
        /// <param name="text">输入文本</param>
        /// <param name="fieldName">字段名称（用于错误提示）</param>
        /// <param name="result">解析结果</param>
        /// <returns>是否验证通过</returns>
        private bool ValidateAndParseUInt(string text, string fieldName, out uint result)
        {
            if (!uint.TryParse(text, out result))
            {
                LogError($"\"{fieldName}\"输入无效：\"{text}\" 不是有效的正整数");
                return false;
            }
            return true;
        }

        /// <summary>
        /// 验证IP地址格式是否合法
        /// </summary>
        /// <param name="ipText">IP地址文本</param>
        /// <param name="fieldName">字段名称（用于错误提示）</param>
        /// <returns>是否验证通过</returns>
        private bool ValidateIpAddress(string ipText, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(ipText))
            {
                LogError($"\"{fieldName}\"不能为空");
                return false;
            }

            //if (!IPAddress.TryParse(ipText, out IPAddress? addr))
            if (!IPAddress.TryParse(ipText, out _))
            {
                LogError($"\"{fieldName}\"格式无效：\"{ipText}\" 不是有效的IP地址");
                return false;
            }
            return true;
        }

        #endregion

        #region 配置回填方法

        /// <summary>
        /// 从雷达管理器的当前配置中读取端口值，回填到网络配置面板的输入框
        /// 确保UI显示与实际配置一致，避免用户误以为使用了默认值
        /// </summary>
        private void FillNetworkConfigFromRadar()
        {
            if (_radar?.Config?.HapConfig?.HostNetInfo == null || _radar.Config.HapConfig.HostNetInfo.Count == 0)
                return;

            var hostNetInfo = _radar.Config.HapConfig.HostNetInfo[0];
            TxtCfgHostIp.Text = hostNetInfo.HostIp;
            TxtCfgCmdPort.Text = hostNetInfo.CmdDataPort.ToString();
            TxtCfgPointPort.Text = hostNetInfo.PointDataPort.ToString();
            TxtCfgImuPort.Text = hostNetInfo.ImuDataPort.ToString();
            TxtCfgPushPort.Text = hostNetInfo.PushMsgPort.ToString();
        }

        #endregion

        /// <summary>
        /// 智能解析KeyValueResult，根据Key类型返回可读的值
        /// </summary>
        /// <param name="kv">键值对查询结果</param>
        /// <returns>格式化的值字符串</returns>
        private static string ParseKeyValueResult(KeyValueResult kv)
        {
            try
            {
                return kv.Key switch
                {
                    KeyType.SerialNumber => kv.AsAsciiString(),
                    KeyType.ProductInfo => kv.AsAsciiString(),
                    KeyType.VersionApp => FormatVersion(kv.Value),
                    KeyType.VersionLoader => FormatVersion(kv.Value),
                    KeyType.VersionHardware => FormatVersion(kv.Value),
                    KeyType.MacAddress => FormatMacAddress(kv.Value),
                    KeyType.CurrentWorkState => $"0x{kv.AsByte():X2} ({(DeviceWorkState)kv.AsByte()})",
                    KeyType.CoreTemp => $"{kv.AsFloat():F1}°C",
                    KeyType.StatusCode => $"0x{kv.AsUInt32():X8}",
                    KeyType.FirmwareType => $"0x{kv.AsByte():X2} ({(FirmwareType)kv.AsByte()})",
                    KeyType.PowerUpCount => kv.AsUInt32().ToString(),
                    KeyType.LidarIpConfig => kv.AsIpAddress(),
                    KeyType.StateInfoHostIpConfig => kv.AsIpAddress(),
                    KeyType.PointCloudHostIpConfig => kv.AsIpAddress(),
                    KeyType.ImuHostIpConfig => kv.AsIpAddress(),
                    _ => BitConverter.ToString(kv.Value).Replace("-", " ")
                };
            }
            catch
            {
                return BitConverter.ToString(kv.Value).Replace("-", " ");
            }
        }

        /// <summary>
        /// 格式化版本号（4字节 → A.B.C.D）
        /// </summary>
        /// <param name="value">原始字节</param>
        /// <returns>格式化版本号字符串</returns>
        private static string FormatVersion(byte[] value)
        {
            if (value == null || value.Length < 4) return "N/A";
            return $"{value[0]}.{value[1]}.{value[2]}.{value[3]}";
        }

        /// <summary>
        /// 格式化MAC地址（6字节 → XX:XX:XX:XX:XX:XX）
        /// </summary>
        /// <param name="value">原始字节</param>
        /// <returns>格式化MAC地址字符串</returns>
        private static string FormatMacAddress(byte[] value)
        {
            if (value == null || value.Length < 6) return "N/A";
            return string.Join(":", value.Take(6).Select(b => b.ToString("X2")));
        }

        /// <summary>
        /// 追加文本到TextBox（线程安全），并控制最大长度避免内存溢出
        /// </summary>
        /// <param name="textBox">目标文本框</param>
        /// <param name="text">要追加的文本</param>
        /// <param name="autoScroll">是否自动滚动到底部</param>
        private static void AppendTextBox(System.Windows.Controls.TextBox textBox, string text, bool autoScroll)
        {
            // 限制最大文本长度，超出时截断旧内容
            const int maxLength = 50000;
            if (textBox.Text.Length > maxLength)
                textBox.Text = textBox.Text[^30000..];

            textBox.AppendText(text);
            if (autoScroll)
                textBox.ScrollToEnd();
        }

        /// <summary>
        /// 记录普通消息到日志
        /// </summary>
        /// <param name="message">消息内容</param>
        private void Log(string message)
        {
            string line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}";
            AppendTextBox(TxtLog, line, true);
        }

        /// <summary>
        /// 记录错误消息到日志（带[ERROR]前缀）
        /// </summary>
        /// <param name="message">错误消息内容</param>
        private void LogError(string message)
        {
            string line = $"[{DateTime.Now:HH:mm:ss.fff}] [ERROR] {message}{Environment.NewLine}";
            AppendTextBox(TxtLog, line, true);
        }

        /// <summary>
        /// 清空日志文本框
        /// </summary>
        private void BtnClearLog_Click(object sender, RoutedEventArgs e)
        {
            TxtLog.Clear();
        }

        /// <summary>
        /// 清空点云数据显示
        /// </summary>
        private void BtnClearPcl_Click(object sender, RoutedEventArgs e)
        {
            TxtPointCloud.Clear();
            _pclPacketCount = 0;
            _pclTotalBytes = 0;
            TxtPclStats.Text = "";
        }

        #endregion

        #region 窗口关闭

        /// <summary>
        /// 窗口关闭时释放雷达资源
        /// </summary>
        protected override void OnClosed(EventArgs e)
        {
            _radar?.Dispose();
            base.OnClosed(e);
        }

        #endregion
    }

    #region 设备列表项模型

#if NET45_OR_GREATER
    /// <summary>
    /// 设备列表项模型，封装 LidarDeviceInfo 并提供 UI 显示所需的属性
    /// 实现 INotifyPropertyChanged 接口，确保连接状态变更时UI指示灯自动刷新
    /// </summary>
    public class DeviceListItem : INotifyPropertyChanged
    {
        /// <summary>设备信息实体</summary>
        public LidarDeviceInfo DeviceInfo { get; }

        /// <summary>是否已连接</summary>
        private bool _isConnected;

        /// <summary>
        /// 构造设备列表项
        /// </summary>
        /// <param name="deviceInfo">设备信息</param>
        public DeviceListItem(LidarDeviceInfo deviceInfo)
        {
            DeviceInfo = deviceInfo;
            _isConnected = deviceInfo.IsConnected;
        }
#elif NET9_0_OR_GREATER
    /// <summary>
    /// 设备列表项模型，封装 LidarDeviceInfo 并提供 UI 显示所需的属性
    /// 实现 INotifyPropertyChanged 接口，确保连接状态变更时UI指示灯自动刷新
    /// </summary>
    /// <remarks>
    /// 构造设备列表项
    /// </remarks>
    /// <param name="deviceInfo">设备信息</param>
    public class DeviceListItem(LidarDeviceInfo deviceInfo) : INotifyPropertyChanged
    {
        /// <summary>设备信息实体</summary>
        public LidarDeviceInfo DeviceInfo { get; } = deviceInfo;

        /// <summary>是否已连接</summary>
        private bool _isConnected = deviceInfo.IsConnected;
#endif

        /// <summary>
        /// 是否已连接（设置时触发属性变更通知，使UI绑定自动更新）
        /// </summary>
        public bool IsConnected
        {
            get { return _isConnected; }
            set
            {
                if (_isConnected != value)
                {
                    _isConnected = value;
                    OnPropertyChanged(nameof(IsConnected));
                    OnPropertyChanged(nameof(ConnStatusColor));
                }
            }
        }

        /// <summary>显示名称（格式：型号 SN IP）</summary>
        public string DisplayName => $"{DeviceInfo.DeviceTypeName} | {DeviceInfo.SerialNumberString} | {DeviceInfo.LidarIpString}";

        /// <summary>连接状态指示灯颜色（根据IsConnected自动切换）</summary>
        public Brush ConnStatusColor => IsConnected ? Brushes.LimeGreen : Brushes.Gray;

        ///// <summary>
        ///// 构造设备列表项
        ///// </summary>
        ///// <param name="deviceInfo">设备信息</param>
        //public DeviceListItem(LidarDeviceInfo deviceInfo)
        //{
        //    DeviceInfo = deviceInfo;
        //    _isConnected = deviceInfo.IsConnected;
        //}

        /// <summary>
        /// 手动通知属性变更（用于非setter路径的状态更新）
        /// </summary>
        public void NotifyStateChanged()
        {
            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(ConnStatusColor));
        }

        /// <summary>
        /// 属性变更事件
        /// </summary>
        public event PropertyChangedEventHandler
            //.net 9框架下使返回对象可为空
#if NET9_0_OR_GREATER
            ?
#endif
            PropertyChanged;

        /// <summary>
        /// 触发属性变更通知
        /// </summary>
        /// <param name="propertyName">变更的属性名称</param>
        protected void OnPropertyChanged([CallerMemberName] string
            //.net 9框架下使返回对象可为空
#if NET9_0_OR_GREATER
            ?
#endif
  propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    #endregion
}
