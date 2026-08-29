using GongSolutions.Wpf.DragDrop;
using Ink_Canvas.Controls.Toolbar;
using Ink_Canvas.Controls.Toolbar.BoardToolbar;
using Ink_Canvas.Helpers;
using Ink_Canvas.Properties;
using Ink_Canvas.Windows.SettingsViews.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using ContentDialog = iNKORE.UI.WPF.Modern.Controls.ContentDialog;
using ContentDialogButton = iNKORE.UI.WPF.Modern.Controls.ContentDialogButton;
using ContentDialogResult = iNKORE.UI.WPF.Modern.Controls.ContentDialogResult;
using MessageBox = iNKORE.UI.WPF.Modern.Controls.MessageBox;
using Page = iNKORE.UI.WPF.Modern.Controls.Page;

namespace Ink_Canvas.Windows.SettingsViews.Pages
{
    public partial class BoardPage : Page, IDropTarget
    {
        private static readonly string LogTag = "BoardPage";
        private bool _isLoaded;
        private bool _suppressConfigChange;
        private bool _suppressSave;
        private bool _suppressChickenSoupSourceSelectionChanged;

        #region Toolbar state
        private BoardToolbarLayoutSettings _currentLayout;
        private string _currentAreaId = "center";
        public ObservableCollection<BoardToolbarGroupEntry> AreaGroups { get; } = new();
        public IDropTarget GroupDropHandler { get; }
        public IDropTarget GroupListDropHandler { get; }
        public IReadOnlyList<IBoardToolbarItem> AvailableItems => BoardToolbarRegistry.Discover();

        public static readonly DependencyProperty SelectedEntryProperty =
            DependencyProperty.Register(nameof(SelectedEntry), typeof(BoardToolbarComponentEntry), typeof(BoardPage),
                new PropertyMetadata(null, OnSelectedEntryChanged));

        public BoardToolbarComponentEntry SelectedEntry
        {
            get => (BoardToolbarComponentEntry)GetValue(SelectedEntryProperty);
            set => SetValue(SelectedEntryProperty, value);
        }

        private static void OnSelectedEntryChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var page = (BoardPage)d;
            page.UpdatePropertiesPanel();
        }

        public static readonly DependencyProperty SettingsTabIndexProperty =
            DependencyProperty.Register(nameof(SettingsTabIndex), typeof(int), typeof(BoardPage),
                new PropertyMetadata(0));

        public int SettingsTabIndex
        {
            get => (int)GetValue(SettingsTabIndexProperty);
            set => SetValue(SettingsTabIndexProperty, value);
        }
        #endregion

        #region Menu state
        private bool _suppressMenuSave;
        public ObservableCollection<string> AddedMenuItems { get; } = new ObservableCollection<string>();
        #endregion

        public BoardPage()
        {
            GroupDropHandler = new BoardPageGroupChildrenDropHandler(this);
            GroupListDropHandler = new BoardPageGroupListDropHandler(this);
            InitializeComponent();
            DataContext = this;
            Loaded += OnPageLoaded;
            Unloaded += OnPageUnloaded;
        }

        private void OnPageLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                LogHelper.WriteLogToFile($"{LogTag}: OnPageLoaded 开始", LogHelper.LogType.Info);
                LoadToolbarSettings();
                LoadAppearanceSettings();
                LoadMenuSettings();
                RadioAreaCenter.IsChecked = true;
                _currentAreaId = "center";
                RefreshAreaPanel();
                UpdateAllSliderTexts();
                SliderTouchHelper.AddTouchSupportToAllSliders(this);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"{LogTag}: OnPageLoaded 异常: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}", LogHelper.LogType.Error);
            }
            _isLoaded = true;
        }

        private void OnPageUnloaded(object sender, RoutedEventArgs e)
        {
            _isLoaded = false;
        }

        // =====================================================
        // Toolbar: Config file management
        // =====================================================

        private void RefreshConfigFileList()
        {
            _suppressConfigChange = true;
            ComboBoxConfigFile.Items.Clear();
            var files = BoardToolbarRegistry.ListConfigFiles();
            foreach (var name in files)
                ComboBoxConfigFile.Items.Add(name);

            var activeName = SettingsManager.Settings?.BoardToolbarConfigName ?? "default";
            var idx = files.IndexOf(activeName);
            ComboBoxConfigFile.SelectedIndex = idx >= 0 ? idx : 0;
            _suppressConfigChange = false;
        }

        private void ComboBoxConfigFile_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressConfigChange || !_isLoaded) return;
            var name = ComboBoxConfigFile.SelectedItem as string;
            if (string.IsNullOrEmpty(name)) return;

            SettingsManager.Settings.BoardToolbarConfigName = name;
            SettingsManager.SaveSettingsToFile();
            LoadToolbarSettings();
            RebuildMainWindowBoardToolbar();
        }

        private void ButtonNewConfig_Click(object sender, RoutedEventArgs e)
        {
            var existing = BoardToolbarRegistry.ListConfigFiles();
            var name = GenerateUniqueConfigName(existing, "Config");
            BoardToolbarRegistry.SaveConfigFile(name, BoardToolbarLayoutSettings.CreateDefault());
            SettingsManager.Settings.BoardToolbarConfigName = name;
            SettingsManager.SaveSettingsToFile();
            RefreshConfigFileList();
            LoadToolbarSettings();
            RebuildMainWindowBoardToolbar();
        }

        private void ButtonDuplicateConfig_Click(object sender, RoutedEventArgs e)
        {
            var currentName = ComboBoxConfigFile.SelectedItem as string;
            if (string.IsNullOrEmpty(currentName)) return;

            var existing = BoardToolbarRegistry.ListConfigFiles();
            var name = GenerateUniqueConfigName(existing, currentName + "_copy");
            var layout = BoardToolbarRegistry.LoadConfigFile(currentName) ?? BoardToolbarLayoutSettings.CreateDefault();
            BoardToolbarRegistry.SaveConfigFile(name, layout);
            SettingsManager.Settings.BoardToolbarConfigName = name;
            SettingsManager.SaveSettingsToFile();
            RefreshConfigFileList();
            LoadToolbarSettings();
            RebuildMainWindowBoardToolbar();
        }

        private static string GenerateUniqueConfigName(IReadOnlyList<string> existing, string baseName)
        {
            if (!existing.Contains(baseName, StringComparer.OrdinalIgnoreCase))
                return baseName;
            for (int i = 2; ; i++)
            {
                var candidate = $"{baseName}{i}";
                if (!existing.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                    return candidate;
            }
        }

        private void ButtonDeleteConfig_Click(object sender, RoutedEventArgs e)
        {
            var name = ComboBoxConfigFile.SelectedItem as string;
            if (string.IsNullOrEmpty(name)) return;

            var files = BoardToolbarRegistry.ListConfigFiles();
            if (files.Count <= 1)
            {
                MessageBox.Show(FloatingBarStrings.ToolbarPage_AtLeastOneConfig, FloatingBarStrings.ToolbarPage_Hint, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (MessageBox.Show($"{FloatingBarStrings.ToolbarPage_ConfirmDeleteConfig} \"{name}\"?", FloatingBarStrings.ToolbarPage_ConfirmDelete,
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            BoardToolbarRegistry.DeleteConfigFile(name);
            if (SettingsManager.Settings.BoardToolbarConfigName == name)
            {
                SettingsManager.Settings.BoardToolbarConfigName = "default";
                SettingsManager.SaveSettingsToFile();
            }
            RefreshConfigFileList();
            LoadToolbarSettings();
            RebuildMainWindowBoardToolbar();
        }

        private void ButtonRefreshConfig_Click(object sender, RoutedEventArgs e)
        {
            RefreshConfigFileList();
            LoadToolbarSettings();
            RebuildMainWindowBoardToolbar();
        }

        private void ButtonOpenConfigFolder_Click(object sender, RoutedEventArgs e)
        {
            var dir = BoardToolbarRegistry.GetConfigDirectory();
            if (!System.IO.Directory.Exists(dir))
                System.IO.Directory.CreateDirectory(dir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            });
        }

        // =====================================================
        // Toolbar: Settings load/save
        // =====================================================

        private void LoadToolbarSettings()
        {
            LogHelper.WriteLogToFile($"{LogTag}: LoadToolbarSettings 开始", LogHelper.LogType.Info);
            SelectedEntry = null;
            AreaGroups.Clear();
            RefreshConfigFileList();
            _currentLayout = BoardToolbarRegistry.LoadActiveConfig();
            RefreshAreaPanel();
            LogHelper.WriteLogToFile($"{LogTag}: LoadToolbarSettings 完成 Areas={_currentLayout?.Areas?.Count ?? 0}", LogHelper.LogType.Info);
        }

        internal void SaveToolbarSettings()
        {
            if (!_isLoaded || _suppressSave) return;
            try
            {
                SyncAreaBack();
                var configName = SettingsManager.Settings?.BoardToolbarConfigName ?? "default";
                BoardToolbarRegistry.SaveConfigFile(configName, _currentLayout);
                LogHelper.WriteLogToFile($"{LogTag}: 配置已保存到 [{configName}]", LogHelper.LogType.Info);
                RebuildMainWindowBoardToolbar();
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"{LogTag}: SaveSettings 异常: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}", LogHelper.LogType.Error);
            }
        }

        private void SyncAreaBack()
        {
            if (_currentLayout == null) return;
            var area = _currentLayout.Areas.FirstOrDefault(a =>
                string.Equals(a.Id, _currentAreaId, StringComparison.OrdinalIgnoreCase));
            if (area == null) return;

            area.Groups = new List<BoardToolbarGroupEntry>(AreaGroups.Select(g =>
            {
                var ng = new BoardToolbarGroupEntry { Id = g.Id };
                ng.Components = new List<BoardToolbarComponentEntry>(g.Components.Select(CloneEntry));
                return ng;
            }));
        }

        private static BoardToolbarComponentEntry CloneEntry(BoardToolbarComponentEntry source)
        {
            var clone = new BoardToolbarComponentEntry { Id = source.Id };
            if (source.Settings != null && source.Settings.Count > 0)
                clone.Settings = new Dictionary<string, object>(source.Settings);
            return clone;
        }

        private void RebuildMainWindowBoardToolbar()
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    var mainWindow = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();
                    mainWindow?.RebuildBoardToolbar();
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile($"{LogTag}: RebuildBoardToolbar 异常: {ex.Message}", LogHelper.LogType.Error);
                }
            }));
        }

        // =====================================================
        // Toolbar: Area management
        // =====================================================

        private void AreaRadioButton_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SyncAreaBack();

            if (RadioAreaLeft.IsChecked == true) _currentAreaId = "left";
            else if (RadioAreaCenter.IsChecked == true) _currentAreaId = "center";
            else if (RadioAreaRight.IsChecked == true) _currentAreaId = "right";

            RefreshAreaPanel();
        }

        private void RefreshAreaPanel()
        {
            AreaGroups.Clear();
            SelectedEntry = null;

            var area = _currentLayout?.Areas?.FirstOrDefault(a =>
                string.Equals(a.Id, _currentAreaId, StringComparison.OrdinalIgnoreCase));
            if (area == null) return;

            foreach (var group in area.Groups)
            {
                var ng = new BoardToolbarGroupEntry { Id = group.Id };
                ng.Components = new List<BoardToolbarComponentEntry>(group.Components.Select(CloneEntry));
                AreaGroups.Add(ng);
            }

            AreaGroupsControl.ItemsSource = AreaGroups;
        }

        internal void RefreshGroupsDisplay()
        {
            AreaGroupsControl.ItemsSource = null;
            AreaGroupsControl.ItemsSource = AreaGroups;
        }

        private BoardToolbarAreaEntry FindOrCreateArea(string areaId)
        {
            var area = _currentLayout?.Areas?.FirstOrDefault(a =>
                string.Equals(a.Id, areaId, StringComparison.OrdinalIgnoreCase));
            if (area == null && _currentLayout != null)
            {
                area = new BoardToolbarAreaEntry { Id = areaId };
                _currentLayout.Areas.Add(area);
            }
            return area;
        }

        // =====================================================
        // Toolbar: Item management
        // =====================================================

        private void RemoveGroupChildItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is BoardToolbarComponentEntry entry)
            {
                foreach (var group in AreaGroups)
                {
                    if (group.Components.Remove(entry))
                    {
                        if (SelectedEntry == entry) SelectedEntry = null;
                        RefreshGroupsDisplay();
                        SaveToolbarSettings();
                        return;
                    }
                }
            }
        }

        private void GroupChildList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ListView listView)
                SettingsListItemHelper.UpdateRemoveButtonVisibility(listView, "BtnRemoveGroupChild");
        }

        private void ToolbarListViewItem_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Control control)
            {
                control.ApplyTemplate();
                if (control.Template.FindName("PressedBackground", control) is FrameworkElement indicator)
                    indicator.Width = 3;
            }
        }

        private void LibraryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ItemsControl itemsControl)
                SettingsListItemHelper.UpdateButtonVisibility(itemsControl, "BtnAddItem");
        }

        private void AddToolbarLibraryItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is IBoardToolbarItem item)
            {
                if (AreaGroups.Count == 0)
                {
                    var group = new BoardToolbarGroupEntry { Id = "default" };
                    AreaGroups.Add(group);
                }
                var entry = new BoardToolbarComponentEntry { Id = item.Id };
                AreaGroups.Last().Components.Add(entry);
                SelectedEntry = entry;
                RefreshGroupsDisplay();
                SaveToolbarSettings();
            }
        }

        private void ButtonAddGroup_Click(object sender, RoutedEventArgs e)
        {
            var group = new BoardToolbarGroupEntry { Id = $"group-{Guid.NewGuid():N}" };
            AreaGroups.Add(group);
            SaveToolbarSettings();
        }

        private void RemoveGroup_Click(object sender, RoutedEventArgs e)
        {
            var group = (sender as FrameworkElement)?.DataContext as BoardToolbarGroupEntry;
            if (group == null) return;
            AreaGroups.Remove(group);
            SaveToolbarSettings();
        }

        private void InsertGroupBelow_Click(object sender, RoutedEventArgs e)
        {
            var group = (sender as FrameworkElement)?.DataContext as BoardToolbarGroupEntry;
            if (group == null) return;

            var newGroup = new BoardToolbarGroupEntry { Id = $"group-{Guid.NewGuid():N}" };
            var index = AreaGroups.IndexOf(group);
            AreaGroups.Insert(index + 1, newGroup);
            SaveToolbarSettings();
        }

        private void MoveGroupUp_Click(object sender, RoutedEventArgs e)
        {
            var group = (sender as FrameworkElement)?.DataContext as BoardToolbarGroupEntry;
            if (group == null) return;
            var index = AreaGroups.IndexOf(group);
            if (index <= 0) return;
            AreaGroups.Move(index, index - 1);
            SaveToolbarSettings();
        }

        private void MoveGroupDown_Click(object sender, RoutedEventArgs e)
        {
            var group = (sender as FrameworkElement)?.DataContext as BoardToolbarGroupEntry;
            if (group == null) return;
            var index = AreaGroups.IndexOf(group);
            if (index < 0 || index >= AreaGroups.Count - 1) return;
            AreaGroups.Move(index, index + 1);
            SaveToolbarSettings();
        }

        // =====================================================
        // Toolbar: Properties panel
        // =====================================================

        private void UpdatePropertiesPanel()
        {
            var entry = SelectedEntry;
            if (entry == null) return;
            _suppressSave = true;

            TextBoxFixedWidth.Text = entry.GetSettingDouble("fixedWidth")?.ToString() ?? "";
            TextBoxFixedHeight.Text = entry.GetSettingDouble("fixedHeight")?.ToString() ?? "";
            TextBoxMinWidth.Text = entry.GetSettingDouble("minWidth")?.ToString() ?? "";
            TextBoxMinHeight.Text = entry.GetSettingDouble("minHeight")?.ToString() ?? "";
            TextBoxFontSize.Text = entry.GetSettingDouble("fontSize")?.ToString() ?? "";
            TextBoxOpacity.Text = entry.GetSettingDouble("opacity")?.ToString() ?? "";
            TextBoxMarginLeft.Text = entry.GetSettingDouble("marginLeft")?.ToString() ?? "";
            TextBoxMarginTop.Text = entry.GetSettingDouble("marginTop")?.ToString() ?? "";
            TextBoxMarginRight.Text = entry.GetSettingDouble("marginRight")?.ToString() ?? "";
            TextBoxMarginBottom.Text = entry.GetSettingDouble("marginBottom")?.ToString() ?? "";

            _suppressSave = false;
        }

        private void ComponentSetting_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isLoaded || SelectedEntry == null || _suppressSave) return;
            WriteComponentSettingsFromUI(SelectedEntry);
            SaveToolbarSettings();
        }

        private void WriteComponentSettingsFromUI(BoardToolbarComponentEntry entry)
        {
            WriteDoubleIfNotEmpty(entry, "fixedWidth", TextBoxFixedWidth.Text);
            WriteDoubleIfNotEmpty(entry, "fixedHeight", TextBoxFixedHeight.Text);
            WriteDoubleIfNotEmpty(entry, "minWidth", TextBoxMinWidth.Text);
            WriteDoubleIfNotEmpty(entry, "minHeight", TextBoxMinHeight.Text);
            WriteDoubleIfNotEmpty(entry, "fontSize", TextBoxFontSize.Text);
            WriteDoubleIfNotEmpty(entry, "opacity", TextBoxOpacity.Text);
            WriteDoubleIfNotEmpty(entry, "marginLeft", TextBoxMarginLeft.Text);
            WriteDoubleIfNotEmpty(entry, "marginTop", TextBoxMarginTop.Text);
            WriteDoubleIfNotEmpty(entry, "marginRight", TextBoxMarginRight.Text);
            WriteDoubleIfNotEmpty(entry, "marginBottom", TextBoxMarginBottom.Text);
        }

        private static void WriteDoubleIfNotEmpty(BoardToolbarComponentEntry entry, string key, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                entry.Settings?.Remove(key);
                return;
            }
            if (double.TryParse(text, out var val))
                entry.SetSetting(key, val);
        }

        private void ButtonResetComponentSettings_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedEntry == null) return;
            SelectedEntry.Settings?.Clear();
            UpdatePropertiesPanel();
            SaveToolbarSettings();
        }

        private void ButtonResetToolbar_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var configName = SettingsManager.Settings?.BoardToolbarConfigName ?? "default";
                BoardToolbarRegistry.SaveConfigFile(configName, BoardToolbarLayoutSettings.CreateDefault());
                SettingsManager.SaveSettingsToFile();
                RebuildMainWindowBoardToolbar();
                LoadToolbarSettings();
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"{LogTag}: ButtonResetToolbar 异常: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}", LogHelper.LogType.Error);
            }
        }

        // =====================================================
        // Appearance: Whiteboard display options
        // =====================================================

        private void LoadAppearanceSettings()
        {
            var settings = SettingsManager.Settings;
            if (settings?.Appearance == null) return;

            CardEnableTimeDisplayInWhiteboardMode.IsOn = settings.Appearance.EnableTimeDisplayInWhiteboardMode;
            CardEnableChickenSoupInWhiteboardMode.IsOn = settings.Appearance.EnableChickenSoupInWhiteboardMode;

            SelectComboBoxItemByTag(ComboBoxChickenSoupPosition, settings.Appearance.ChickenSoupPosition);

            _suppressChickenSoupSourceSelectionChanged = true;
            try
            {
                ComboBoxChickenSoupSource.SelectedIndex = settings.Appearance.ChickenSoupSource;
            }
            finally
            {
                Dispatcher.BeginInvoke(
                    (Action)(() => { _suppressChickenSoupSourceSelectionChanged = false; }),
                    DispatcherPriority.ContextIdle);
            }

            if (BtnHitokotoCustomize != null)
                BtnHitokotoCustomize.Visibility = settings.Appearance.ChickenSoupSource == 3 ? Visibility.Visible : Visibility.Collapsed;

            BoardToolbarLeftOpacitySlider.Value = settings.Appearance.BoardToolbarLeftOpacity;
            BoardToolbarCenterOpacitySlider.Value = settings.Appearance.BoardToolbarCenterOpacity;
            BoardToolbarRightOpacitySlider.Value = settings.Appearance.BoardToolbarRightOpacity;
            BoardMenuOpacitySlider.Value = settings.Appearance.BoardMenuOpacity;

            ViewboxBlackBoardLeftScaleTransformValueSlider.Value = settings.Appearance.ViewboxBlackBoardLeftScaleTransformValue;
            ViewboxBlackBoardCenterScaleTransformValueSlider.Value = settings.Appearance.ViewboxBlackBoardScaleTransformValue;
            ViewboxBlackBoardRightScaleTransformValueSlider.Value = settings.Appearance.ViewboxBlackBoardRightScaleTransformValue;
        }

        private void ToggleSwitchEnableTimeDisplayInWhiteboardMode_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Appearance.EnableTimeDisplayInWhiteboardMode = CardEnableTimeDisplayInWhiteboardMode.IsOn;
            SettingsManager.SaveSettingsToFile();
            SettingsActionHub.OnTimeDisplayInWhiteboardChanged(CardEnableTimeDisplayInWhiteboardMode.IsOn);
        }

        private void ToggleSwitchEnableChickenSoupInWhiteboardMode_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Appearance.EnableChickenSoupInWhiteboardMode = CardEnableChickenSoupInWhiteboardMode.IsOn;
            SettingsManager.SaveSettingsToFile();
            SettingsActionHub.OnChickenSoupInWhiteboardChanged(
                CardEnableChickenSoupInWhiteboardMode.IsOn,
                CardEnableTimeDisplayInWhiteboardMode.IsOn);
        }

        private void ComboBoxChickenSoupSource_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressChickenSoupSourceSelectionChanged || !_isLoaded) return;
            int idx = ComboBoxChickenSoupSource.SelectedIndex;
            if (idx < 0) return;
            if (SettingsManager.Settings.Appearance.ChickenSoupSource == idx) return;
            SettingsManager.Settings.Appearance.ChickenSoupSource = idx;
            if (BtnHitokotoCustomize != null)
                BtnHitokotoCustomize.Visibility = idx == 3 ? Visibility.Visible : Visibility.Collapsed;
            SettingsManager.SaveSettingsToFile();
            SettingsActionHub.OnChickenSoupSourceChanged();
        }

        private void ComboBoxChickenSoupPosition_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded) return;
            var position = GetSelectedComboBoxTag(ComboBoxChickenSoupPosition, "TopRight");
            SettingsManager.Settings.Appearance.ChickenSoupPosition = position;
            SettingsManager.SaveSettingsToFile();
            SettingsActionHub.OnChickenSoupPositionChanged();
        }

        private async void BtnHitokotoCustomize_Click(object sender, RoutedEventArgs e)
        {
            var categories = new Dictionary<string, string>
            {
                { "a", ThemeStrings.Theme_HitokotoCategory_Animation }, { "b", ThemeStrings.Theme_HitokotoCategory_Manga }, { "c", ThemeStrings.Theme_HitokotoCategory_Game }, { "d", ThemeStrings.Theme_HitokotoCategory_Literature },
                { "e", ThemeStrings.Theme_HitokotoCategory_Original }, { "f", ThemeStrings.Theme_HitokotoCategory_FromWeb }, { "g", NotificationStrings.Type_Other }, { "h", ThemeStrings.Theme_HitokotoCategory_Movie },
                { "i", ThemeStrings.Theme_HitokotoCategory_Poetry }, { "j", ThemeStrings.Theme_HitokotoCategory_NeteaseCloud }, { "k", ThemeStrings.Theme_HitokotoCategory_Philosophy }, { "l", ThemeStrings.Theme_HitokotoCategory_Humor }
            };

            var contentPanel = new StackPanel { Margin = new Thickness(20), Orientation = Orientation.Vertical };
            var selectAllCheckBox = new CheckBox { Content = ThemeStrings.Theme_Hitokoto_SelectAll, FontSize = 14, Margin = new Thickness(0, 0, 0, 8) };
            var categoryCheckBoxes = new Dictionary<string, CheckBox>();
            var savedHitokoto = SettingsManager.Settings.Appearance.HitokotoCategories;
            bool implicitAllCategories = savedHitokoto == null || savedHitokoto.Count == 0;

            foreach (var category in categories)
            {
                var checkBox = new CheckBox
                {
                    Content = category.Value,
                    Tag = category.Key,
                    FontSize = 13,
                    IsChecked = implicitAllCategories || savedHitokoto.Contains(category.Key),
                    Margin = new Thickness(0, 0, 0, 8)
                };
                categoryCheckBoxes[category.Key] = checkBox;
                contentPanel.Children.Add(checkBox);
            }

            bool isUpdatingSelectAll = false;
            selectAllCheckBox.IsChecked = implicitAllCategories || savedHitokoto.Count == categories.Count;
            selectAllCheckBox.Checked += (s, args) => { if (isUpdatingSelectAll) return; isUpdatingSelectAll = true; foreach (var cb in categoryCheckBoxes.Values) cb.IsChecked = true; isUpdatingSelectAll = false; };
            selectAllCheckBox.Unchecked += (s, args) => { if (isUpdatingSelectAll) return; isUpdatingSelectAll = true; foreach (var cb in categoryCheckBoxes.Values) cb.IsChecked = false; isUpdatingSelectAll = false; };
            foreach (var cb in categoryCheckBoxes.Values)
            {
                cb.Checked += (s, args) => { if (isUpdatingSelectAll) return; isUpdatingSelectAll = true; selectAllCheckBox.IsChecked = categoryCheckBoxes.Values.All(c => c.IsChecked == true); isUpdatingSelectAll = false; };
                cb.Unchecked += (s, args) => { if (isUpdatingSelectAll) return; isUpdatingSelectAll = true; selectAllCheckBox.IsChecked = false; isUpdatingSelectAll = false; };
            }

            var mainPanel = new StackPanel();
            mainPanel.Children.Add(selectAllCheckBox);
            mainPanel.Children.Add(new Separator { Margin = new Thickness(0, 8, 0, 8) });
            mainPanel.Children.Add(contentPanel);

            var mw = Application.Current.MainWindow as MainWindow;
            var contentDialog = new ContentDialog
            {
                Title = ThemeStrings.Theme_Hitokoto_CustomizeTitle,
                Content = new ScrollViewer { Content = mainPanel, MaxHeight = 400, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
                PrimaryButtonText = CommonStrings.Common_OK,
                SecondaryButtonText = CommonStrings.Common_Cancel,
                DefaultButton = ContentDialogButton.Primary,
                Owner = mw
            };

            var dialogResult = await contentDialog.ShowAsync();
            if (dialogResult == ContentDialogResult.Primary)
            {
                SettingsManager.Settings.Appearance.HitokotoCategories = categoryCheckBoxes.Where(kvp => kvp.Value.IsChecked == true).Select(kvp => kvp.Key).ToList();
                if (SettingsManager.Settings.Appearance.HitokotoCategories.Count == 0)
                    SettingsManager.Settings.Appearance.HitokotoCategories = categories.Keys.ToList();
                SettingsManager.SaveSettingsToFile();
                if (SettingsManager.Settings.Appearance.ChickenSoupSource == 3 && SettingsManager.Settings.Appearance.EnableChickenSoupInWhiteboardMode)
                {
                    SettingsActionHub.OnChickenSoupSourceChanged();
                }
            }
        }

        // =====================================================
        // Appearance: Opacity sliders
        // =====================================================

        private void UpdateAllSliderTexts()
        {
            UpdateSliderText(BoardToolbarLeftOpacitySlider, BoardToolbarLeftOpacityText, "{0:F2}");
            UpdateSliderText(BoardToolbarCenterOpacitySlider, BoardToolbarCenterOpacityText, "{0:F2}");
            UpdateSliderText(BoardToolbarRightOpacitySlider, BoardToolbarRightOpacityText, "{0:F2}");
            UpdateSliderText(BoardMenuOpacitySlider, BoardMenuOpacityText, "{0:F2}");
            UpdateSliderText(ViewboxBlackBoardLeftScaleTransformValueSlider, ViewboxBlackBoardLeftScaleText, "{0:F2}");
            UpdateSliderText(ViewboxBlackBoardCenterScaleTransformValueSlider, ViewboxBlackBoardCenterScaleText, "{0:F2}");
            UpdateSliderText(ViewboxBlackBoardRightScaleTransformValueSlider, ViewboxBlackBoardRightScaleText, "{0:F2}");
        }

        private static void UpdateSliderText(Slider slider, TextBlock textBlock, string format)
        {
            if (slider == null || textBlock == null) return;
            textBlock.Text = string.Format(format, slider.Value);
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

        private void BoardToolbarLeftOpacitySlider_ValueChanged(object sender, RoutedEventArgs e)
        {
            UpdateSliderText(BoardToolbarLeftOpacitySlider, BoardToolbarLeftOpacityText, "{0:F2}");
            if (!_isLoaded) return;
            var val = Math.Round(BoardToolbarLeftOpacitySlider.Value, 2);
            if (BoardToolbarLeftOpacitySlider.Value != val) { BoardToolbarLeftOpacitySlider.Value = val; return; }
            SettingsManager.Settings.Appearance.BoardToolbarLeftOpacity = val;
            SettingsManager.SaveSettingsToFile();
            SettingsActionHub.OnBoardToolbarLeftOpacityChanged(val);
        }

        private void BoardToolbarCenterOpacitySlider_ValueChanged(object sender, RoutedEventArgs e)
        {
            UpdateSliderText(BoardToolbarCenterOpacitySlider, BoardToolbarCenterOpacityText, "{0:F2}");
            if (!_isLoaded) return;
            var val = Math.Round(BoardToolbarCenterOpacitySlider.Value, 2);
            if (BoardToolbarCenterOpacitySlider.Value != val) { BoardToolbarCenterOpacitySlider.Value = val; return; }
            SettingsManager.Settings.Appearance.BoardToolbarCenterOpacity = val;
            SettingsManager.SaveSettingsToFile();
            SettingsActionHub.OnBoardToolbarCenterOpacityChanged(val);
        }

        private void BoardToolbarRightOpacitySlider_ValueChanged(object sender, RoutedEventArgs e)
        {
            UpdateSliderText(BoardToolbarRightOpacitySlider, BoardToolbarRightOpacityText, "{0:F2}");
            if (!_isLoaded) return;
            var val = Math.Round(BoardToolbarRightOpacitySlider.Value, 2);
            if (BoardToolbarRightOpacitySlider.Value != val) { BoardToolbarRightOpacitySlider.Value = val; return; }
            SettingsManager.Settings.Appearance.BoardToolbarRightOpacity = val;
            SettingsManager.SaveSettingsToFile();
            SettingsActionHub.OnBoardToolbarRightOpacityChanged(val);
        }

        private void BoardMenuOpacitySlider_ValueChanged(object sender, RoutedEventArgs e)
        {
            UpdateSliderText(BoardMenuOpacitySlider, BoardMenuOpacityText, "{0:F2}");
            if (!_isLoaded) return;
            var val = Math.Round(BoardMenuOpacitySlider.Value, 2);
            if (BoardMenuOpacitySlider.Value != val) { BoardMenuOpacitySlider.Value = val; return; }
            SettingsManager.Settings.Appearance.BoardMenuOpacity = val;
            SettingsManager.SaveSettingsToFile();
            SettingsActionHub.OnBoardMenuOpacityChanged(val);
        }

        // =====================================================
        // Appearance: Scale sliders
        // =====================================================

        private void ViewboxBlackBoardLeftScaleTransformValueSlider_ValueChanged(object sender, RoutedEventArgs e)
        {
            UpdateSliderText(ViewboxBlackBoardLeftScaleTransformValueSlider, ViewboxBlackBoardLeftScaleText, "{0:F2}");
            if (!_isLoaded) return;
            var val = Math.Round(ViewboxBlackBoardLeftScaleTransformValueSlider.Value, 2);
            if (ViewboxBlackBoardLeftScaleTransformValueSlider.Value != val) { ViewboxBlackBoardLeftScaleTransformValueSlider.Value = val; return; }
            SettingsManager.Settings.Appearance.ViewboxBlackBoardLeftScaleTransformValue = val;
            SettingsManager.SaveSettingsToFile();
            SettingsActionHub.OnBlackBoardLeftScaleChanged(val);
        }

        private void ViewboxBlackBoardCenterScaleTransformValueSlider_ValueChanged(object sender, RoutedEventArgs e)
        {
            UpdateSliderText(ViewboxBlackBoardCenterScaleTransformValueSlider, ViewboxBlackBoardCenterScaleText, "{0:F2}");
            if (!_isLoaded) return;
            var val = Math.Round(ViewboxBlackBoardCenterScaleTransformValueSlider.Value, 2);
            if (ViewboxBlackBoardCenterScaleTransformValueSlider.Value != val) { ViewboxBlackBoardCenterScaleTransformValueSlider.Value = val; return; }
            SettingsManager.Settings.Appearance.ViewboxBlackBoardScaleTransformValue = val;
            SettingsManager.SaveSettingsToFile();
            SettingsActionHub.OnBlackBoardScaleChanged(val);
        }

        private void ViewboxBlackBoardRightScaleTransformValueSlider_ValueChanged(object sender, RoutedEventArgs e)
        {
            UpdateSliderText(ViewboxBlackBoardRightScaleTransformValueSlider, ViewboxBlackBoardRightScaleText, "{0:F2}");
            if (!_isLoaded) return;
            var val = Math.Round(ViewboxBlackBoardRightScaleTransformValueSlider.Value, 2);
            if (ViewboxBlackBoardRightScaleTransformValueSlider.Value != val) { ViewboxBlackBoardRightScaleTransformValueSlider.Value = val; return; }
            SettingsManager.Settings.Appearance.ViewboxBlackBoardRightScaleTransformValue = val;
            SettingsManager.SaveSettingsToFile();
            SettingsActionHub.OnBlackBoardRightScaleChanged(val);
        }

        // =====================================================
        // Menu: Settings
        // =====================================================

        private void LoadMenuSettings()
        {
            var layout = ToolsMenuRegistry.LoadBoardConfig();
            AddedMenuItems.Clear();
            foreach (var id in layout.BoardItems)
                AddedMenuItems.Add(id);
            RefreshMenuLibraryList();
            MenuAddedList.ItemsSource = AddedMenuItems;
            MenuLibraryList.ItemsSource = ToolsMenuRegistry.BoardAvailableItems;
        }

        private void SaveMenuSettings()
        {
            if (_suppressMenuSave) return;
            var layout = new ToolsMenuLayoutSettings
            {
                BoardItems = AddedMenuItems.ToList()
            };
            ToolsMenuRegistry.SaveBoardConfig(layout);
        }

        private void RefreshMenuLibraryList()
        {
            var addedSet = new HashSet<string>(AddedMenuItems);
            var available = ToolsMenuRegistry.BoardAvailableItems
                .Where(i => !addedSet.Contains(i.Id))
                .ToList();
            MenuLibraryList.ItemsSource = available;
        }

        private void AddMenuLibraryItem_Click(object sender, RoutedEventArgs e)
        {
            if (AddedMenuItems.Count >= 9)
            {
                MessageBox.Show("最多只能添加 9 个菜单项。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (sender is FrameworkElement fe && fe.DataContext is ToolsMenuItemInfo item)
            {
                AddedMenuItems.Add(item.Id);
                RefreshMenuLibraryList();
                SaveMenuSettings();
            }
        }

        private void RemoveMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is string id)
            {
                AddedMenuItems.Remove(id);
                RefreshMenuLibraryList();
                SaveMenuSettings();
            }
        }

        private void MenuAddedList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SettingsListItemHelper.UpdateRemoveButtonVisibility(MenuAddedList, "BtnRemoveItem");
        }

        private void MenuLibraryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ItemsControl itemsControl)
                SettingsListItemHelper.UpdateButtonVisibility(itemsControl, "BtnAddMenuItem");
        }

        private void MenuListViewItem_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Control control)
            {
                control.ApplyTemplate();
                if (control.Template.FindName("PressedBackground", control) is FrameworkElement indicator)
                    indicator.Width = 3;
            }
        }

        private void ButtonResetMenu_Click(object sender, RoutedEventArgs e)
        {
            var layout = ToolsMenuRegistry.CreateDefaultBoardLayout();
            _suppressMenuSave = true;
            AddedMenuItems.Clear();
            foreach (var id in layout.BoardItems)
                AddedMenuItems.Add(id);
            _suppressMenuSave = false;
            RefreshMenuLibraryList();
            SaveMenuSettings();
        }

        // =====================================================
        // Menu: Drag-drop (IDropTarget)
        // =====================================================

        public new void DragEnter(IDropInfo dropInfo) { }
        public new void DragLeave(IDropInfo dropInfo) { }
        public void DropHint(IDropHintInfo dropHintInfo) { }

        public new void DragOver(IDropInfo dropInfo)
        {
            if (dropInfo.Data is ToolsMenuItemInfo)
            {
                dropInfo.DropTargetAdorner = DropTargetAdorners.Insert;
                dropInfo.Effects = DragDropEffects.Copy;
            }
            else if (dropInfo.Data is string)
            {
                dropInfo.DropTargetAdorner = DropTargetAdorners.Insert;
                dropInfo.Effects = DragDropEffects.Move;
            }
        }

        public new void Drop(IDropInfo dropInfo)
        {
            if (dropInfo.Data is ToolsMenuItemInfo item)
            {
                if (AddedMenuItems.Count >= 9) return;
                var insertIndex = dropInfo.UnfilteredInsertIndex;
                if (insertIndex < 0 || insertIndex > AddedMenuItems.Count)
                    insertIndex = AddedMenuItems.Count;
                AddedMenuItems.Insert(insertIndex, item.Id);
                RefreshMenuLibraryList();
                SaveMenuSettings();
            }
            else if (dropInfo.Data is string id)
            {
                var oldIndex = AddedMenuItems.IndexOf(id);
                if (oldIndex == -1) return;
                var newIndex = dropInfo.UnfilteredInsertIndex;
                if (oldIndex < newIndex) newIndex--;
                newIndex = Math.Max(0, Math.Min(newIndex, AddedMenuItems.Count - 1));
                if (oldIndex != newIndex)
                    AddedMenuItems.Move(oldIndex, newIndex);
                SaveMenuSettings();
            }
        }
    }

    // BoardPage-specific drop handlers (accept BoardPage instead of BoardToolbarPage)
    internal class BoardPageGroupChildrenDropHandler : IDropTarget
    {
        private readonly BoardPage _page;
        public BoardPageGroupChildrenDropHandler(BoardPage page) { _page = page; }
        public void DragEnter(IDropInfo dropInfo) { }
        public void DragLeave(IDropInfo dropInfo) { }
        public void DropHint(IDropHintInfo dropHintInfo) { }
        public void DragOver(IDropInfo dropInfo)
        {
            if (dropInfo.Data is IBoardToolbarItem || dropInfo.Data is BoardToolbarComponentEntry)
            {
                dropInfo.DropTargetAdorner = DropTargetAdorners.Insert;
                dropInfo.Effects = DragDropEffects.Move;
            }
        }
        public void Drop(IDropInfo dropInfo)
        {
            var group = (dropInfo.VisualTarget as FrameworkElement)?.DataContext as BoardToolbarGroupEntry;
            if (group == null) return;
            if (dropInfo.Data is IBoardToolbarItem item)
            {
                var entry = new BoardToolbarComponentEntry { Id = item.Id };
                var insertIndex = dropInfo.UnfilteredInsertIndex;
                if (insertIndex < 0 || insertIndex > group.Components.Count) insertIndex = group.Components.Count;
                group.Components.Insert(insertIndex, entry);
                _page.SelectedEntry = entry;
                _page.RefreshGroupsDisplay();
                _page.SaveToolbarSettings();
            }
            else if (dropInfo.Data is BoardToolbarComponentEntry vm)
            {
                var oldIndex = group.Components.IndexOf(vm);
                if (oldIndex == -1) return;
                var newIndex = dropInfo.UnfilteredInsertIndex;
                if (oldIndex < newIndex) newIndex--;
                newIndex = Math.Max(0, Math.Min(newIndex, group.Components.Count - 1));
                if (oldIndex != newIndex)
                {
                    group.Components.RemoveAt(oldIndex);
                    group.Components.Insert(newIndex, vm);
                }
                _page.RefreshGroupsDisplay();
                _page.SaveToolbarSettings();
            }
        }
    }

    internal class BoardPageGroupListDropHandler : IDropTarget
    {
        private readonly BoardPage _page;
        public BoardPageGroupListDropHandler(BoardPage page) { _page = page; }
        public void DragEnter(IDropInfo dropInfo) { }
        public void DragLeave(IDropInfo dropInfo) { }
        public void DropHint(IDropHintInfo dropHintInfo) { }
        public void DragOver(IDropInfo dropInfo)
        {
            if (dropInfo.Data is BoardToolbarGroupEntry)
            {
                dropInfo.DropTargetAdorner = DropTargetAdorners.Insert;
                dropInfo.Effects = DragDropEffects.Move;
            }
        }
        public void Drop(IDropInfo dropInfo)
        {
            if (!(dropInfo.Data is BoardToolbarGroupEntry group)) return;
            var oldIndex = _page.AreaGroups.IndexOf(group);
            if (oldIndex == -1) return;
            var newIndex = dropInfo.UnfilteredInsertIndex;
            if (oldIndex < newIndex) newIndex--;
            newIndex = Math.Max(0, Math.Min(newIndex, _page.AreaGroups.Count - 1));
            if (oldIndex != newIndex)
            {
                _page.AreaGroups.Move(oldIndex, newIndex);
                _page.SaveToolbarSettings();
            }
        }
    }
}
