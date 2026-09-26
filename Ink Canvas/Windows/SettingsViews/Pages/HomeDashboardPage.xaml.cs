using Ink_Canvas.Helpers;
using Ink_Canvas.Properties;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Ink_Canvas.Windows.SettingsViews.Pages
{
    public partial class HomeDashboardPage
    {
        private sealed class ShortcutEntry
        {
            public string Name { get; set; }
            public string Label { get; set; }
            public string[] Keys { get; set; }
            public string[] DefaultKeys { get; set; }
        }

        public HomeDashboardPage()
        {
            InitializeComponent();
            Loaded += HomeDashboardPage_Loaded;
            DashboardBodyGrid.SizeChanged += (s, e) => ApplyResponsiveLayout(e.NewSize.Width);
        }

        private void HomeDashboardPage_Loaded(object sender, RoutedEventArgs e)
        {
            ApplyVersion();
            LoadShortcutEntries();
            RebuildFavourites();
            ApplyResponsiveLayout(DashboardBodyGrid.ActualWidth);
        }

        private void ApplyVersion()
        {
            try
            {
                var version = Assembly.GetExecutingAssembly().GetName().Version;
                if (version != null)
                {
                    AppVersionText.Text = version.Major + "." + version.Minor + "." + version.Build + "." + version.Revision;
                    var informationalVersion = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                    if (informationalVersion != null)
                    {
                        string infoVersion = informationalVersion.InformationalVersion;
                        int lastDotIndex = infoVersion.LastIndexOf('.');
                        if (lastDotIndex >= 0 && lastDotIndex < infoVersion.Length - 7)
                        {
                            AppVersionText.Text += " (" + infoVersion.Substring(lastDotIndex + 1) + ")";
                        }
                    }
                }
            }
            catch { }
        }

        private void LoadShortcutEntries()
        {
            var configured = new Dictionary<string, (Key key, ModifierKeys modifiers)>();
            var manager = GetHotkeyManager();
            if (manager != null)
            {
                foreach (var hotkey in manager.GetHotkeysFromConfigFile())
                {
                    if (!string.IsNullOrEmpty(hotkey.Name))
                        configured[hotkey.Name] = (hotkey.Key, hotkey.Modifiers);
                }
            }

            var all = new List<ShortcutEntry>
            {
                new ShortcutEntry { Name = "Undo", Label = FloatingBarStrings.Board_Undo, DefaultKeys = new[] { "Ctrl", "Z" } },
                new ShortcutEntry { Name = "Redo", Label = FloatingBarStrings.Board_Redo, DefaultKeys = new[] { "Ctrl", "Y" } },
                new ShortcutEntry { Name = "Clear", Label = FloatingBarStrings.FloatingBar_Clear, DefaultKeys = new[] { "Ctrl", "E" } },
                new ShortcutEntry { Name = "SelectTool", Label = HotkeyStrings.Hotkey_SelectTool, DefaultKeys = new[] { "Alt", "S" } },
                new ShortcutEntry { Name = "DrawTool", Label = HotkeyStrings.Hotkey_DrawTool, DefaultKeys = new[] { "Alt", "D" } },
                new ShortcutEntry { Name = "EraserTool", Label = HotkeyStrings.Hotkey_EraserTool, DefaultKeys = new[] { "Alt", "E" } },
                new ShortcutEntry { Name = "BlackboardTool", Label = HotkeyStrings.Hotkey_BlackboardTool, DefaultKeys = new[] { "Alt", "B" } },
                new ShortcutEntry { Name = "Screenshot", Label = FloatingBarStrings.Board_Screenshot, DefaultKeys = new[] { "Alt", "C" } },
                new ShortcutEntry { Name = "QuickDraw", Label = HotkeyStrings.Hotkey_QuickDraw, DefaultKeys = new[] { "Alt", "K" } },
                new ShortcutEntry { Name = "Hide", Label = FloatingBarStrings.FloatingBar_Hide, DefaultKeys = new[] { "Alt", "V" } },
            };

            foreach (var entry in all)
            {
                entry.Keys = configured.TryGetValue(entry.Name, out var hotkey)
                    ? FormatKeys(hotkey.key, hotkey.modifiers)
                    : entry.DefaultKeys;
            }

            ShortcutItemsControl.ItemsSource = all.GetRange(0, Math.Min(6, all.Count));
        }

        private static GlobalHotkeyManager GetHotkeyManager()
        {
            var mainWindow = Application.Current?.MainWindow as MainWindow;
            if (mainWindow == null) return null;
            var field = typeof(MainWindow).GetField("_globalHotkeyManager",
                BindingFlags.NonPublic | BindingFlags.Instance);
            return field?.GetValue(mainWindow) as GlobalHotkeyManager;
        }

        private static string[] FormatKeys(Key key, ModifierKeys modifiers)
        {
            var keys = new List<string>();
            if ((modifiers & ModifierKeys.Control) != 0) keys.Add("Ctrl");
            if ((modifiers & ModifierKeys.Shift) != 0) keys.Add("Shift");
            if ((modifiers & ModifierKeys.Alt) != 0) keys.Add("Alt");
            if ((modifiers & ModifierKeys.Windows) != 0) keys.Add("Win");
            keys.Add(FormatKey(key));
            return keys.ToArray();
        }

        private static string FormatKey(Key key)
        {
            if (key >= Key.D0 && key <= Key.D9) return (key - Key.D0).ToString();
            if (key == Key.Escape) return "Esc";
            return key.ToString();
        }

        private void RebuildFavourites()
        {
            if (FavouritesList == null || FavouritesEmpty == null) return;

            FavouritesList.Children.Clear();

            var settingsWindow = Window.GetWindow(this) as SettingsWindow;
            var entries = settingsWindow?.GetFavouriteEntries() ?? new List<SettingsWindow.FavouriteEntry>();

            FavouritesEmpty.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            var rowStyle = TryFindResource("FavouriteRowStyle") as Style;
            foreach (var entry in entries)
            {
                var row = new Border { Tag = entry.PropertyPath };
                if (rowStyle != null) row.Style = rowStyle;
                row.MouseLeftButtonUp += FavouriteRow_Click;

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var header = new TextBlock
                {
                    Text = entry.Header,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextWrapping = TextWrapping.Wrap
                };
                Grid.SetColumn(header, 0);

                var located = new TextBlock
                {
                    Text = string.Format(NavStrings.Nav_Favourites_LocatedIn, entry.PageTitle),
                    FontSize = 12,
                    Margin = new Thickness(12, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = (Brush)TryFindResource("TextFillColorSecondaryBrush")
                };
                Grid.SetColumn(located, 1);

                grid.Children.Add(header);
                grid.Children.Add(located);
                row.Child = grid;

                FavouritesList.Children.Add(row);
            }
        }

        private void FavouriteRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is string propertyPath)
            {
                (Window.GetWindow(this) as SettingsWindow)?.NavigateToFavourite(propertyPath);
            }
        }

        private void ApplyResponsiveLayout(double width)
        {
            if (LeftColumn == null || RightColumn == null || FavouritesCard == null) return;

            bool narrow = width > 0 && width < 620;
            if (narrow)
            {
                RightColumn.MinWidth = 0;
                RightColumn.Width = new GridLength(0);
                Grid.SetRow(FavouritesCard, 1);
                Grid.SetColumn(FavouritesCard, 0);
                Grid.SetColumnSpan(FavouritesCard, 2);
                FavouritesCard.Margin = new Thickness(0, 16, 0, 0);
            }
            else
            {
                RightColumn.MinWidth = 400;
                RightColumn.Width = GridLength.Auto;
                Grid.SetRow(FavouritesCard, 0);
                Grid.SetColumn(FavouritesCard, 1);
                Grid.SetColumnSpan(FavouritesCard, 1);
                FavouritesCard.Margin = new Thickness(16, 0, 0, 0);
            }
        }

        private void HomeBanner_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            (Window.GetWindow(this) as SettingsWindow)?.NavigateToPage("AboutPage");
        }

        private void BtnBackToOldUI_Click(object sender, RoutedEventArgs e)
        {
            (Window.GetWindow(this) as SettingsWindow)?.NavigateToPage("HomePage");
        }

        private void QuickNavCard_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is string pageTag)
            {
                var settingsWindow = Window.GetWindow(this) as SettingsWindow;
                settingsWindow?.NavigateToPage(pageTag);
            }
        }
    }
}
