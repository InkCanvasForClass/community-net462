using Ink_Canvas.Helpers;
using Ink_Canvas.Windows.SettingsViews.Helpers;
using System;
using System.Windows;
using System.Windows.Controls;
using Page = iNKORE.UI.WPF.Modern.Controls.Page;

namespace Ink_Canvas.Windows.SettingsViews.Pages
{
    public partial class MiniWhiteboardPage : Page
    {
        private bool _isLoaded = false;

        public MiniWhiteboardPage()
        {
            InitializeComponent();
            Loaded += Page_Loaded;
            Unloaded += Page_Unloaded;
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            LoadSettings();
            _isLoaded = true;
            UpdateAllSliderTexts();
            SliderTouchHelper.AddTouchSupportToAllSliders(this);
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            _isLoaded = false;
        }

        private void UpdateAllSliderTexts()
        {
            UpdateMiniWhiteboardSizeText();
            UpdateMiniWhiteboardOpacityText();
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
            if (settings?.MiniWhiteboard == null) return;

            ToggleSwitchMiniWhiteboardEnabled.IsOn = settings.MiniWhiteboard.IsEnabled;
            ToggleSwitchMiniWhiteboardSyncPPT.IsOn = settings.MiniWhiteboard.SyncWithPPTPages;
            MiniWhiteboardWidthSlider.Value = settings.MiniWhiteboard.DefaultWidth;
            MiniWhiteboardHeightSlider.Value = settings.MiniWhiteboard.DefaultHeight;
            MiniWhiteboardOpacitySlider.Value = settings.MiniWhiteboard.DefaultOpacity;
        }

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
    }
}
