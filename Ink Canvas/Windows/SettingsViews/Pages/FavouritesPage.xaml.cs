using Ink_Canvas.Properties;
using Ink_Canvas.Windows.SettingsViews.Helpers;
using System.Windows;
using System.Windows.Controls;

namespace Ink_Canvas.Windows.SettingsViews.Pages
{
    public partial class FavouritesPage : iNKORE.UI.WPF.Modern.Controls.Page
    {
        public FavouritesPage()
        {
            InitializeComponent();
            Loaded += FavouritesPage_Loaded;
        }

        private void FavouritesPage_Loaded(object sender, RoutedEventArgs e)
        {
            RebuildFavourites();
        }

        private void RebuildFavourites()
        {
            var settingsWindow = Window.GetWindow(this) as SettingsWindow;
            var entries = settingsWindow?.GetFavouriteEntries() ?? new System.Collections.Generic.List<SettingsWindow.FavouriteEntry>();

            if (entries.Count == 0)
            {
                EmptyHint.Visibility = Visibility.Visible;
            }
            else
            {
                EmptyHint.Visibility = Visibility.Collapsed;
            }

            // 移除旧的动态卡片（保留静态头部与空提示）
            var toRemove = new System.Collections.Generic.List<UIElement>();
            foreach (var child in FavouritesRoot.Children)
            {
                if (child is iNKORE.UI.WPF.Modern.Controls.SettingsCard card && card.Tag is string tag && tag == "FavouriteEntry")
                {
                    toRemove.Add((UIElement)child);
                }
            }
            foreach (var el in toRemove)
            {
                FavouritesRoot.Children.Remove(el);
            }

            foreach (var entry in entries)
            {
                var card = new iNKORE.UI.WPF.Modern.Controls.SettingsCard
                {
                    Header = entry.Header,
                    Description = string.Format(NavStrings.Nav_Favourites_LocatedIn, entry.PageTitle),
                    IsClickEnabled = true,
                    Tag = "FavouriteEntry",
                };
                card.HeaderIcon = new iNKORE.UI.WPF.Modern.Controls.FontIcon
                {
                    Icon = iNKORE.UI.WPF.Modern.Common.IconKeys.SegoeFluentIcons.FavoriteStar,
                };
                string path = entry.PropertyPath;
                card.Click += (s, args) =>
                {
                    var win = Window.GetWindow(this) as SettingsWindow;
                    win?.NavigateToFavourite(path);
                };

                int index = FavouritesRoot.Children.IndexOf(EmptyHint);
                FavouritesRoot.Children.Insert(index >= 0 ? index : FavouritesRoot.Children.Count, card);
            }
        }
    }
}
