using Ink_Canvas.Helpers;
using Ink_Canvas.Properties;
using Ink_Canvas.Windows.SettingsViews.Helpers;
using iNKORE.UI.WPF.Modern.Controls;
using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Page = iNKORE.UI.WPF.Modern.Controls.Page;

namespace Ink_Canvas.Windows.SettingsViews.Pages
{
    public partial class DisplayPage : Page
    {
        public static event Action<double> OnBottomOffsetChanged;

        private bool _isLoaded = false;
        private bool _isApplyingLanguageFromSettings = false;

        public DisplayPage()
        {
            InitializeComponent();
            Loaded += Page_Loaded;
            Unloaded += Page_Unloaded;
        }

        public static void NotifyBottomOffsetChanged(double val)
        {
            OnBottomOffsetChanged?.Invoke(val);
        }

        private void HandleBottomOffsetChanged(double val)
        {
            if (QuickPanelBottomOffsetSlider != null)
            {
                QuickPanelBottomOffsetSlider.Value = val;
                UpdateSliderText(QuickPanelBottomOffsetSlider, QuickPanelBottomOffsetText, "{0:F0}");
            }
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            LoadSettings();
            _isLoaded = true;
            UpdateAllSliderTexts();
            OnBottomOffsetChanged += HandleBottomOffsetChanged;
            SliderTouchHelper.AddTouchSupportToAllSliders(this);
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            _isLoaded = false;
            OnBottomOffsetChanged -= HandleBottomOffsetChanged;
        }

        private void UpdateAllSliderTexts()
        {
            UpdateSliderText(QuickPanelBottomOffsetSlider, QuickPanelBottomOffsetText, "{0:F0}");
            UpdateSliderText(QuickPanelOpacitySlider, QuickPanelOpacityText, "{0:P0}");
            UpdateSliderText(AutoCollapseDelaySlider, AutoCollapseDelayText, "{0:F1}s");
            UpdateMiniWhiteboardSizeText();
            UpdateMiniWhiteboardOpacityText();
        }

        private void UpdateSliderText(Slider slider, TextBlock textBlock, string format)
        {
            if (slider == null || textBlock == null) return;
            textBlock.Text = string.Format(format, slider.Value);
        }

        private void UpdateMiniWhiteboardSizeText()
        {
            if (MiniWhiteboardSizeText == null || MiniWhiteboardWidthSlider == null || MiniWhiteboardHeightSlider == null) return;
            MiniWhiteboardSizeText.Text = $"{(int)MiniWhiteboardWidthSlider.Value} \u00D7 {(int)MiniWhiteboardHeightSlider.Value}";
        }

        private void UpdateMiniWhiteboardOpacityText()
        {
            if (MiniWhiteboardOpacityText == null || MiniWhiteboardOpacitySlider == null) return;
            MiniWhiteboardOpacityText.Text = $"{Math.Round(MiniWhiteboardOpacitySlider.Value * 100):0}%";
        }

        private void LoadSettings()
        {
            var settings = SettingsManager.Settings;
            if (settings?.Appearance == null) return;

            // Theme
            ComboBoxTheme.SelectedIndex = settings.Appearance.Theme;
            SelectComboBoxItemByTag(ComboBoxWindowBackdrop, settings.Appearance.WindowBackdrop);

            _isApplyingLanguageFromSettings = true;
            try
            {
                var lang = settings.Appearance.Language ?? string.Empty;
                int langIndex = string.IsNullOrWhiteSpace(lang) ? 0 :
                    string.Equals(lang, "zh-CN", StringComparison.OrdinalIgnoreCase) ? 1 :
                    string.Equals(lang, "en-US", StringComparison.OrdinalIgnoreCase) ? 2 :
                    string.Equals(lang, "zh-ME", StringComparison.OrdinalIgnoreCase) ? 3 : 0;
                ComboBoxLanguage.SelectedIndex = langIndex;
            }
            finally
            {
                _isApplyingLanguageFromSettings = false;
            }

            // Clock
            ComboBoxTimeFormat.SelectedIndex = settings.Appearance.Use24HourTimeFormat ? 1 : 0;

            // Quick Panel
            CardEnableQuickPanel.IsOn = settings.Appearance.IsShowQuickPanel;
            QuickPanelBottomOffsetSlider.Value = settings.Appearance.QuickPanelBottomOffset;
            ComboBoxUnFoldBtnImg.SelectedIndex = settings.Appearance.UnFoldButtonImageType;
            CardAllowDragSidePanel.IsOn = settings.Appearance.AllowDragSidePanel;
            QuickPanelOpacitySlider.Value = settings.Appearance.QuickPanelOpacity;
            CardAutoCollapseQuickPanel.IsOn = settings.Appearance.IsAutoCollapseQuickPanel;
            AutoCollapseDelaySlider.Value = settings.Appearance.AutoCollapseQuickPanelDelay;

            // Splash Screen
            ToggleSwitchEnableSplashScreen.IsOn = settings.Appearance.EnableSplashScreen;
            ComboBoxSplashScreenStyle.SelectedIndex = settings.Appearance.SplashScreenStyle;
            UpdateCustomSplashImageVisibility();

            if (!string.IsNullOrEmpty(settings.Appearance.CustomSplashImagePath) &&
                System.IO.File.Exists(settings.Appearance.CustomSplashImagePath))
            {
                TextBlockCustomSplashPath.Text = System.IO.Path.GetFileName(settings.Appearance.CustomSplashImagePath);
                TextBlockCustomSplashPath.ToolTip = settings.Appearance.CustomSplashImagePath;
            }
            else
            {
                TextBlockCustomSplashPath.Text = ThemeStrings.Theme_CustomSplash_NotSelected;
                TextBlockCustomSplashPath.ToolTip = null;
            }

            UpdateTextAlignButtonAppearance(settings.Appearance.CustomSplashTextPosition);

            // Mini Whiteboard
            settings.MiniWhiteboard ??= new MiniWhiteboardSettings();
            ToggleSwitchMiniWhiteboardEnabled.IsOn = settings.MiniWhiteboard.IsEnabled;
            ToggleSwitchMiniWhiteboardSyncPPT.IsOn = settings.MiniWhiteboard.SyncWithPPTPages;
            MiniWhiteboardWidthSlider.Value = settings.MiniWhiteboard.DefaultWidth;
            MiniWhiteboardHeightSlider.Value = settings.MiniWhiteboard.DefaultHeight;
            MiniWhiteboardOpacitySlider.Value = settings.MiniWhiteboard.DefaultOpacity;
        }

        private static void SelectComboBoxItemByTag(ComboBox comboBox, string tag)
        {
            if (comboBox == null) return;

            var selectedItem = comboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
                ?? comboBox.Items.OfType<ComboBoxItem>().FirstOrDefault();

            comboBox.SelectedItem = selectedItem;
        }

        private static string GetSelectedComboBoxTag(ComboBox comboBox, string fallback)
        {
            return (comboBox?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? fallback;
        }

        #region Theme & Language

        private void ComboBoxTheme_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded) return;
            try
            {
                SettingsManager.Settings.Appearance.Theme = ComboBoxTheme.SelectedIndex;
                SettingsManager.SaveSettingsToFile();
                SettingsActionHub.OnThemeChanged(ComboBoxTheme.SelectedIndex);
            }
            catch (Exception ex) { Debug.WriteLine($"theme change error: {ex.Message}"); }
        }

        private void ComboBoxLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded || _isApplyingLanguageFromSettings) return;
            try
            {
                var index = ComboBoxLanguage.SelectedIndex;
                string language = index switch
                {
                    1 => "zh-CN",
                    2 => "en-US",
                    3 => "zh-ME",
                    _ => string.Empty
                };
                SettingsManager.Settings.Appearance.Language = language;
                SettingsManager.SaveSettingsToFile();
                SettingsActionHub.OnLanguageChanged(language);
            }
            catch (Exception ex) { Debug.WriteLine($"language change error: {ex.Message}"); }
        }

        private void ComboBoxWindowBackdrop_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded) return;
            try
            {
                var backdrop = GetSelectedComboBoxTag(ComboBoxWindowBackdrop, "None");
                SettingsManager.Settings.Appearance.WindowBackdrop = backdrop;
                SettingsManager.SaveSettingsToFile();

                if (Window.GetWindow(this) is SettingsWindow settingsWindow)
                {
                    settingsWindow.ApplyWindowBackdrop(backdrop);
                }
            }
            catch (Exception ex) { Debug.WriteLine($"backdrop change error: {ex.Message}"); }
        }

        #endregion

        #region Clock

        private void ComboBoxTimeFormat_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Appearance.Use24HourTimeFormat = ComboBoxTimeFormat.SelectedIndex == 1;
            SettingsManager.SaveSettingsToFile();
        }

        #endregion

        #region Quick Panel

        private void ToggleSwitchEnableQuickPanel_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Appearance.IsShowQuickPanel = CardEnableQuickPanel.IsOn;
            SettingsManager.SaveSettingsToFile();
        }

        private void ToggleSwitchAllowDragSidePanel_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Appearance.AllowDragSidePanel = CardAllowDragSidePanel.IsOn;
            SettingsManager.SaveSettingsToFile();
        }

        private void QuickPanelOpacitySlider_ValueChanged(object sender, RoutedEventArgs e)
        {
            UpdateSliderText(QuickPanelOpacitySlider, QuickPanelOpacityText, "{0:P0}");
            if (!_isLoaded) return;
            SettingsManager.Settings.Appearance.QuickPanelOpacity = QuickPanelOpacitySlider.Value;
            SettingsManager.SaveSettingsToFile();
            SettingsActionHub.OnQuickPanelOpacityChanged(QuickPanelOpacitySlider.Value);
        }

        private void ToggleSwitchAutoCollapseQuickPanel_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Appearance.IsAutoCollapseQuickPanel = CardAutoCollapseQuickPanel.IsOn;
            SettingsManager.SaveSettingsToFile();
            SettingsActionHub.OnAutoCollapseQuickPanelChanged();
        }

        private void AutoCollapseDelaySlider_ValueChanged(object sender, RoutedEventArgs e)
        {
            UpdateSliderText(AutoCollapseDelaySlider, AutoCollapseDelayText, "{0:F1}s");
            if (!_isLoaded) return;
            SettingsManager.Settings.Appearance.AutoCollapseQuickPanelDelay = AutoCollapseDelaySlider.Value;
            SettingsManager.SaveSettingsToFile();
        }

        private void QuickPanelBottomOffsetSlider_ValueChanged(object sender, RoutedEventArgs e)
        {
            UpdateSliderText(QuickPanelBottomOffsetSlider, QuickPanelBottomOffsetText, "{0:F0}");
            if (!_isLoaded) return;
            var val = Math.Round(QuickPanelBottomOffsetSlider.Value);
            if (QuickPanelBottomOffsetSlider.Value != val)
            {
                QuickPanelBottomOffsetSlider.Value = val;
                return;
            }
            SettingsManager.Settings.Appearance.QuickPanelBottomOffset = val;
            SettingsManager.SaveSettingsToFile();
            SettingsActionHub.OnQuickPanelBottomOffsetChanged(val);
        }

        private void ComboBoxUnFoldBtnImg_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Appearance.UnFoldButtonImageType = ComboBoxUnFoldBtnImg.SelectedIndex;
            SettingsManager.SaveSettingsToFile();
            SettingsActionHub.OnUnFoldButtonImageTypeChanged(ComboBoxUnFoldBtnImg.SelectedIndex);
        }

        #endregion

        #region Splash Screen

        private void ToggleSwitchEnableSplashScreen_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Appearance.EnableSplashScreen = ToggleSwitchEnableSplashScreen.IsOn;
            SettingsManager.SaveSettingsToFile();
        }

        private void ComboBoxSplashScreenStyle_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Appearance.SplashScreenStyle = ComboBoxSplashScreenStyle.SelectedIndex;
            SettingsManager.SaveSettingsToFile();
            UpdateCustomSplashImageVisibility();
        }

        private void UpdateCustomSplashImageVisibility()
        {
            bool isCustomSelected = ComboBoxSplashScreenStyle.SelectedIndex == 7;
            CardCustomSplashImage.Visibility = isCustomSelected ? Visibility.Visible : Visibility.Collapsed;
            CardCustomSplashTextPosition.Visibility = isCustomSelected ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BorderTextAlign_Click(object sender, MouseButtonEventArgs e)
        {
            if (!_isLoaded) return;

            if (sender is Border border && border.Tag != null)
            {
                int selectedIndex = int.Parse(border.Tag.ToString());
                SettingsManager.Settings.Appearance.CustomSplashTextPosition = selectedIndex;
                SettingsManager.SaveSettingsToFile();
                UpdateTextAlignButtonAppearance(selectedIndex);
            }
        }

        private void UpdateTextAlignButtonAppearance(int selectedIndex)
        {
            AnimateIndicatorToPosition(selectedIndex);
        }

        private void AnimateIndicatorToPosition(int position)
        {
            double targetX = position * 36;

            var animation = new DoubleAnimation
            {
                To = targetX,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            IndicatorTranslateTransform.BeginAnimation(TranslateTransform.XProperty, animation);

            var isDarkTheme = SettingsManager.Settings.Appearance.Theme == 1;

            if (isDarkTheme)
            {
                SelectionIndicator.Background = new SolidColorBrush(Color.FromArgb(40, 0, 120, 215));
                SelectionIndicator.BorderBrush = new SolidColorBrush(Color.FromArgb(150, 0, 120, 215));
            }
            else
            {
                SelectionIndicator.Background = new SolidColorBrush(Color.FromArgb(25, 0, 120, 215));
                SelectionIndicator.BorderBrush = new SolidColorBrush(Color.FromArgb(120, 0, 120, 215));
            }
        }

        private void ButtonBrowseCustomSplash_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var openFileDialog = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp|All Files|*.*",
                    Title = ThemeStrings.Theme_SelectCustomSplashImage
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    string selectedPath = openFileDialog.FileName;
                    if (!string.IsNullOrEmpty(selectedPath))
                    {
                        SettingsManager.Settings.Appearance.CustomSplashImagePath = selectedPath;
                        SettingsManager.SaveSettingsToFile();
                        TextBlockCustomSplashPath.Text = System.IO.Path.GetFileName(selectedPath);
                        TextBlockCustomSplashPath.ToolTip = selectedPath;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"browse custom splash error: {ex.Message}");
            }
        }

        private void ButtonClearCustomSplash_Click(object sender, RoutedEventArgs e)
        {
            SettingsManager.Settings.Appearance.CustomSplashImagePath = string.Empty;
            SettingsManager.SaveSettingsToFile();
            TextBlockCustomSplashPath.Text = ThemeStrings.Theme_CustomSplash_NotSelected;
            TextBlockCustomSplashPath.ToolTip = null;
        }

        #endregion

        #region Mini Whiteboard

        private void ToggleSwitchMiniWhiteboardEnabled_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.MiniWhiteboard ??= new MiniWhiteboardSettings();
            SettingsManager.Settings.MiniWhiteboard.IsEnabled = ToggleSwitchMiniWhiteboardEnabled.IsOn;
            SettingsManager.SaveSettingsToFile();
        }

        private void ToggleSwitchMiniWhiteboardSyncPPT_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.MiniWhiteboard ??= new MiniWhiteboardSettings();
            SettingsManager.Settings.MiniWhiteboard.SyncWithPPTPages = ToggleSwitchMiniWhiteboardSyncPPT.IsOn;
            SettingsManager.SaveSettingsToFile();
        }

        private void MiniWhiteboardWidthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateMiniWhiteboardSizeText();
            if (!_isLoaded) return;
            SettingsManager.Settings.MiniWhiteboard ??= new MiniWhiteboardSettings();
            SettingsManager.Settings.MiniWhiteboard.DefaultWidth = MiniWhiteboardWidthSlider.Value;
            SettingsManager.SaveSettingsToFile();
        }

        private void MiniWhiteboardHeightSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateMiniWhiteboardSizeText();
            if (!_isLoaded) return;
            SettingsManager.Settings.MiniWhiteboard ??= new MiniWhiteboardSettings();
            SettingsManager.Settings.MiniWhiteboard.DefaultHeight = MiniWhiteboardHeightSlider.Value;
            SettingsManager.SaveSettingsToFile();
        }

        private void MiniWhiteboardOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateMiniWhiteboardOpacityText();
            if (!_isLoaded) return;
            SettingsManager.Settings.MiniWhiteboard ??= new MiniWhiteboardSettings();
            SettingsManager.Settings.MiniWhiteboard.DefaultOpacity = MiniWhiteboardOpacitySlider.Value;
            SettingsManager.SaveSettingsToFile();
        }

        #endregion
    }
}
