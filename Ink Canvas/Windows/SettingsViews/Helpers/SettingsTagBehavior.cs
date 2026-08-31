using Ink_Canvas.Controls;
using Ink_Canvas.Helpers;
using Ink_Canvas.Properties;
using Ink_Canvas.Windows.SettingsViews.Helpers;
using iNKORE.UI.WPF.Modern.Common.IconKeys;
using iNKORE.UI.WPF.Modern.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using FontIcon = iNKORE.UI.WPF.Modern.Controls.FontIcon;
using SettingsCard = iNKORE.UI.WPF.Modern.Controls.SettingsCard;

namespace Ink_Canvas.Windows.SettingsViews.Helpers
{
    /// <summary>
    /// 设置项 tag 行为：在卡片标题右侧注入彩色 chip 徽章 + 收藏星标。
    /// 身份由页面级遍历统一解析（见 EnumerateCardIdentities），tag 从 [SettingsTag] 特性自动解析。
    /// </summary>
    public static class SettingsTags
    {
        public static readonly DependencyProperty PropertyPathProperty =
            DependencyProperty.RegisterAttached(
                "PropertyPath",
                typeof(string),
                typeof(SettingsTags),
                new PropertyMetadata(null));

        public static string GetPropertyPath(DependencyObject obj) =>
            (string)obj.GetValue(PropertyPathProperty);

        public static void SetPropertyPath(DependencyObject obj, string value) =>
            obj.SetValue(PropertyPathProperty, value);

        /// <summary>
        /// 页内所有可收藏卡片及其身份。身份优先级：
        /// 1. 显式附加属性 PropertyPath（真属性路径，带 tag / chip）
        /// 2. 卡片 x:Name → "{pageTag}:{xName}"
        /// 3. 兜底 → "{pageTag}:card{序号}"（序号 = 页面逻辑树遍历序，注意卡片重排会导致收藏错位）
        /// 遍历顺序与 SettingsWindow.CollectEntriesFromPage 完全一致，保证两侧序号一致。
        /// </summary>
        public static List<KeyValuePair<FrameworkElement, string>> EnumerateCardIdentities(DependencyObject root, string pageTag)
        {
            var result = new List<KeyValuePair<FrameworkElement, string>>();
            if (root == null) return result;

            int ordinal = 0;
            foreach (var node in EnumerateLogicalDescendants(root))
            {
                FrameworkElement element;
                if (node is LabeledSettingsCard lsc)
                {
                    element = lsc;
                }
                else if (node is SettingsCard sc && !HasAncestor<LabeledSettingsCard>(node))
                {
                    element = sc;
                }
                else
                {
                    continue;
                }

                string identity = GetIdentity(element, pageTag, ordinal);
                result.Add(new KeyValuePair<FrameworkElement, string>(element, identity));
                ordinal++;
            }
            return result;
        }

        /// <summary>
        /// 为页面全部卡片注入星标（chip 仅当身份解析出真实 tag 时显示）。
        /// </summary>
        public static void InjectStarsIntoPage(FrameworkElement pageRoot, string pageTag)
        {
            if (pageRoot == null) return;
            foreach (var pair in EnumerateCardIdentities(pageRoot, pageTag))
            {
                Inject(pair.Key, pair.Value);
            }
        }

        private static string GetIdentity(FrameworkElement element, string pageTag, int ordinal)
        {
            string explicitPath = GetPropertyPath(element);
            if (!string.IsNullOrWhiteSpace(explicitPath)) return explicitPath.Trim();
            if (!string.IsNullOrEmpty(element.Name)) return pageTag + ":" + element.Name;
            return pageTag + ":card" + ordinal;
        }

        private static void Inject(FrameworkElement element, string identity)
        {
            // 定位内层 SettingsCard（LabeledSettingsCard 包裹了一个 SettingsCard）
            var card = element as SettingsCard ?? FindVisualChild<SettingsCard>(element);
            if (card == null) return;

            // 强制应用模板，覆盖折叠展开器内尚未实化的卡片
            try { card.ApplyTemplate(); } catch { }

            SettingsTag tags = SettingsTagResolver.GetTags(identity);
            bool isFavourite = SettingsTagResolver.IsFavourite(identity);

            var headerPresenter = card.Template?.FindName("PART_HeaderPresenter", card) as FrameworkElement;
            if (headerPresenter == null) return;
            var headerPanel = VisualTreeHelper.GetParent(headerPresenter) as Panel;
            if (headerPanel == null) return;

            Border favouriteChip = null;
            var headerRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
            };

            // 从垂直 HeaderPanel 中取出标题 presenter，放入水平行
            headerPanel.Children.Remove(headerPresenter);
            headerRow.Children.Add(headerPresenter);

            var chips = BuildChips(tags, identity, out favouriteChip);
            if (chips.Children.Count > 0)
            {
                headerRow.Children.Add(chips);
            }

            var star = BuildStarButton(identity, isFavourite, favouriteChip);
            if (star != null)
            {
                headerRow.Children.Add(star);
            }

            headerPanel.Children.Insert(0, headerRow);
        }

        private static StackPanel BuildChips(SettingsTag tags, string identity, out Border favouriteChip)
        {
            favouriteChip = null;
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };

            void Add(SettingsTag tag, string text, Color color)
            {
                if (!tags.HasFlag(tag)) return;
                panel.Children.Add(CreateChip(text, color));
            }

            Add(SettingsTag.Warn, CommonStrings.SettingsTag_Warn, Color.FromRgb(0xF7, 0x63, 0x0C));
            Add(SettingsTag.New, CommonStrings.SettingsTag_New, Color.FromRgb(0x00, 0x78, 0xD4));
            Add(SettingsTag.Experimental, CommonStrings.SettingsTag_Experimental, Color.FromRgb(0x8A, 0x5C, 0xF6));
            Add(SettingsTag.Secret, CommonStrings.SettingsTag_Secret, Color.FromRgb(0x6E, 0x6E, 0x6E));

            // 收藏 chip 始终创建，按收藏状态显隐
            favouriteChip = CreateChip(CommonStrings.SettingsTag_Favourite, Color.FromRgb(0xFF, 0xB9, 0x00));
            favouriteChip.Visibility = SettingsTagResolver.IsFavourite(identity) ? Visibility.Visible : Visibility.Collapsed;
            panel.Children.Add(favouriteChip);

            return panel;
        }

        private static Border CreateChip(string text, Color color)
        {
            return new Border
            {
                Background = new SolidColorBrush(color),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(4, 1, 4, 1),
                Margin = new Thickness(0, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = text,
                    FontSize = 11,
                    Foreground = Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
        }

        private static ToggleButton BuildStarButton(string identity, bool isFavourite, Border favouriteChip)
        {
            var icon = new iNKORE.UI.WPF.Modern.Controls.FontIcon
            {
                Icon = isFavourite ? SegoeFluentIcons.FavoriteStarFill : SegoeFluentIcons.FavoriteStar,
                FontSize = 16,
            };

            var button = new ToggleButton
            {
                IsChecked = isFavourite,
                Content = icon,
                Width = 28,
                Height = 28,
                Padding = new Thickness(0),
                Margin = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                ToolTip = isFavourite ? CommonStrings.SettingsTag_FavouriteRemove : CommonStrings.SettingsTag_FavouriteAdd,
            };

            button.Checked += (s, e) =>
            {
                icon.Icon = SegoeFluentIcons.FavoriteStarFill;
                button.ToolTip = CommonStrings.SettingsTag_FavouriteRemove;
                ToggleFavourite(identity, true, favouriteChip);
            };
            button.Unchecked += (s, e) =>
            {
                icon.Icon = SegoeFluentIcons.FavoriteStar;
                button.ToolTip = CommonStrings.SettingsTag_FavouriteAdd;
                ToggleFavourite(identity, false, favouriteChip);
            };

            return button;
        }

        private static void ToggleFavourite(string identity, bool add, Border favouriteChip)
        {
            var settings = SettingsManager.Settings;
            if (settings == null) return;

            if (settings.FavouriteSettings == null)
                settings.FavouriteSettings = new List<string>();

            if (add)
            {
                if (!settings.FavouriteSettings.Any(p => string.Equals(p, identity, StringComparison.OrdinalIgnoreCase)))
                    settings.FavouriteSettings.Add(identity);
            }
            else
            {
                settings.FavouriteSettings.RemoveAll(p => string.Equals(p, identity, StringComparison.OrdinalIgnoreCase));
            }

            SettingsManager.SaveSettingsToFile();

            if (favouriteChip != null)
            {
                favouriteChip.Visibility = add ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private static bool HasAncestor<T>(DependencyObject node) where T : DependencyObject
        {
            var parent = LogicalTreeHelper.GetParent(node);
            while (parent != null)
            {
                if (parent is T) return true;
                parent = LogicalTreeHelper.GetParent(parent);
            }
            return false;
        }

        private static IEnumerable<DependencyObject> EnumerateLogicalDescendants(DependencyObject root)
        {
            if (root == null) yield break;
            var stack = new Stack<DependencyObject>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var node = stack.Pop();
                yield return node;
                foreach (var child in LogicalTreeHelper.GetChildren(node))
                {
                    if (child is DependencyObject d) stack.Push(d);
                }
            }
        }

        private static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typed) return typed;
                var result = FindVisualChild<T>(child);
                if (result != null) return result;
            }
            return null;
        }
    }
}
