using Ink_Canvas.Helpers;
using Ink_Canvas.Properties;
using Ink_Canvas.Windows.SettingsViews.Helpers;
using OSVersionExtension;
using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;

namespace Ink_Canvas.Windows.FeedbackPages
{
    /// <summary>
    /// 反馈窗口，提供用户反馈和问题报告功能。
    /// 收集系统环境信息并生成GitHub Issue的Markdown模板。
    /// </summary>
    public partial class FeedbackWindow : Window
    {
        private string _appVersion = "";
        private string _updateChannel = "";
        private string _osVersion = "";
        private string _netVersion = "";
        private string _deviceId = "";
        private string _pptLinkageSettings = "";
        private string _inkRecognitionSettings = "";

        private FeedbackPage1 _page1;
        private FeedbackPage2 _page2;
        private FeedbackPage3 _page3;

        public FeedbackWindow()
        {
            InitializeComponent();
            _page1 = new FeedbackPage1();
            _page2 = new FeedbackPage2();
            _page3 = new FeedbackPage3();

            _page3.BtnOpenGitHubIssueClick += BtnOpenGitHubIssue_Click;
            _page3.CardCopyIssueUrlClick += CardCopyIssueUrl_Click;
            _page3.BtnCopyMarkdownClick += BtnCopyMarkdown_Click;
            _page3.BtnUploadPastebinClick += BtnUploadPastebin_Click;
            _page3.BtnCopyPasteUrlClick += BtnCopyPasteUrl_Click;

            ContentFrame.Navigated += ContentFrame_Navigated;
            LoadInformation();
            ContentFrame.Navigate(_page1);
        }

        private void ContentFrame_Navigated(object sender, System.Windows.Navigation.NavigationEventArgs e)
        {
            UpdateButtonVisibility();
        }

        /// <summary>
        /// 加载系统环境信息，包括软件版本、系统信息、设备信息等。
        /// </summary>
        private void LoadInformation()
        {
            try
            {
                var assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version;
                _appVersion = $"v{assemblyVersion}";
            }
            catch (Exception ex)
            {
                _appVersion = Properties.AboutStrings.Unknown;
                Debug.WriteLine($"获取软件版本失败: {ex.Message}");
            }

            try
            {
                var settings = SettingsManager.Settings;
                if (settings?.Startup != null)
                {
                    _updateChannel = settings.Startup.UpdateChannel.ToString();
                }
            }
            catch (Exception ex)
            {
                _updateChannel = Properties.AboutStrings.Unknown;
                Debug.WriteLine($"获取更新通道失败: {ex.Message}");
            }

            try
            {
                _osVersion = $"{OSVersion.GetOperatingSystem()} {OSVersion.GetOSVersion().Version}";
            }
            catch (Exception ex)
            {
                _osVersion = Properties.AboutStrings.Unknown;
                Debug.WriteLine($"获取系统版本失败: {ex.Message}");
            }

            try
            {
                _netVersion = RuntimeInformation.FrameworkDescription;
            }
            catch (Exception ex)
            {
                _netVersion = Properties.AboutStrings.Unknown;
                Debug.WriteLine($"获取.NET版本失败: {ex.Message}");
            }

            try
            {
                _deviceId = DeviceIdentifier.GetDeviceId();
            }
            catch (Exception ex)
            {
                _deviceId = Properties.FeedbackStrings.Feedback_FetchFailed;
                Debug.WriteLine($"获取设备ID失败: {ex.Message}");
            }

            try
            {
                var settings = SettingsManager.Settings;
                if (settings?.PowerPointSettings != null)
                {
                    _pptLinkageSettings = string.Format(Properties.FeedbackStrings.Feedback_PptLinkageLineFormat, settings.PowerPointSettings.PowerPointSupport);
                    _pptLinkageSettings += string.Format(Properties.FeedbackStrings.Feedback_WpsSupportLineFormat, settings.PowerPointSettings.IsSupportWPS);
                    _pptLinkageSettings += string.Format(Properties.FeedbackStrings.Feedback_MsoSupportLineFormat, settings.PowerPointSettings.PowerPointSupport);
                }
                else
                {
                    _pptLinkageSettings = Properties.FeedbackStrings.Feedback_NoPptLinkage;
                }
            }
            catch (Exception ex)
            {
                _pptLinkageSettings = Properties.FeedbackStrings.Feedback_PptLinkageFailed;
                Debug.WriteLine($"获取PPT联动设置失败: {ex.Message}");
            }

            try
            {
                var settings = SettingsManager.Settings;
                if (settings?.InkToShape != null)
                {
                    _inkRecognitionSettings = string.Format(Properties.FeedbackStrings.Feedback_InkRecognitionLineFormat, settings.InkToShape.IsInkToShapeEnabled);

                    var engineMode = ShapeRecognitionRouter.FromSettingsInt(settings.InkToShape.ShapeRecognitionEngine);
                    bool useWinRT = ShapeRecognitionRouter.ResolveUseWinRt(engineMode);
                    _inkRecognitionSettings += string.Format(Properties.FeedbackStrings.Feedback_EngineLineFormat, useWinRT ? "WinRT" : "IACore");
                }
                else
                {
                    _inkRecognitionSettings = Properties.FeedbackStrings.Feedback_NoInkRecognition;
                }
            }
            catch (Exception ex)
            {
                _inkRecognitionSettings = Properties.FeedbackStrings.Feedback_InkRecognitionFailed;
                Debug.WriteLine($"获取墨迹识别设置失败: {ex.Message}");
            }
        }

        private void ButtonCancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ButtonBack_Click(object sender, RoutedEventArgs e)
        {
            if (ContentFrame.BackStackDepth > 0)
            {
                ContentFrame.GoBack();
                UpdateButtonVisibility();
            }
        }

        private void ButtonNext_Click(object sender, RoutedEventArgs e)
        {
            UpdatePage2Info();
            ContentFrame.Navigate(_page2);
            UpdateButtonVisibility();
        }

        private void ButtonConfirm_Click(object sender, RoutedEventArgs e)
        {
            GenerateMarkdownTemplate();
            ContentFrame.Navigate(_page3);
            UpdateButtonVisibility();
        }

        /// <summary>
        /// 根据当前页面更新按钮的可见性。
        /// </summary>
        private void UpdateButtonVisibility()
        {
            if (ContentFrame.Content == _page1)
            {
                ButtonCancel.Visibility = Visibility.Visible;
                ButtonNext.Visibility = Visibility.Visible;
                ButtonBack.Visibility = Visibility.Collapsed;
                ButtonConfirm.Visibility = Visibility.Collapsed;
            }
            else if (ContentFrame.Content == _page2)
            {
                ButtonCancel.Visibility = Visibility.Collapsed;
                ButtonNext.Visibility = Visibility.Collapsed;
                ButtonBack.Visibility = Visibility.Visible;
                ButtonConfirm.Visibility = Visibility.Visible;
            }
            else if (ContentFrame.Content == _page3)
            {
                ButtonCancel.Visibility = Visibility.Collapsed;
                ButtonNext.Visibility = Visibility.Collapsed;
                ButtonBack.Visibility = Visibility.Visible;
                ButtonConfirm.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// 更新第二页的显示信息。
        /// </summary>
        private void UpdatePage2Info()
        {
            try
            {
                string versionInfo = "";
                string systemInfo = "";

                if (_page1.CheckAppVersion.IsChecked == true)
                {
                    versionInfo += _appVersion;
                }
                if (_page1.CheckUpdateChannel.IsChecked == true)
                {
                    if (!string.IsNullOrEmpty(versionInfo)) versionInfo += " ";
                    versionInfo += $"({_updateChannel})";
                }

                if (_page1.CheckOSVersion.IsChecked == true)
                {
                    systemInfo += _osVersion;
                }
                if (_page1.CheckNetVersion.IsChecked == true)
                {
                    if (!string.IsNullOrEmpty(systemInfo)) systemInfo += " | ";
                    systemInfo += _netVersion;
                }

                _page2.TextAppVersionInfo.Text = versionInfo;
                _page2.TextSystemInfo.Text = systemInfo;

                if (_page1.CheckDeviceId.IsChecked == true)
                {
                    _page2.TextDeviceInfo.Text = string.Format(Properties.FeedbackStrings.Feedback_DeviceIdFormat, _deviceId);
                }
                else
                {
                    _page2.TextDeviceInfo.Text = string.Format(Properties.FeedbackStrings.Feedback_DeviceIdFormat, FeedbackStrings.Page2_Exclude);
                }

                if (_page1.CheckPPTLinkage.IsChecked == true || _page1.CheckInkRecognition.IsChecked == true)
                {
                    _page2.CardConfiguration.Visibility = Visibility.Visible;
                    _page2.TextConfigurationInfo.Text = "";
                    if (_page1.CheckPPTLinkage.IsChecked == true)
                    {
                        _page2.TextConfigurationInfo.Text += string.Format(Properties.FeedbackStrings.Feedback_PptLinkageBlockFormat, _pptLinkageSettings.TrimEnd('\n', '\r'));
                    }
                    if (_page1.CheckInkRecognition.IsChecked == true)
                    {
                        if (_page1.CheckPPTLinkage.IsChecked == true) _page2.TextConfigurationInfo.Text += "\n";
                        _page2.TextConfigurationInfo.Text += string.Format(Properties.FeedbackStrings.Feedback_InkRecognitionBlockFormat, _inkRecognitionSettings.TrimEnd('\n', '\r'));
                    }
                }
                else
                {
                    _page2.CardConfiguration.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"更新第二页信息失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 生成Markdown格式的反馈信息模板。
        /// </summary>
        private void GenerateMarkdownTemplate()
        {
            string template = Properties.FeedbackStrings.Feedback_SectionEnvironment;

            if (_page1.CheckAppVersion.IsChecked == true)
            {
                template += string.Format(Properties.FeedbackStrings.Feedback_SoftwareVersionFormat, _appVersion);
            }
            if (_page1.CheckUpdateChannel.IsChecked == true)
            {
                template += string.Format(Properties.FeedbackStrings.Feedback_UpdateChannelFormat, _updateChannel);
            }
            if (_page1.CheckOSVersion.IsChecked == true)
            {
                template += string.Format(Properties.FeedbackStrings.Feedback_OsVersionFormat, _osVersion);
            }
            if (_page1.CheckNetVersion.IsChecked == true)
            {
                template += string.Format(Properties.FeedbackStrings.Feedback_NetVersionFormat, _netVersion);
            }

            template += Properties.FeedbackStrings.Feedback_SectionDeviceInfo;
            if (_page1.CheckDeviceId.IsChecked == true)
            {
                template += string.Format(Properties.FeedbackStrings.Feedback_DeviceIdLineFormat, _deviceId);
            }

            if (_page1.CheckPPTLinkage.IsChecked == true || _page1.CheckInkRecognition.IsChecked == true)
            {
                template += Properties.FeedbackStrings.Feedback_SectionSoftwareConfig;
                if (_page1.CheckPPTLinkage.IsChecked == true)
                {
                    template += Properties.FeedbackStrings.Feedback_SectionPptLinkage;
                    template += _pptLinkageSettings.TrimEnd('\n', '\r');
                }
                if (_page1.CheckInkRecognition.IsChecked == true)
                {
                    if (_page1.CheckPPTLinkage.IsChecked == true) template += "\n";
                    template += Properties.FeedbackStrings.Feedback_SectionInkRecognition;
                    template += _inkRecognitionSettings.TrimEnd('\n', '\r');
                }
            }

            _page3.TextBoxMarkdownTemplate.Text = template;
        }

        /// <summary>
        /// 构建反馈信息元组。
        /// </summary>
        private (string versionInfo, string systemInfo, string extraInfo) BuildFeedbackInfo()
        {
            string versionInfo = "";
            string systemInfo = "";
            string extraInfo = "";

            if (_page1.CheckAppVersion.IsChecked == true)
            {
                versionInfo += _appVersion;
            }
            if (_page1.CheckUpdateChannel.IsChecked == true)
            {
                if (!string.IsNullOrEmpty(versionInfo)) versionInfo += " ";
                versionInfo += $"({_updateChannel})";
            }

            if (_page1.CheckOSVersion.IsChecked == true)
            {
                systemInfo += _osVersion;
            }
            if (_page1.CheckNetVersion.IsChecked == true)
            {
                if (!string.IsNullOrEmpty(systemInfo)) systemInfo += " | ";
                systemInfo += _netVersion;
            }

            if (_page1.CheckDeviceId.IsChecked == true)
            {
                extraInfo += string.Format(Properties.FeedbackStrings.Feedback_DeviceIdLineFormat2, _deviceId);
            }

            if (_page1.CheckPPTLinkage.IsChecked == true)
            {
                extraInfo += Properties.FeedbackStrings.Feedback_PptLinkageHeader;
                extraInfo += _pptLinkageSettings.TrimEnd('\n', '\r');
            }

            if (_page1.CheckInkRecognition.IsChecked == true)
            {
                extraInfo += Properties.FeedbackStrings.Feedback_InkRecognitionHeader;
                extraInfo += _inkRecognitionSettings.TrimEnd('\n', '\r');
            }

            return (versionInfo, systemInfo, extraInfo);
        }

        /// <summary>
        /// 构建GitHub Issue创建页面的URL。
        /// </summary>
        private string BuildGitHubIssueUrl()
        {
            var (versionInfo, systemInfo, extraInfo) = BuildFeedbackInfo();

            string url = "https://github.com/InkCanvasForClass/community/issues/new?template=01-bug_report.yml";

            if (!string.IsNullOrEmpty(versionInfo))
            {
                url += $"&version={Uri.EscapeDataString(versionInfo)}";
            }

            if (!string.IsNullOrEmpty(systemInfo))
            {
                url += $"&os={Uri.EscapeDataString(systemInfo)}";
            }

            if (!string.IsNullOrEmpty(extraInfo))
            {
                url += $"&extra={Uri.EscapeDataString(extraInfo)}";
            }

            return url;
        }

        private void BtnOpenGitHubIssue_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string url = BuildGitHubIssueUrl();

                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });

                Close();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"打开反馈链接失败: {ex.Message}");
            }
        }

        private void CardCopyIssueUrl_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string url = BuildGitHubIssueUrl();
                Clipboard.SetText(url);
                _page3.CardCopyIssueUrl.Header = FeedbackStrings.Page3_Copied;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"复制反馈链接失败: {ex.Message}");
            }
        }

        private void BtnCopyMarkdown_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(_page3.MarkdownTemplate);
                _page3.BtnCopyMarkdown.Content = FeedbackStrings.Page3_Copied;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"复制Markdown模板失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 构建脱敏后的反馈 JSON 字符串。
        /// 设备 ID 保留原样，WebDAV/token/密码等敏感字段被移除。
        /// </summary>
        private string BuildSanitizedFeedbackJson()
        {
            return FeedbackSanitizer.BuildSanitizedSettingsJson(SettingsManager.Settings, _deviceId);
        }

        /// <summary>
        /// 上传脱敏后的数据到 pastebin。从 Page3 读取服务器地址，直接 POST JSON。
        /// </summary>
        private async void BtnUploadPastebin_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string serverUrl = _page3.PastebinUrl;

                if (string.IsNullOrWhiteSpace(serverUrl))
                {
                    _page3.BtnUploadPastebin.Content = FeedbackStrings.Page3_PastebinNotConfigured;
                    return;
                }

                _page3.BtnUploadPastebin.IsEnabled = false;
                _page3.BtnUploadPastebin.Content = FeedbackStrings.Page3_Uploading;

                string sanitizedJson = BuildSanitizedFeedbackJson();
                var (pasteUrl, error) = await MicroBinClient.UploadRawAsync(serverUrl, sanitizedJson);

                if (!string.IsNullOrEmpty(pasteUrl))
                {
                    _page3.BtnUploadPastebin.Content = FeedbackStrings.Page3_UploadSuccess;
                    _page3.CardPasteResult.Header = pasteUrl;
                    _page3.CardPasteResult.Visibility = Visibility.Visible;
                }
                else
                {
                    _page3.BtnUploadPastebin.Content = FeedbackStrings.Page3_UploadFailed;
                    LogHelper.WriteLogToFile($"Pastebin 上传失败: {error}", LogHelper.LogType.Error);
                }
            }
            catch (Exception ex)
            {
                _page3.BtnUploadPastebin.Content = FeedbackStrings.Page3_UploadFailed;
                LogHelper.WriteLogToFile($"Pastebin 上传异常: {ex}", LogHelper.LogType.Error);
            }
            finally
            {
                _page3.BtnUploadPastebin.IsEnabled = true;
            }
        }

        private void BtnCopyPasteUrl_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string url = _page3.CardPasteResult.Header?.ToString();
                if (!string.IsNullOrEmpty(url))
                {
                    Clipboard.SetText(url);
                    _page3.BtnCopyPasteUrl.Content = FeedbackStrings.Page3_Copied;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"复制 Pastebin 链接失败: {ex.Message}");
            }
        }
    }
}
