using Ink_Canvas.Helpers;
using Ink_Canvas.Properties;
using Ink_Canvas.Windows.SettingsViews.Helpers;
using OSVersionExtension;
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Ink_Canvas.Windows.SettingsViews.Pages
{
    public partial class ScreenTouchPage : iNKORE.UI.WPF.Modern.Controls.Page
    {
        private bool _isLoaded = false;

        // Auto-calibrate state
        private int _calibrateStep = 0;
        private double _nibTouchWidth = 0;
        private double _fingerTouchWidth = 0;
        private double _palmTouchWidth = 0;

        public ScreenTouchPage()
        {
            InitializeComponent();
            Loaded += ScreenTouchPage_Loaded;
            Unloaded += ScreenTouchPage_Unloaded;
        }

        private void ScreenTouchPage_Loaded(object sender, RoutedEventArgs e)
        {
            _isLoaded = false;
            LoadSettings();
            _isLoaded = true;
            SliderTouchHelper.AddTouchSupportToAllSliders(this);
        }

        private void ScreenTouchPage_Unloaded(object sender, RoutedEventArgs e)
        {
            _isLoaded = false;
        }

        private MainWindow GetMainWindow() => Application.Current.MainWindow as MainWindow;

        private void LoadSettings()
        {
            var settings = SettingsManager.Settings;
            if (settings?.Advanced == null) return;

            try
            {
                // Screen settings
                CardMultiScreenSupport.IsOn = settings.Advanced.EnableMultiScreenSupport;
                CardFollowMouseScreen.IsOn = settings.Advanced.FollowMouseForScreenSelection;
                CardAvoidFullScreen.IsOn = settings.Advanced.IsEnableAvoidFullScreenHelper;
                CardIsSpecialScreen.IsOn = settings.Advanced.IsSpecialScreen;

                // Touch multiplier
                TouchMultiplierSlider.Value = settings.Advanced.TouchMultiplier;
                ToggleSwitchEraserBindTouchMultiplier.IsOn = settings.Advanced.EraserBindTouchMultiplier;

                // Bounds width
                NibModeBoundsWidthSlider.Value = settings.Advanced.NibModeBoundsWidth;
                FingerModeBoundsWidthSlider.Value = settings.Advanced.FingerModeBoundsWidth;
                ToggleSwitchIsQuadIR.IsOn = settings.Advanced.IsQuadIR;

                // Auto-calibrate defaults
                CardTouchMultiplier.IsExpanded = settings.Advanced.IsSpecialScreen;

                // Pressure & eraser
                if (settings.Canvas != null)
                {
                    CardEnablePressureTouchMode.IsOn = settings.Canvas.EnablePressureTouchMode;
                    CardDisablePressure.IsOn = settings.Canvas.DisablePressure;
                }

                // Experimental
                CardForceFullScreen.IsOn = settings.Advanced.IsEnableForceFullScreen;
                CardDPIChangeDetection.IsOn = settings.Advanced.IsEnableDPIChangeDetection;
                CardResolutionChangeDetection.IsOn = settings.Advanced.IsEnableResolutionChangeDetection;
                CardFullScreenHelper.IsOn = settings.Advanced.IsEnableFullScreenHelper;
                CardEdgeGestureUtil.IsOn = settings.Advanced.IsEnableEdgeGestureUtil;

                UpdateAllSliderTexts();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"加载触控设置时出错: {ex.Message}");
            }
        }

        private void UpdateAllSliderTexts()
        {
            UpdateSliderText(TouchMultiplierSlider, TouchMultiplierText, "{0:F2}");
            UpdateSliderText(NibModeBoundsWidthSlider, NibModeBoundsWidthText, "{0:0}");
            UpdateSliderText(FingerModeBoundsWidthSlider, FingerModeBoundsWidthText, "{0:0}");
        }

        private void UpdateSliderText(Slider slider, TextBlock textBlock, string format)
        {
            if (slider == null || textBlock == null) return;
            textBlock.Text = string.Format(format, slider.Value);
        }

        #region Screen Settings

        private void ToggleSwitchMultiScreenSupport_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            try
            {
                SettingsManager.Settings.Advanced.EnableMultiScreenSupport = CardMultiScreenSupport.IsOn;
                SettingsManager.SaveSettingsToFile();

                if (Application.Current.MainWindow is MainWindow mainWindow)
                {
                    mainWindow.ApplyMultiScreenSettings();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"设置多屏支持时出错: {ex.Message}");
            }
        }

        private void ToggleSwitchFollowMouseScreen_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            try
            {
                SettingsManager.Settings.Advanced.FollowMouseForScreenSelection = CardFollowMouseScreen.IsOn;
                SettingsManager.SaveSettingsToFile();

                if (Application.Current.MainWindow is MainWindow mainWindow)
                {
                    mainWindow.ApplyMultiScreenSettings();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"设置自动跟随鼠标选择显示屏时出错: {ex.Message}");
            }
        }

        private void ToggleSwitchAvoidFullScreen_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            try
            {
                SettingsManager.Settings.Advanced.IsEnableAvoidFullScreenHelper = CardAvoidFullScreen.IsOn;
                SettingsManager.SaveSettingsToFile();

                var window = Application.Current.MainWindow;
                if (window != null)
                {
                    if (CardAvoidFullScreen.IsOn)
                        AvoidFullScreenHelper.StartAvoidFullScreen(window);
                    else
                        AvoidFullScreenHelper.StopAvoidFullScreen(window);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"设置避免全屏时出错: {ex.Message}");
            }
        }

        private void ToggleSwitchIsSpecialScreen_OnToggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Advanced.IsSpecialScreen = CardIsSpecialScreen.IsOn;
            CardTouchMultiplier.IsExpanded = CardIsSpecialScreen.IsOn;
            SettingsManager.SaveSettingsToFile();
        }

        #endregion

        #region Touch Multiplier & Bounds

        private void TouchMultiplierSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateSliderText(TouchMultiplierSlider, TouchMultiplierText, "{0:F2}");
            if (!_isLoaded) return;
            var val = Math.Round(TouchMultiplierSlider.Value, 2);
            TouchMultiplierSlider.Value = val;
            SettingsManager.Settings.Advanced.TouchMultiplier = val;
            SettingsManager.SaveSettingsToFile();
        }

        private void BorderCalculateMultiplier_TouchDown(object sender, TouchEventArgs e)
        {
            var args = e.GetTouchPoint(null).Bounds;
            double value;
            if (!SettingsManager.Settings.Advanced.IsQuadIR) value = args.Width;
            else value = Math.Sqrt(args.Width * args.Height);

            TextBlockShowRawValue.Text = value.ToString();
            TextBlockShowCalculatedMultiplier.Text = (5 / (value * 1.1)).ToString();
        }

        private void ToggleSwitchEraserBindTouchMultiplier_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Advanced.EraserBindTouchMultiplier = ToggleSwitchEraserBindTouchMultiplier.IsOn;
            SettingsManager.SaveSettingsToFile();
        }

        private void NibModeBoundsWidthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateSliderText(NibModeBoundsWidthSlider, NibModeBoundsWidthText, "{0:0}");
            if (!_isLoaded) return;
            SettingsManager.Settings.Advanced.NibModeBoundsWidth = (int)e.NewValue;
            SettingsActionHub.OnNibModeBoundsWidthChanged();
            SettingsManager.SaveSettingsToFile();
        }

        private void FingerModeBoundsWidthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateSliderText(FingerModeBoundsWidthSlider, FingerModeBoundsWidthText, "{0:0}");
            if (!_isLoaded) return;
            SettingsManager.Settings.Advanced.FingerModeBoundsWidth = (int)e.NewValue;
            SettingsActionHub.OnFingerModeBoundsWidthChanged();
            SettingsManager.SaveSettingsToFile();
        }

        private void ToggleSwitchIsQuadIR_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Advanced.IsQuadIR = ToggleSwitchIsQuadIR.IsOn;
            SettingsManager.SaveSettingsToFile();
        }

        #endregion

        #region Auto Calibrate

        private void BtnStartCalibrate_Click(object sender, RoutedEventArgs e)
        {
            _calibrateStep = 1;
            _nibTouchWidth = 0;
            _fingerTouchWidth = 0;
            _palmTouchWidth = 0;

            BtnStartCalibrate.IsEnabled = false;
            BorderCalibrate.IsEnabled = true;
            TextCalibrateHint.Text = "请用 笔尖 点击此处";
            TextNibCalibrated.Text = "笔尖值: 等待校准...";
            TextFingerCalibrated.Text = "手指值: 未校准";
            TextPalmCalibrated.Text = "手掌值: 未校准";
            TextCalibrateResult.Text = "";
        }

        private void BorderCalibrate_TouchDown(object sender, TouchEventArgs e)
        {
            var args = e.GetTouchPoint(null).Bounds;
            double touchWidth;
            if (!SettingsManager.Settings.Advanced.IsQuadIR)
                touchWidth = args.Width;
            else
                touchWidth = Math.Sqrt(args.Width * args.Height);

            switch (_calibrateStep)
            {
                case 1:
                    _nibTouchWidth = touchWidth;
                    TextNibCalibrated.Text = $"笔尖值: {touchWidth:F2}";
                    TextCalibrateHint.Text = "请用 手指 点击此处";
                    _calibrateStep = 2;
                    break;

                case 2:
                    _fingerTouchWidth = touchWidth;
                    TextFingerCalibrated.Text = $"手指值: {touchWidth:F2}";
                    TextCalibrateHint.Text = "请用 手掌 点击此处（模拟误触）";
                    _calibrateStep = 3;
                    break;

                case 3:
                    _palmTouchWidth = touchWidth;
                    TextPalmCalibrated.Text = $"手掌值: {touchWidth:F2}";
                    ApplyCalibratedSettings();
                    _calibrateStep = 0;
                    BtnStartCalibrate.IsEnabled = true;
                    BorderCalibrate.IsEnabled = false;
                    TextCalibrateHint.Text = "校准完成！点击按钮重新校准";
                    break;
            }

            e.Handled = true;
        }

        private void ApplyCalibratedSettings()
        {
            if (!_isLoaded) return;

            double nibThreshold = _nibTouchWidth * 1.5;
            nibThreshold = Math.Max(1, Math.Min(50, nibThreshold));
            SettingsManager.Settings.Advanced.NibModeBoundsWidth = (int)Math.Round(nibThreshold);
            NibModeBoundsWidthSlider.Value = SettingsManager.Settings.Advanced.NibModeBoundsWidth;

            double fingerThreshold = _fingerTouchWidth * 1.5;
            fingerThreshold = Math.Max(1, Math.Min(50, fingerThreshold));
            SettingsManager.Settings.Advanced.FingerModeBoundsWidth = (int)Math.Round(fingerThreshold);
            FingerModeBoundsWidthSlider.Value = SettingsManager.Settings.Advanced.FingerModeBoundsWidth;

            double touchMultiplier = 5 / (_fingerTouchWidth * 1.1);
            touchMultiplier = Math.Max(0, Math.Min(2, touchMultiplier));
            SettingsManager.Settings.Advanced.TouchMultiplier = Math.Round(touchMultiplier, 2);
            TouchMultiplierSlider.Value = SettingsManager.Settings.Advanced.TouchMultiplier;

            UpdateAllSliderTexts();
            SettingsActionHub.OnNibModeBoundsWidthChanged();
            SettingsActionHub.OnFingerModeBoundsWidthChanged();
            SettingsManager.SaveSettingsToFile();

            TextCalibrateResult.Text = $"校准成功！笔尖阈值={(int)nibThreshold}，手指阈值={(int)fingerThreshold}，触摸倍率={touchMultiplier:F2}";
        }

        #endregion

        #region Pressure & Eraser

        private void ToggleSwitchEnablePressureTouchMode_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Canvas.EnablePressureTouchMode = CardEnablePressureTouchMode.IsOn;
            SettingsActionHub.OnEnablePressureTouchModeChanged(CardEnablePressureTouchMode.IsOn);
            if (!CardEnablePressureTouchMode.IsOn || !SettingsManager.Settings.Canvas.DisablePressure)
                CardDisablePressure.IsOn = SettingsManager.Settings.Canvas.DisablePressure;
            SettingsManager.SaveSettingsToFile();
        }

        private void ToggleSwitchDisablePressure_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Canvas.DisablePressure = CardDisablePressure.IsOn;
            SettingsActionHub.OnDisablePressureChanged(CardDisablePressure.IsOn);
            if (!CardDisablePressure.IsOn || !SettingsManager.Settings.Canvas.EnablePressureTouchMode)
                CardEnablePressureTouchMode.IsOn = SettingsManager.Settings.Canvas.EnablePressureTouchMode;
            SettingsManager.SaveSettingsToFile();
        }

        #endregion

        #region Experimental

        private void ToggleSwitchForceFullScreen_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            try
            {
                SettingsManager.Settings.Advanced.IsEnableForceFullScreen = CardForceFullScreen.IsOn;
                SettingsManager.SaveSettingsToFile();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"设置强制全屏时出错: {ex.Message}");
            }
        }

        private void ToggleSwitchDPIChangeDetection_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            try
            {
                SettingsManager.Settings.Advanced.IsEnableDPIChangeDetection = CardDPIChangeDetection.IsOn;
                SettingsManager.SaveSettingsToFile();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"设置DPI变化检测时出错: {ex.Message}");
            }
        }

        private void ToggleSwitchResolutionChangeDetection_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            try
            {
                SettingsManager.Settings.Advanced.IsEnableResolutionChangeDetection = CardResolutionChangeDetection.IsOn;
                SettingsManager.SaveSettingsToFile();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"设置分辨率变化检测时出错: {ex.Message}");
            }
        }

        private void ToggleSwitchFullScreenHelper_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            try
            {
                SettingsManager.Settings.Advanced.IsEnableFullScreenHelper = CardFullScreenHelper.IsOn;
                SettingsManager.SaveSettingsToFile();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"设置全屏助手时出错: {ex.Message}");
            }
        }

        private void ToggleSwitchEdgeGestureUtil_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            try
            {
                SettingsManager.Settings.Advanced.IsEnableEdgeGestureUtil = CardEdgeGestureUtil.IsOn;
                SettingsManager.SaveSettingsToFile();

                if (OSVersion.GetOperatingSystem() >= OSVersionExtension.OperatingSystem.Windows10)
                {
                    var window = Application.Current.MainWindow;
                    if (window != null)
                    {
                        var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                        EdgeGestureUtil.DisableEdgeGestures(handle, CardEdgeGestureUtil.IsOn);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"设置边缘手势时出错: {ex.Message}");
            }
        }

        #endregion
    }
}
