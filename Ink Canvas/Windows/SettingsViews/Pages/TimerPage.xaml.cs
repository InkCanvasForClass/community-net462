using Ink_Canvas.Helpers;
using Ink_Canvas.Properties;
using Ink_Canvas.Windows.SettingsViews.Helpers;
using System;
using System.Windows;
using System.Windows.Controls;
using MessageBox = iNKORE.UI.WPF.Modern.Controls.MessageBox;
using Page = iNKORE.UI.WPF.Modern.Controls.Page;

namespace Ink_Canvas.Windows.SettingsViews.Pages
{
    public partial class TimerPage : Page
    {
        private bool _isLoaded = false;

        public TimerPage()
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

        private void UpdateAllSliderTexts()
        {
            UpdateSliderText(TimerVolumeSlider, TimerVolumeText, "{0:F1}");
            UpdateSliderText(ProgressiveReminderVolumeSlider, ProgressiveReminderVolumeText, "{0:F1}");
        }

        private void UpdateSliderText(Slider slider, TextBlock textBlock, string format)
        {
            if (slider == null || textBlock == null) return;
            textBlock.Text = string.Format(format, slider.Value);
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            _isLoaded = false;
        }

        private void LoadSettings()
        {
            var settings = SettingsManager.Settings;
            if (settings?.RandSettings == null) return;

            if (settings.RandSettings.UseLegacyTimerUI)
                ComboBoxTimerUIStyle.SelectedIndex = 0;
            else
                ComboBoxTimerUIStyle.SelectedIndex = 1;
            ToggleSwitchEnableOvertimeCountUp.IsOn = settings.RandSettings.EnableOvertimeCountUp;

            bool canEnableRedText = settings.RandSettings.EnableOvertimeCountUp && settings.RandSettings.EnableOvertimeRedText;
            ToggleSwitchEnableOvertimeRedText.IsOn = canEnableRedText;

            TimerVolumeSlider.Value = settings.RandSettings.TimerVolume;
            ToggleSwitchEnableProgressiveReminder.IsOn = settings.RandSettings.EnableProgressiveReminder;
            ProgressiveReminderVolumeSlider.Value = settings.RandSettings.ProgressiveReminderVolume;
        }

        #region Timer

        private void ComboBoxTimerUIStyle_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded) return;
            var selectedItem = ComboBoxTimerUIStyle.SelectedItem as ComboBoxItem;
            var tag = selectedItem?.Tag?.ToString() ?? "Default";
            SettingsManager.Settings.RandSettings.UseLegacyTimerUI = tag == "Legacy";
            SettingsManager.Settings.RandSettings.UseNewStyleUI = tag == "NewStyle";
            SettingsManager.SaveSettingsToFile();
        }

        private void ToggleSwitchEnableOvertimeCountUp_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.RandSettings.EnableOvertimeCountUp = ToggleSwitchEnableOvertimeCountUp.IsOn;

            if (!ToggleSwitchEnableOvertimeCountUp.IsOn)
            {
                ToggleSwitchEnableOvertimeRedText.IsOn = false;
                SettingsManager.Settings.RandSettings.EnableOvertimeRedText = false;
            }

            SettingsManager.SaveSettingsToFile();
        }

        private void ToggleSwitchEnableOvertimeRedText_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;

            if (ToggleSwitchEnableOvertimeRedText.IsOn && !ToggleSwitchEnableOvertimeCountUp.IsOn)
            {
                ToggleSwitchEnableOvertimeCountUp.IsOn = true;
                SettingsManager.Settings.RandSettings.EnableOvertimeCountUp = true;
            }

            SettingsManager.Settings.RandSettings.EnableOvertimeRedText = ToggleSwitchEnableOvertimeRedText.IsOn;
            SettingsManager.SaveSettingsToFile();
        }

        private void TimerVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateSliderText(TimerVolumeSlider, TimerVolumeText, "{0:F1}");
            if (!_isLoaded) return;
            var slider = TimerVolumeSlider;
            var val = Math.Round(slider.Value, 2);
            if (slider.Value != val)
            {
                slider.Value = val;
                return;
            }
            SettingsManager.Settings.RandSettings.TimerVolume = val;
            SettingsManager.SaveSettingsToFile();
        }

        private void ButtonSelectCustomTimerSound_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = RandomStrings.Random_SelectTimerAlarm,
                Filter = RandomStrings.Random_AudioFilter,
                DefaultExt = "wav"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                SettingsManager.Settings.RandSettings.CustomTimerSoundPath = openFileDialog.FileName;
                SettingsManager.SaveSettingsToFile();
                MessageBox.Show(RandomStrings.Random_CustomAlarmSuccess, RandomStrings.Random_AlarmSetupSuccess, MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void ButtonResetTimerSound_Click(object sender, RoutedEventArgs e)
        {
            SettingsManager.Settings.RandSettings.CustomTimerSoundPath = "";
            SettingsManager.SaveSettingsToFile();
            MessageBox.Show(RandomStrings.Random_ResetAlarmSuccess, RandomStrings.Random_ResetSuccess, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ToggleSwitchEnableProgressiveReminder_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.RandSettings.EnableProgressiveReminder = ToggleSwitchEnableProgressiveReminder.IsOn;
            SettingsManager.SaveSettingsToFile();
        }

        private void ProgressiveReminderVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateSliderText(ProgressiveReminderVolumeSlider, ProgressiveReminderVolumeText, "{0:F1}");
            if (!_isLoaded) return;
            var slider = ProgressiveReminderVolumeSlider;
            var val = Math.Round(slider.Value, 2);
            if (slider.Value != val)
            {
                slider.Value = val;
                return;
            }
            SettingsManager.Settings.RandSettings.ProgressiveReminderVolume = val;
            SettingsManager.SaveSettingsToFile();
        }

        private void ButtonSelectCustomProgressiveReminderSound_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = RandomStrings.Random_SelectProgressiveAlarm,
                Filter = RandomStrings.Random_AudioFilter,
                DefaultExt = "wav"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                SettingsManager.Settings.RandSettings.ProgressiveReminderSoundPath = openFileDialog.FileName;
                SettingsManager.SaveSettingsToFile();
            }
        }

        private void ButtonResetProgressiveReminderSound_Click(object sender, RoutedEventArgs e)
        {
            SettingsManager.Settings.RandSettings.ProgressiveReminderSoundPath = "";
            SettingsManager.SaveSettingsToFile();
        }

        #endregion
    }
}
