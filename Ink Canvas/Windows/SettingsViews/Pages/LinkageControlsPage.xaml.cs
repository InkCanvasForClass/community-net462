using Ink_Canvas.Helpers;
using Ink_Canvas.Windows.SettingsViews.Helpers;
using System;
using System.Windows;
using System.Windows.Controls;
using Page = iNKORE.UI.WPF.Modern.Controls.Page;

namespace Ink_Canvas.Windows.SettingsViews.Pages
{
    public partial class LinkageControlsPage : Page
    {
        private bool _isLoaded = false;
        private DelayAction _sliderDelayAction = new DelayAction();

        public LinkageControlsPage()
        {
            InitializeComponent();
            Loaded += LinkageControlsPage_Loaded;
            Unloaded += LinkageControlsPage_Unloaded;
        }

        private void LinkageControlsPage_Loaded(object sender, RoutedEventArgs e)
        {
            LoadSettings();
            _isLoaded = true;
            UpdateAllSliderTexts();
            SliderTouchHelper.AddTouchSupportToAllSliders(this);
        }

        private void UpdateAllSliderTexts()
        {
            UpdateSliderText(PPTButtonLeftPositionValueSlider, PPTButtonLeftPositionText, "{0:F0}");
            UpdateSliderText(PPTButtonRightPositionValueSlider, PPTButtonRightPositionText, "{0:F0}");
            UpdateSliderText(PPTButtonLBPositionValueSlider, PPTButtonLBPositionText, "{0:F0}");
            UpdateSliderText(PPTButtonRBPositionValueSlider, PPTButtonRBPositionText, "{0:F0}");
            UpdateSliderText(PPTLSButtonOpacityValueSlider, PPTLSButtonOpacityText, "{0:P0}");
            UpdateSliderText(PPTRSButtonOpacityValueSlider, PPTRSButtonOpacityText, "{0:P0}");
            UpdateSliderText(PPTLBButtonOpacityValueSlider, PPTLBButtonOpacityText, "{0:P0}");
            UpdateSliderText(PPTRBButtonOpacityValueSlider, PPTRBButtonOpacityText, "{0:P0}");
            UpdateSliderText(PPTNavBarScaleValueSlider, PPTNavBarScaleText, "{0:F2}");
        }

        private void UpdateSliderText(Slider slider, TextBlock textBlock, string format)
        {
            if (slider == null || textBlock == null) return;
            textBlock.Text = string.Format(format, slider.Value);
        }

        private void LinkageControlsPage_Unloaded(object sender, RoutedEventArgs e)
        {
            _isLoaded = false;
        }

        private void LoadSettings()
        {
            _isLoaded = false;
            var ppt = SettingsManager.Settings.PowerPointSettings;

            CardShowPPTButton.IsOn = ppt.ShowPPTButton;
            var displayOpt = ppt.PPTButtonsDisplayOption.ToString();
            CheckboxEnableLBPPTButton.IsChecked = displayOpt.Length > 0 && displayOpt[0] == '2';
            CheckboxEnableRBPPTButton.IsChecked = displayOpt.Length > 1 && displayOpt[1] == '2';
            CheckboxEnableLSPPTButton.IsChecked = displayOpt.Length > 2 && displayOpt[2] == '2';
            CheckboxEnableRSPPTButton.IsChecked = displayOpt.Length > 3 && displayOpt[3] == '2';

            PPTButtonLeftPositionValueSlider.Value = ppt.PPTLSButtonPosition;
            PPTButtonRightPositionValueSlider.Value = ppt.PPTRSButtonPosition;
            PPTButtonLBPositionValueSlider.Value = ppt.PPTLBButtonPosition;
            PPTButtonRBPositionValueSlider.Value = ppt.PPTRBButtonPosition;

            PPTLSButtonOpacityValueSlider.Value = ppt.PPTLSButtonOpacity;
            PPTRSButtonOpacityValueSlider.Value = ppt.PPTRSButtonOpacity;
            PPTLBButtonOpacityValueSlider.Value = ppt.PPTLBButtonOpacity;
            PPTRBButtonOpacityValueSlider.Value = ppt.PPTRBButtonOpacity;

            PPTNavBarScaleValueSlider.Value = ppt.PPTNavBarScale;

            var sOpt = ppt.PPTSButtonsOption.ToString();
            CheckboxSPPTDisplayPage.IsChecked = sOpt.Length > 0 && sOpt[0] == '2';
            CheckboxSPPTHalfOpacity.IsChecked = sOpt.Length > 1 && sOpt[1] == '2';
            CheckboxSPPTBlackBackground.IsChecked = sOpt.Length > 2 && sOpt[2] == '2';

            var bOpt = ppt.PPTBButtonsOption.ToString();
            CheckboxBPPTDisplayPage.IsChecked = bOpt.Length > 0 && bOpt[0] == '2';
            CheckboxBPPTHalfOpacity.IsChecked = bOpt.Length > 1 && bOpt[1] == '2';
            CheckboxBPPTBlackBackground.IsChecked = bOpt.Length > 2 && bOpt[2] == '2';

            CardEnablePPTButtonPageClickable.IsOn = ppt.EnablePPTButtonPageClickable;
            CardEnablePPTButtonEnhancedPreview.IsOn = ppt.EnablePPTButtonEnhancedPreview;
            CardEnablePPTButtonLongPressPageTurn.IsOn = ppt.EnablePPTButtonLongPressPageTurn;

            _isLoaded = true;
        }

        #region PPT Flip Buttons

        private void ToggleSwitchShowPPTButton_OnToggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.PowerPointSettings.ShowPPTButton = CardShowPPTButton.IsOn;
            SettingsManager.SaveSettingsToFile();
            SettingsActionHub.OnShowPPTButtonChanged(CardShowPPTButton.IsOn);
        }

        private void ToggleSwitchEnablePPTButtonPageClickable_OnToggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.PowerPointSettings.EnablePPTButtonPageClickable = CardEnablePPTButtonPageClickable.IsOn;
            SettingsManager.SaveSettingsToFile();
        }

        private void ToggleSwitchEnablePPTButtonEnhancedPreview_OnToggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.PowerPointSettings.EnablePPTButtonEnhancedPreview = CardEnablePPTButtonEnhancedPreview.IsOn;
            SettingsManager.SaveSettingsToFile();
        }

        private void ToggleSwitchEnablePPTButtonLongPressPageTurn_OnToggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.PowerPointSettings.EnablePPTButtonLongPressPageTurn = CardEnablePPTButtonLongPressPageTurn.IsOn;
            SettingsManager.SaveSettingsToFile();
        }

        #endregion

        #region PPT Button Position & Opacity Sliders

        private void PPTButtonLeftPositionValueSlider_ValueChanged(object sender, RoutedEventArgs e)
        {
            UpdateSliderText(PPTButtonLeftPositionValueSlider, PPTButtonLeftPositionText, "{0:F0}");
            if (!_isLoaded) return;
            SettingsManager.Settings.PowerPointSettings.PPTLSButtonPosition = (int)PPTButtonLeftPositionValueSlider.Value;
            SettingsActionHub.OnPPTButtonPositionChanged();
            _sliderDelayAction.DebounceAction(2000, null, () => SettingsManager.SaveSettingsToFile());
        }

        private void PPTButtonRightPositionValueSlider_ValueChanged(object sender, RoutedEventArgs e)
        {
            UpdateSliderText(PPTButtonRightPositionValueSlider, PPTButtonRightPositionText, "{0:F0}");
            if (!_isLoaded) return;
            SettingsManager.Settings.PowerPointSettings.PPTRSButtonPosition = (int)PPTButtonRightPositionValueSlider.Value;
            SettingsActionHub.OnPPTButtonPositionChanged();
            _sliderDelayAction.DebounceAction(2000, null, () => SettingsManager.SaveSettingsToFile());
        }

        private void PPTButtonLBPositionValueSlider_ValueChanged(object sender, RoutedEventArgs e)
        {
            UpdateSliderText(PPTButtonLBPositionValueSlider, PPTButtonLBPositionText, "{0:F0}");
            if (!_isLoaded) return;
            SettingsManager.Settings.PowerPointSettings.PPTLBButtonPosition = (int)PPTButtonLBPositionValueSlider.Value;
            SettingsActionHub.OnPPTButtonPositionChanged();
            _sliderDelayAction.DebounceAction(2000, null, () => SettingsManager.SaveSettingsToFile());
        }

        private void PPTButtonRBPositionValueSlider_ValueChanged(object sender, RoutedEventArgs e)
        {
            UpdateSliderText(PPTButtonRBPositionValueSlider, PPTButtonRBPositionText, "{0:F0}");
            if (!_isLoaded) return;
            SettingsManager.Settings.PowerPointSettings.PPTRBButtonPosition = (int)PPTButtonRBPositionValueSlider.Value;
            SettingsActionHub.OnPPTButtonPositionChanged();
            _sliderDelayAction.DebounceAction(2000, null, () => SettingsManager.SaveSettingsToFile());
        }

        private void PPTLSButtonOpacityValueSlider_ValueChanged(object sender, RoutedEventArgs e)
        {
            UpdateSliderText(PPTLSButtonOpacityValueSlider, PPTLSButtonOpacityText, "{0:P0}");
            if (!_isLoaded) return;
            double roundedValue = Math.Round(PPTLSButtonOpacityValueSlider.Value, 1);
            PPTLSButtonOpacityValueSlider.ValueChanged -= PPTLSButtonOpacityValueSlider_ValueChanged;
            PPTLSButtonOpacityValueSlider.Value = roundedValue;
            PPTLSButtonOpacityValueSlider.ValueChanged += PPTLSButtonOpacityValueSlider_ValueChanged;
            SettingsManager.Settings.PowerPointSettings.PPTLSButtonOpacity = roundedValue;
            SettingsManager.SaveSettingsToFile();
            SettingsActionHub.OnPPTButtonOpacityChanged("LS", roundedValue);
        }

        private void PPTRSButtonOpacityValueSlider_ValueChanged(object sender, RoutedEventArgs e)
        {
            UpdateSliderText(PPTRSButtonOpacityValueSlider, PPTRSButtonOpacityText, "{0:P0}");
            if (!_isLoaded) return;
            double roundedValue = Math.Round(PPTRSButtonOpacityValueSlider.Value, 1);
            PPTRSButtonOpacityValueSlider.ValueChanged -= PPTRSButtonOpacityValueSlider_ValueChanged;
            PPTRSButtonOpacityValueSlider.Value = roundedValue;
            PPTRSButtonOpacityValueSlider.ValueChanged += PPTRSButtonOpacityValueSlider_ValueChanged;
            SettingsManager.Settings.PowerPointSettings.PPTRSButtonOpacity = roundedValue;
            SettingsManager.SaveSettingsToFile();
            SettingsActionHub.OnPPTButtonOpacityChanged("RS", roundedValue);
        }

        private void PPTLBButtonOpacityValueSlider_ValueChanged(object sender, RoutedEventArgs e)
        {
            UpdateSliderText(PPTLBButtonOpacityValueSlider, PPTLBButtonOpacityText, "{0:P0}");
            if (!_isLoaded) return;
            double roundedValue = Math.Round(PPTLBButtonOpacityValueSlider.Value, 1);
            PPTLBButtonOpacityValueSlider.ValueChanged -= PPTLBButtonOpacityValueSlider_ValueChanged;
            PPTLBButtonOpacityValueSlider.Value = roundedValue;
            PPTLBButtonOpacityValueSlider.ValueChanged += PPTLBButtonOpacityValueSlider_ValueChanged;
            SettingsManager.Settings.PowerPointSettings.PPTLBButtonOpacity = roundedValue;
            SettingsManager.SaveSettingsToFile();
            SettingsActionHub.OnPPTButtonOpacityChanged("LB", roundedValue);
        }

        private void PPTRBButtonOpacityValueSlider_ValueChanged(object sender, RoutedEventArgs e)
        {
            UpdateSliderText(PPTRBButtonOpacityValueSlider, PPTRBButtonOpacityText, "{0:P0}");
            if (!_isLoaded) return;
            double roundedValue = Math.Round(PPTRBButtonOpacityValueSlider.Value, 1);
            PPTRBButtonOpacityValueSlider.ValueChanged -= PPTRBButtonOpacityValueSlider_ValueChanged;
            PPTRBButtonOpacityValueSlider.Value = roundedValue;
            PPTRBButtonOpacityValueSlider.ValueChanged += PPTRBButtonOpacityValueSlider_ValueChanged;
            SettingsManager.Settings.PowerPointSettings.PPTRBButtonOpacity = roundedValue;
            SettingsManager.SaveSettingsToFile();
            SettingsActionHub.OnPPTButtonOpacityChanged("RB", roundedValue);
        }

        private void PPTNavBarScaleValueSlider_ValueChanged(object sender, RoutedEventArgs e)
        {
            UpdateSliderText(PPTNavBarScaleValueSlider, PPTNavBarScaleText, "{0:F2}");
            if (!_isLoaded) return;
            double roundedValue = Math.Round(PPTNavBarScaleValueSlider.Value, 2);
            PPTNavBarScaleValueSlider.ValueChanged -= PPTNavBarScaleValueSlider_ValueChanged;
            PPTNavBarScaleValueSlider.Value = roundedValue;
            PPTNavBarScaleValueSlider.ValueChanged += PPTNavBarScaleValueSlider_ValueChanged;
            SettingsManager.Settings.PowerPointSettings.PPTNavBarScale = roundedValue;
            SettingsManager.SaveSettingsToFile();
            SettingsActionHub.OnPPTNavBarScaleChanged(roundedValue);
        }

        #endregion

        #region PPT Button Display Checkboxes

        private void CheckboxEnableLBPPTButton_IsCheckChanged(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            var str = SettingsManager.Settings.PowerPointSettings.PPTButtonsDisplayOption.ToString();
            char[] c = str.ToCharArray();
            c[0] = CheckboxEnableLBPPTButton.IsChecked == true ? '2' : '1';
            SettingsManager.Settings.PowerPointSettings.PPTButtonsDisplayOption = int.Parse(new string(c));
            SettingsManager.SaveSettingsToFile();
            SettingsActionHub.OnPPTButtonsDisplayOptionChanged();
        }

        private void CheckboxEnableRBPPTButton_IsCheckChanged(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            var str = SettingsManager.Settings.PowerPointSettings.PPTButtonsDisplayOption.ToString();
            char[] c = str.ToCharArray();
            c[1] = CheckboxEnableRBPPTButton.IsChecked == true ? '2' : '1';
            SettingsManager.Settings.PowerPointSettings.PPTButtonsDisplayOption = int.Parse(new string(c));
            SettingsManager.SaveSettingsToFile();
            SettingsActionHub.OnPPTButtonsDisplayOptionChanged();
        }

        private void CheckboxEnableLSPPTButton_IsCheckChanged(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            var str = SettingsManager.Settings.PowerPointSettings.PPTButtonsDisplayOption.ToString();
            char[] c = str.ToCharArray();
            c[2] = CheckboxEnableLSPPTButton.IsChecked == true ? '2' : '1';
            SettingsManager.Settings.PowerPointSettings.PPTButtonsDisplayOption = int.Parse(new string(c));
            SettingsManager.SaveSettingsToFile();
            SettingsActionHub.OnPPTButtonsDisplayOptionChanged();
        }

        private void CheckboxEnableRSPPTButton_IsCheckChanged(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            var str = SettingsManager.Settings.PowerPointSettings.PPTButtonsDisplayOption.ToString();
            char[] c = str.ToCharArray();
            c[3] = CheckboxEnableRSPPTButton.IsChecked == true ? '2' : '1';
            SettingsManager.Settings.PowerPointSettings.PPTButtonsDisplayOption = int.Parse(new string(c));
            SettingsManager.SaveSettingsToFile();
            SettingsActionHub.OnPPTButtonsDisplayOptionChanged();
        }

        private void CheckboxSPPTDisplayPage_IsCheckChange(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            var str = SettingsManager.Settings.PowerPointSettings.PPTSButtonsOption.ToString();
            char[] c = str.ToCharArray();
            c[0] = CheckboxSPPTDisplayPage.IsChecked == true ? '2' : '1';
            SettingsManager.Settings.PowerPointSettings.PPTSButtonsOption = int.Parse(new string(c));
            SettingsManager.SaveSettingsToFile();
            SettingsActionHub.OnPPTSButtonsOptionChanged();
        }

        private void CheckboxSPPTHalfOpacity_IsCheckChange(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            var ppt = SettingsManager.Settings.PowerPointSettings;
            var str = ppt.PPTSButtonsOption.ToString();
            char[] c = str.ToCharArray();
            bool isHalf = CheckboxSPPTHalfOpacity.IsChecked == true;
            c[1] = isHalf ? '2' : '1';
            ppt.PPTSButtonsOption = int.Parse(new string(c));
            if (isHalf)
            {
                if (ppt.PPTLSButtonOpacity == 1.0) ppt.PPTLSButtonOpacity = 0.5;
                if (ppt.PPTRSButtonOpacity == 1.0) ppt.PPTRSButtonOpacity = 0.5;
                PPTLSButtonOpacityValueSlider.Value = ppt.PPTLSButtonOpacity;
                PPTRSButtonOpacityValueSlider.Value = ppt.PPTRSButtonOpacity;
            }
            else
            {
                if (ppt.PPTLSButtonOpacity == 0.5) ppt.PPTLSButtonOpacity = 1.0;
                if (ppt.PPTRSButtonOpacity == 0.5) ppt.PPTRSButtonOpacity = 1.0;
                PPTLSButtonOpacityValueSlider.Value = ppt.PPTLSButtonOpacity;
                PPTRSButtonOpacityValueSlider.Value = ppt.PPTRSButtonOpacity;
            }
            SettingsManager.SaveSettingsToFile();
            SettingsActionHub.OnPPTSButtonsOptionWithOpacityChanged();
        }

        private void CheckboxSPPTBlackBackground_IsCheckChange(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            var str = SettingsManager.Settings.PowerPointSettings.PPTSButtonsOption.ToString();
            char[] c = str.ToCharArray();
            c[2] = CheckboxSPPTBlackBackground.IsChecked == true ? '2' : '1';
            SettingsManager.Settings.PowerPointSettings.PPTSButtonsOption = int.Parse(new string(c));
            SettingsManager.SaveSettingsToFile();
            SettingsActionHub.OnPPTSButtonsOptionChanged();
        }

        private void CheckboxBPPTDisplayPage_IsCheckChange(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            var str = SettingsManager.Settings.PowerPointSettings.PPTBButtonsOption.ToString();
            char[] c = str.ToCharArray();
            c[0] = CheckboxBPPTDisplayPage.IsChecked == true ? '2' : '1';
            SettingsManager.Settings.PowerPointSettings.PPTBButtonsOption = int.Parse(new string(c));
            SettingsManager.SaveSettingsToFile();
            SettingsActionHub.OnPPTBButtonsOptionChanged();
        }

        private void CheckboxBPPTHalfOpacity_IsCheckChange(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            var ppt = SettingsManager.Settings.PowerPointSettings;
            var str = ppt.PPTBButtonsOption.ToString();
            char[] c = str.ToCharArray();
            bool isHalf = CheckboxBPPTHalfOpacity.IsChecked == true;
            c[1] = isHalf ? '2' : '1';
            ppt.PPTBButtonsOption = int.Parse(new string(c));
            if (isHalf)
            {
                if (ppt.PPTLBButtonOpacity == 1.0) ppt.PPTLBButtonOpacity = 0.5;
                if (ppt.PPTRBButtonOpacity == 1.0) ppt.PPTRBButtonOpacity = 0.5;
                PPTLBButtonOpacityValueSlider.Value = ppt.PPTLBButtonOpacity;
                PPTRBButtonOpacityValueSlider.Value = ppt.PPTRBButtonOpacity;
            }
            else
            {
                if (ppt.PPTLBButtonOpacity == 0.5) ppt.PPTLBButtonOpacity = 1.0;
                if (ppt.PPTRBButtonOpacity == 0.5) ppt.PPTRBButtonOpacity = 1.0;
                PPTLBButtonOpacityValueSlider.Value = ppt.PPTLBButtonOpacity;
                PPTRBButtonOpacityValueSlider.Value = ppt.PPTRBButtonOpacity;
            }
            SettingsManager.SaveSettingsToFile();
            SettingsActionHub.OnPPTBButtonsOptionWithOpacityChanged();
        }

        private void CheckboxBPPTBlackBackground_IsCheckChange(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            var str = SettingsManager.Settings.PowerPointSettings.PPTBButtonsOption.ToString();
            char[] c = str.ToCharArray();
            c[2] = CheckboxBPPTBlackBackground.IsChecked == true ? '2' : '1';
            SettingsManager.Settings.PowerPointSettings.PPTBButtonsOption = int.Parse(new string(c));
            SettingsManager.SaveSettingsToFile();
            SettingsActionHub.OnPPTBButtonsOptionChanged();
        }

        #endregion
    }
}
