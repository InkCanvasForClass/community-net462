using Ink_Canvas.Helpers;
using Ink_Canvas.Properties;
using iNKORE.UI.WPF.Modern.Controls;
using System;
using System.Windows;
using System.Windows.Controls;
using Page = iNKORE.UI.WPF.Modern.Controls.Page;

namespace Ink_Canvas.Windows.SettingsViews.Pages
{
    public partial class BoothPage : Page
    {
        private bool _isLoaded;

        public BoothPage()
        {
            InitializeComponent();
            Loaded += Page_Loaded;
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            LoadSettings();
            _isLoaded = true;
        }

        private void LoadSettings()
        {
            var settings = Helpers.SettingsManager.Settings;
            if (settings?.Camera == null) return;

            // Populate camera list
            var cameraService = new Ink_Canvas.Helpers.CameraService(
                settings.Camera.RotationAngle,
                settings.Camera.ResolutionWidth,
                settings.Camera.ResolutionHeight);

            ComboBoxCamera.Items.Clear();
            foreach (var name in cameraService.GetCameraNames())
                ComboBoxCamera.Items.Add(new ComboBoxItem { Content = name });

            if (ComboBoxCamera.Items.Count > 0)
                ComboBoxCamera.SelectedIndex = Math.Max(0, Math.Min(settings.Camera.SelectedCameraIndex, ComboBoxCamera.Items.Count - 1));

            // Rotation
            ComboBoxRotation.SelectedIndex = Math.Max(0, Math.Min(settings.Camera.RotationAngle, 3));

            // Resolution
            string resTag = $"{settings.Camera.ResolutionWidth},{settings.Camera.ResolutionHeight}";
            for (int i = 0; i < ComboBoxResolution.Items.Count; i++)
            {
                if (ComboBoxResolution.Items[i] is ComboBoxItem item && item.Tag?.ToString() == resTag)
                {
                    ComboBoxResolution.SelectedIndex = i;
                    break;
                }
            }

            // Default to 1920x1080 if no match
            if (ComboBoxResolution.SelectedIndex < 0)
                ComboBoxResolution.SelectedIndex = 2;
        }

        private void ComboBoxCamera_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded) return;
            var settings = Helpers.SettingsManager.Settings;
            if (settings?.Camera == null) return;

            settings.Camera.SelectedCameraIndex = ComboBoxCamera.SelectedIndex;
            Helpers.SettingsManager.SaveSettingsToFile();
        }

        private void ComboBoxRotation_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded) return;
            var settings = Helpers.SettingsManager.Settings;
            if (settings?.Camera == null) return;

            if (ComboBoxRotation.SelectedItem is ComboBoxItem item &&
                int.TryParse(item.Tag?.ToString(), out int angle))
            {
                settings.Camera.RotationAngle = angle;
                Helpers.SettingsManager.SaveSettingsToFile();
            }
        }

        private void ComboBoxResolution_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded) return;
            var settings = Helpers.SettingsManager.Settings;
            if (settings?.Camera == null) return;

            if (ComboBoxResolution.SelectedItem is ComboBoxItem item)
            {
                var parts = item.Tag?.ToString()?.Split(',');
                if (parts?.Length == 2 &&
                    int.TryParse(parts[0], out int w) &&
                    int.TryParse(parts[1], out int h))
                {
                    settings.Camera.ResolutionWidth = w;
                    settings.Camera.ResolutionHeight = h;
                    Helpers.SettingsManager.SaveSettingsToFile();
                }
            }
        }
    }
}
