using Ink_Canvas.Helpers;
using Ink_Canvas.Windows.SettingsViews.Helpers;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using MessageBox = iNKORE.UI.WPF.Modern.Controls.MessageBox;
using Page = iNKORE.UI.WPF.Modern.Controls.Page;

namespace Ink_Canvas.Windows.SettingsViews.Pages
{
    public partial class StoragePage : Page
    {
        private bool _isLoaded = false;
        // 已知子目录定义。其余顶层文件归入“核心文件”或“其他”。
        private static readonly string[] LogDirs = { "Logs", "Crashes" };
        private static readonly string[] InkDirs = { "Saves" };
        private static readonly string[] BackupDirs = { "Backups" };
        private static readonly string[] CustomDirs = { "icons", "backgrounds" };
        private static readonly string[] PluginDirs = { "Plugins", "PluginPackages", "PluginConfigs" };
        private static readonly string[] UpdateDirs = { "AutoUpdate" };
        // 视为核心文件的目录（配置 / ppt-agent 联动组件 / 自动化工作流 / .NET 运行时等）
        private static readonly string[] ConfigDirs = { "Configs", "ppt-agent", "Automations", "runtimes" };

        // 视为核心文件的扩展名(位于应用根目录下)
        private static readonly string[] CoreFileExtensions =
        {
            ".exe", ".dll", ".json", ".enc", ".dat", ".config", ".pdb",
            ".xml", ".manifest", ".runtimeconfig.json", ".deps.json"
        };

        private long _coreSize, _logsSize, _inkSize, _backupsSize,
                     _customSize, _pluginsSize, _updateSize, _otherSize;

        public StoragePage()
        {
            InitializeComponent();
            Loaded += StoragePage_Loaded;
            Unloaded += StoragePage_Unloaded;
        }

        private async void StoragePage_Loaded(object sender, RoutedEventArgs e)
        {
            LoadSettings();
            await RefreshAsync();
            _isLoaded = true;
            SliderTouchHelper.AddTouchSupportToAllSliders(this);
        }

        private void StoragePage_Unloaded(object sender, RoutedEventArgs e)
        {
            _isLoaded = false;
        }

        private void LoadSettings()
        {
            _isLoaded = false;
            try
            {
                var auto = SettingsManager.Settings.Automation;

                CardSaveScreenshotsInDateFolders.IsOn = auto.IsSaveScreenshotsInDateFolders;
                CardAutoSaveStrokesAtScreenshot.IsOn = auto.IsAutoSaveStrokesAtScreenshot;
                CardAutoSaveStrokesAtClear.IsOn = auto.IsAutoSaveScreenshotAtClear;
                CardSaveStrokesAsXML.IsOn = auto.IsSaveStrokesAsXML;
                CardEnableAutoSaveStrokes.IsOn = auto.IsEnableAutoSaveStrokes;

                var interval = auto.AutoSaveStrokesIntervalMinutes;
                foreach (ComboBoxItem item in ComboBoxAutoSaveStrokesInterval.Items)
                {
                    if (item.Tag != null && int.TryParse(item.Tag.ToString(), out int tagVal) && tagVal == interval)
                    {
                        ComboBoxAutoSaveStrokesInterval.SelectedItem = item;
                        break;
                    }
                }

                CardAutoDelSavedFiles.IsOn = auto.AutoDelSavedFiles;
                ComboBoxAutoDelSavedFilesDaysThreshold.SelectedIndex = auto.AutoDelSavedFilesDaysThreshold switch
                {
                    7 => 0,
                    14 => 1,
                    30 => 2,
                    60 => 3,
                    90 => 4,
                    _ => 2
                };

                SideControlMinimumAutomationSlider.Value = auto.MinimumAutomationStrokeNumber;
                CardSaveFullPageStrokes.IsOn = auto.IsSaveFullPageStrokes;

                CardUseCustomSaveFileName.IsOn = auto.IsUseCustomSaveFileName;
                TextBoxCustomSaveFileNameTemplate.Text = auto.CustomSaveFileNameTemplate;
                SyncSaveFileNamePresetSelection(auto.CustomSaveFileNameTemplate);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"加载存储设置时出错: {ex.Message}", LogHelper.LogType.Error);
            }
            _isLoaded = true;
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            _ = RefreshAsync();
        }

        private void BtnOpenAppFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = App.RootPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"打开应用目录失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        private async Task RefreshAsync()
        {
            SetAllSizesText(LocalizationHelper.GetString("Storage_Calculating"));
            try
            {
                await Task.Run(() => CalculateSizes());
                UpdateUI();
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"计算存储占用失败: {ex.Message}", LogHelper.LogType.Error);
                SetAllSizesText(LocalizationHelper.GetString("Storage_CalculateFailed"));
            }
        }

        private void CalculateSizes()
        {
            _coreSize = _logsSize = _inkSize = _backupsSize =
                _customSize = _pluginsSize = _updateSize = _otherSize = 0;

            string root = App.RootPath;
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return;

            // 顶层目录归类
            foreach (var dir in SafeEnumerateDirectories(root))
            {
                string name = Path.GetFileName(dir);
                long size = GetDirectorySize(dir);

                if (LogDirs.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
                    _logsSize += size;
                else if (InkDirs.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
                    _inkSize += size;
                else if (BackupDirs.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
                    _backupsSize += size;
                else if (CustomDirs.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
                    _customSize += size;
                else if (PluginDirs.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
                    _pluginsSize += size;
                else if (UpdateDirs.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
                    _updateSize += size;
                else if (ConfigDirs.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
                    _coreSize += size;
                else
                    _otherSize += size;
            }

            // 顶层文件按扩展名归类
            foreach (var file in SafeEnumerateFiles(root))
            {
                long size = SafeFileLength(file);
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (CoreFileExtensions.Contains(ext))
                    _coreSize += size;
                else
                    _otherSize += size;
            }
        }

        private void UpdateUI()
        {
            long total = _coreSize + _logsSize + _inkSize + _backupsSize
                       + _customSize + _pluginsSize + _updateSize + _otherSize;

            TotalSizeTextBlock.Text = FormatSize(total);

            CoreSizeText.Text = FormatSize(_coreSize);
            LogsSizeText.Text = FormatSize(_logsSize);
            InkSizeText.Text = FormatSize(_inkSize);
            BackupsSizeText.Text = FormatSize(_backupsSize);
            CustomSizeText.Text = FormatSize(_customSize);
            PluginsSizeText.Text = FormatSize(_pluginsSize);
            UpdateSizeText.Text = FormatSize(_updateSize);
            OtherSizeText.Text = FormatSize(_otherSize);

            // 更新柱状图列宽
            if (total > 0)
            {
                BarCoreCol.Width = new GridLength(_coreSize, GridUnitType.Star);
                BarLogsCol.Width = new GridLength(_logsSize, GridUnitType.Star);
                BarInkCol.Width = new GridLength(_inkSize, GridUnitType.Star);
                BarBackupsCol.Width = new GridLength(_backupsSize, GridUnitType.Star);
                BarCustomCol.Width = new GridLength(_customSize, GridUnitType.Star);
                BarPluginsCol.Width = new GridLength(_pluginsSize, GridUnitType.Star);
                BarUpdateCol.Width = new GridLength(_updateSize, GridUnitType.Star);
                BarOtherCol.Width = new GridLength(_otherSize, GridUnitType.Star);
            }
            else
            {
                BarCoreCol.Width = BarLogsCol.Width = BarInkCol.Width =
                    BarBackupsCol.Width = BarCustomCol.Width =
                    BarPluginsCol.Width = BarUpdateCol.Width =
                    BarOtherCol.Width = new GridLength(0, GridUnitType.Star);
            }

            // 占磁盘比例
            try
            {
                var driveLetter = Path.GetPathRoot(App.RootPath);
                if (!string.IsNullOrEmpty(driveLetter))
                {
                    var drive = new DriveInfo(driveLetter);
                    if (drive.IsReady && drive.TotalSize > 0)
                    {
                        double pct = (double)total / drive.TotalSize * 100.0;
                        DiskPercentTextBlock.Text = pct < 0.01 ? "<0.01 %" : $"{pct:0.##} %";
                        return;
                    }
                }
            }
            catch { }
            DiskPercentTextBlock.Text = "—";
        }

        private void SetAllSizesText(string text)
        {
            TotalSizeTextBlock.Text = text;
            CoreSizeText.Text = text;
            LogsSizeText.Text = text;
            InkSizeText.Text = text;
            BackupsSizeText.Text = text;
            CustomSizeText.Text = text;
            PluginsSizeText.Text = text;
            UpdateSizeText.Text = text;
            OtherSizeText.Text = text;
        }

        #region 清理操作

        private void BtnCleanLogs_Click(object sender, RoutedEventArgs e)
        {
            CleanWithConfirm(LocalizationHelper.GetString("Storage_Logs_Header"), LogDirs, keepRoot: true);
        }

        private void BtnCleanInk_Click(object sender, RoutedEventArgs e)
        {
            CleanWithConfirm(LocalizationHelper.GetString("Storage_Ink_Header"), InkDirs, keepRoot: true);
        }

        private void BtnCleanBackups_Click(object sender, RoutedEventArgs e)
        {
            CleanWithConfirm(LocalizationHelper.GetString("Storage_Backups_Header"), BackupDirs, keepRoot: true);
        }

        private void BtnCleanUpdate_Click(object sender, RoutedEventArgs e)
        {
            CleanWithConfirm(LocalizationHelper.GetString("Storage_Update_Header"), UpdateDirs, keepRoot: true);
        }

        private async void CleanWithConfirm(string displayName, string[] subDirs, bool keepRoot)
        {
            var result = MessageBox.Show(
                string.Format(LocalizationHelper.GetString("Storage_Confirm_Body"), displayName).Replace("\\n", "\n"),
                LocalizationHelper.GetString("Storage_Confirm_Title"),
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.OK) return;

            var second = MessageBox.Show(
                string.Format(LocalizationHelper.GetString("Storage_Confirm_Second_Body"), displayName),
                LocalizationHelper.GetString("Storage_Confirm_Second_Title"),
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);
            if (second != MessageBoxResult.OK) return;

            try
            {
                await Task.Run(() =>
                {
                    foreach (var sub in subDirs)
                    {
                        string path = Path.Combine(App.RootPath, sub);
                        if (!Directory.Exists(path)) continue;

                        if (keepRoot)
                        {
                            foreach (var file in SafeEnumerateFiles(path, recursive: true))
                                TryDeleteFile(file);
                            foreach (var dir in SafeEnumerateDirectories(path).Reverse())
                                TryDeleteDirectory(dir);
                        }
                        else
                        {
                            TryDeleteDirectory(path);
                        }
                    }
                });
                LogHelper.WriteLogToFile($"已清理「{displayName}」分类");
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"清理「{displayName}」失败: {ex.Message}", LogHelper.LogType.Error);
                MessageBox.Show(
                    string.Format(LocalizationHelper.GetString("Storage_CleanFailed"), ex.Message),
                    LocalizationHelper.GetString("Storage_Confirm_Title"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }

            await RefreshAsync();
        }

        #endregion

        #region 工具方法

        private static long GetDirectorySize(string path)
        {
            long total = 0;
            try
            {
                foreach (var file in SafeEnumerateFiles(path, recursive: true))
                    total += SafeFileLength(file);
            }
            catch { }
            return total;
        }

        private static System.Collections.Generic.IEnumerable<string> SafeEnumerateDirectories(string path)
        {
            try { return Directory.EnumerateDirectories(path); }
            catch { return System.Linq.Enumerable.Empty<string>(); }
        }

        private static System.Collections.Generic.IEnumerable<string> SafeEnumerateFiles(string path, bool recursive = false)
        {
            try
            {
                return Directory.EnumerateFiles(path, "*",
                    recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
            }
            catch { return System.Linq.Enumerable.Empty<string>(); }
        }

        private static long SafeFileLength(string path)
        {
            try { return new FileInfo(path).Length; }
            catch { return 0; }
        }

        private static void TryDeleteFile(string path)
        {
            try { File.Delete(path); } catch { }
        }

        private static void TryDeleteDirectory(string path)
        {
            try { Directory.Delete(path, true); } catch { }
        }

        private static string FormatSize(long bytes)
        {
            if (bytes <= 0) return "0 B";
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double size = bytes;
            int u = 0;
            while (size >= 1024 && u < units.Length - 1)
            {
                size /= 1024;
                u++;
            }
            return u == 0 ? $"{(long)size} {units[u]}" : $"{size:0.##} {units[u]}";
        }

        #endregion

        #region 自动保存

        private void ToggleSwitchSaveScreenshotsInDateFolders_Toggled(object sender, RoutedEventArgs e)
        { if (!_isLoaded) return; SettingsManager.Settings.Automation.IsSaveScreenshotsInDateFolders = CardSaveScreenshotsInDateFolders.IsOn; SettingsManager.SaveSettingsToFile(); }

        private void ToggleSwitchAutoSaveStrokesAtScreenshot_Toggled(object sender, RoutedEventArgs e)
        { if (!_isLoaded) return; SettingsManager.Settings.Automation.IsAutoSaveStrokesAtScreenshot = CardAutoSaveStrokesAtScreenshot.IsOn; SettingsManager.SaveSettingsToFile(); }

        private void ToggleSwitchAutoSaveStrokesAtClear_Toggled(object sender, RoutedEventArgs e)
        { if (!_isLoaded) return; SettingsManager.Settings.Automation.IsAutoSaveScreenshotAtClear = CardAutoSaveStrokesAtClear.IsOn; SettingsManager.SaveSettingsToFile(); }

        private void ToggleSwitchSaveStrokesAsXML_Toggled(object sender, RoutedEventArgs e)
        { if (!_isLoaded) return; SettingsManager.Settings.Automation.IsSaveStrokesAsXML = CardSaveStrokesAsXML.IsOn; SettingsManager.SaveSettingsToFile(); }

        private void ToggleSwitchEnableAutoSaveStrokes_Toggled(object sender, RoutedEventArgs e)
        { if (!_isLoaded) return; SettingsManager.Settings.Automation.IsEnableAutoSaveStrokes = CardEnableAutoSaveStrokes.IsOn; SettingsManager.SaveSettingsToFile(); SettingsActionHub.OnAutoSaveStrokesChanged(); }

        private void ComboBoxAutoSaveStrokesInterval_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded || ComboBoxAutoSaveStrokesInterval.SelectedItem == null) return;
            var selectedItem = ComboBoxAutoSaveStrokesInterval.SelectedItem as ComboBoxItem;
            if (selectedItem?.Tag != null && int.TryParse(selectedItem.Tag.ToString(), out int intervalMinutes))
            {
                SettingsManager.Settings.Automation.AutoSaveStrokesIntervalMinutes = intervalMinutes;
                SettingsManager.SaveSettingsToFile();
            }
        }

        private void ToggleSwitchAutoDelSavedFiles_Toggled(object sender, RoutedEventArgs e)
        { if (!_isLoaded) return; SettingsManager.Settings.Automation.AutoDelSavedFiles = CardAutoDelSavedFiles.IsOn; SettingsManager.SaveSettingsToFile(); }

        private void ComboBoxAutoDelSavedFilesDaysThreshold_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Automation.AutoDelSavedFilesDaysThreshold =
                int.Parse(((ComboBoxItem)ComboBoxAutoDelSavedFilesDaysThreshold.SelectedItem).Content.ToString());
            SettingsManager.SaveSettingsToFile();
        }

        private void SideControlMinimumAutomationSlider_ValueChanged(object sender, RoutedEventArgs e)
        { if (!_isLoaded) return; SettingsManager.Settings.Automation.MinimumAutomationStrokeNumber = (int)SideControlMinimumAutomationSlider.Value; SettingsManager.SaveSettingsToFile(); }

        private void ToggleSwitchSaveFullPageStrokes_Toggled(object sender, RoutedEventArgs e)
        { if (!_isLoaded) return; SettingsManager.Settings.Automation.IsSaveFullPageStrokes = CardSaveFullPageStrokes.IsOn; SettingsManager.SaveSettingsToFile(); }

        private void ToggleSwitchUseCustomSaveFileName_Toggled(object sender, RoutedEventArgs e)
        { if (!_isLoaded) return; SettingsManager.Settings.Automation.IsUseCustomSaveFileName = CardUseCustomSaveFileName.IsOn; SettingsManager.SaveSettingsToFile(); }

        private void TextBoxCustomSaveFileNameTemplate_LostFocus(object sender, RoutedEventArgs e)
        { if (!_isLoaded) return; SettingsManager.Settings.Automation.CustomSaveFileNameTemplate = TextBoxCustomSaveFileNameTemplate.Text; SettingsManager.SaveSettingsToFile(); }

        private void ComboBoxSaveFileNamePreset_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded || ComboBoxSaveFileNamePreset.SelectedItem == null) return;
            var item = ComboBoxSaveFileNamePreset.SelectedItem as ComboBoxItem;
            var tag = item?.Tag?.ToString();
            if (string.IsNullOrEmpty(tag)) return;

            if (tag == "__custom__")
            {
                CardCustomSaveFileNameTemplate.Visibility = Visibility.Visible;
                return;
            }

            CardCustomSaveFileNameTemplate.Visibility = Visibility.Collapsed;
            SettingsManager.Settings.Automation.CustomSaveFileNameTemplate = tag;
            TextBoxCustomSaveFileNameTemplate.Text = tag;
            SettingsManager.SaveSettingsToFile();
        }

        private void SyncSaveFileNamePresetSelection(string template)
        {
            int matchedIndex = -1;
            for (int i = 0; i < ComboBoxSaveFileNamePreset.Items.Count; i++)
            {
                var item = ComboBoxSaveFileNamePreset.Items[i] as ComboBoxItem;
                var tag = item?.Tag?.ToString();
                if (tag == template) { matchedIndex = i; break; }
            }

            if (matchedIndex >= 0)
            {
                ComboBoxSaveFileNamePreset.SelectedIndex = matchedIndex;
                CardCustomSaveFileNameTemplate.Visibility = Visibility.Collapsed;
            }
            else
            {
                ComboBoxSaveFileNamePreset.SelectedIndex = ComboBoxSaveFileNamePreset.Items.Count - 1;
                CardCustomSaveFileNameTemplate.Visibility = Visibility.Visible;
            }
        }

        #endregion
    }
}