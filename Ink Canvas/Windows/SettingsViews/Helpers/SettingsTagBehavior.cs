using Ink_Canvas.Controls;
using Ink_Canvas.Helpers;
using Ink_Canvas.Properties;
using Ink_Canvas.Windows.SettingsViews.Helpers;
using iNKORE.UI.WPF.Modern.Common.IconKeys;
using iNKORE.UI.WPF.Modern.Controls;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
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

        public struct CardEntry
        {
            public FrameworkElement Element;
            public string Identity;
            public bool HasRealKey;
            public bool IsGroupHeader;
        }

        /// <summary>
        /// 页内所有卡片及其身份。身份优先级：
        /// 1. 解析出的真实设置键（卡片 SettingsTags.PropertyPath，或卡片/卡内控件的 SettingsBinder.PropertyPath）
        /// 2. 卡片 x:Name → "{pageTag}:{xName}"
        /// 3. 兜底 → "{pageTag}:card{序号}"（序号 = 页面逻辑树遍历序）
        /// 遍历顺序与 SettingsWindow.CollectEntriesFromPage 完全一致，保证两侧序号一致。
        /// </summary>
        public static List<CardEntry> EnumerateCardIdentities(DependencyObject root, string pageTag)
        {
            var result = new List<CardEntry>();
            if (root == null) return result;

            var seen = new HashSet<FrameworkElement>();
            int ordinal = 0;

            void AppendCard(FrameworkElement element, bool isGroupHeader = false)
            {
                if (!seen.Add(element)) return;
                bool hasRealKey = ResolveSettingKey(element, out string key);
                string identity = hasRealKey ? key
                    : !string.IsNullOrEmpty(element.Name) ? pageTag + ":" + element.Name
                    : pageTag + ":card" + ordinal;
                result.Add(new CardEntry
                {
                    Element = element,
                    Identity = identity,
                    HasRealKey = hasRealKey,
                    IsGroupHeader = isGroupHeader,
                });
                ordinal++;
            }

            foreach (var node in EnumerateLogicalDescendants(root))
            {
                // SettingsExpander 折叠时内部卡片尚未物化，不在逻辑树中；直接从 Items 集合补齐。
                // 组顶行（如"启用启动动画"顶行开关）同样需要身份，从模板内 Header 卡补齐。
                if (node is SettingsExpander expander)
                {
                    var groupHeader = FindExpanderGroupHeaderCard(expander);
                    if (groupHeader != null) AppendCard(groupHeader, isGroupHeader: true);

                    foreach (var item in expander.Items)
                    {
                        if (item is LabeledSettingsCard lscItem) AppendCard(lscItem);
                        else if (item is SettingsCard scItem && !HasAncestor<LabeledSettingsCard>(scItem)) AppendCard(scItem);
                    }
                    continue;
                }

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

                // 兜底：若组顶行卡经由逻辑树到达（非物化补位路径），同样标记为组顶行
                AppendCard(element, element is SettingsCard groupCard && IsSettingsExpanderGroupHeaderCard(groupCard));
            }
            return result;
        }

        /// <summary>
        /// 为页面全部卡片注入更多按钮 + 标签 chip。仅"真设置项"卡片注入：
        /// 常规设置卡、声明了 PropertyPath 的卡、以及内容含真开关的 SettingsExpander 组顶行。
        /// </summary>
        public static void InjectStarsIntoPage(FrameworkElement pageRoot, string pageTag)
        {
            if (pageRoot == null) return;
            foreach (var entry in EnumerateCardIdentities(pageRoot, pageTag))
            {
                Inject(entry.Element, entry.Identity, pageTag, entry.HasRealKey, entry.IsGroupHeader);
            }
        }

        /// <summary>
        /// 解析卡片的真实设置键（用于收藏/深链身份）。
        /// 优先级：卡片 SettingsTags.PropertyPath → 卡片或卡内控件 SettingsBinder.PropertyPath。
        /// </summary>
        private static bool ResolveSettingKey(FrameworkElement element, out string key)
        {
            foreach (var node in EnumerateLogicalDescendants(element))
            {
                if (!(node is FrameworkElement fe)) continue;
                string p = SettingsTags.GetPropertyPath(fe);
                if (string.IsNullOrWhiteSpace(p)) p = SettingsBinder.GetPropertyPath(fe);
                if (!string.IsNullOrWhiteSpace(p))
                {
                    key = p.Trim();
                    return true;
                }
            }
            key = null;
            return false;
        }

        /// <summary>
        /// 是否真设置项：LabeledSettingsCard（本身即开关）恒真；普通 SettingsCard 需内容子树含设置控件，
        /// 且不是折叠分组 header。组顶行卡（expander header 内含真开关，如"启用启动动画"）经显式补位后放行。
        /// </summary>
        private static bool IsTrueSettingItem(FrameworkElement element, bool allowGroupHeader = false)
        {
            if (element is LabeledSettingsCard) return true;
            // 显式声明了 PropertyPath 的卡片视为真设置项（作者声明该行可收藏 / 可深链）
            if (ResolveSettingKey(element, out _)) return true;
            if (!allowGroupHeader && IsSettingsExpanderGroupHeaderCard(element)) return false;
            return ContainsSettingControl(element);
        }

        /// <summary>
        /// 内容子树是否含"设置控件"。决定卡片是否算可操作设置行。
        /// </summary>
        private static bool ContainsSettingControl(FrameworkElement element)
        {
            foreach (var node in EnumerateLogicalDescendants(element))
            {
                if (node is iNKORE.UI.WPF.Modern.Controls.ToggleSwitch
                    || node is iNKORE.UI.WPF.Modern.Controls.NumberBox
                    || node is ComboBox || node is Slider || node is TextBox
                    || node is CheckBox || node is RadioButton || node is PasswordBox)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// SettingsExpander 模板内的分组 header 卡（视觉祖先经过名为 ExpanderHeader 的折叠按钮）。
        /// 分组 header 视为"其他显示内容"，不注入。
        /// </summary>
        private static bool IsSettingsExpanderGroupHeaderCard(FrameworkElement card)
        {
            DependencyObject cur = card;
            while (cur != null)
            {
                cur = VisualTreeHelper.GetParent(cur);
                if (cur is ToggleButton tb && tb.Name == "ExpanderHeader") return true;
            }
            return false;
        }

        private static void Inject(FrameworkElement element, string identity, string pageTag, bool hasRealKey, bool isGroupHeader = false)
        {
            if (!IsTrueSettingItem(element, isGroupHeader)) return;

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

            headerPanel.Children.Insert(0, headerRow);

            // 更多按钮放到卡片模板最右侧动作列（col3），跨两行垂直居中
            var more = BuildMoreButton(identity, pageTag, hasRealKey, isFavourite, favouriteChip);
            if (more != null)
            {
                PlaceMoreButton(card, more);
            }
        }

        /// <summary>
        /// 将更多按钮放入 SettingsCard 模板根 Grid 的最右列（Grid.Column=3，PART_ActionIconPresenterHolder 同列）。
        /// 不占用库代码托管 ActionIcon 子级，避免其 OnActionIconChanged 回调把可见性改回 Collapsed。
        /// </summary>
        private static void PlaceMoreButton(SettingsCard card, Button more)
        {
            try
            {
                if (!(card.Template?.FindName("PART_RootGrid", card) is Grid rootGrid)) return;

                // 沿用动作列宿主边距（模板内为 SettingsCardActionIconMargin，默认 14,0,0,0）
                Thickness margin = new Thickness(0);
                if (card.Template.FindName("PART_ActionIconPresenterHolder", card) is FrameworkElement holder)
                {
                    margin = holder.Margin;
                }

                more.Margin = margin;
                more.VerticalAlignment = VerticalAlignment.Center;
                more.HorizontalAlignment = HorizontalAlignment.Center;
                Grid.SetColumn(more, 3);
                Grid.SetRowSpan(more, 2);
                rootGrid.Children.Add(more);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"放置设置项更多按钮出错: {ex.Message}");
            }
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

        private static Button BuildMoreButton(string identity, string pageTag,
            bool hasRealKey, bool isFavourite, Border favouriteChip)
        {
            var icon = new iNKORE.UI.WPF.Modern.Controls.FontIcon
            {
                Icon = SegoeFluentIcons.More,
                FontSize = 16,
            };

            var button = new Button
            {
                Content = icon,
                Width = 28,
                Height = 28,
                Padding = new Thickness(0),
                Margin = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                ToolTip = CommonStrings.SettingsItemMore,
            };

            var menu = new ContextMenu();
            var itemCopyKey = new MenuItem
            {
                Header = CommonStrings.SettingsItemCopyKey,
                Visibility = hasRealKey ? Visibility.Visible : Visibility.Collapsed,
            };
            var itemCopyUrl = new MenuItem { Header = CommonStrings.SettingsItemCopyUrl };
            var itemFavourite = new MenuItem
            {
                Header = isFavourite ? CommonStrings.SettingsItemRemoveFavourite : CommonStrings.SettingsItemAddFavourite,
            };
            menu.Items.Add(itemCopyKey);
            menu.Items.Add(itemCopyUrl);
            menu.Items.Add(itemFavourite);
            button.ContextMenu = menu;

            itemCopyKey.Click += (s, e) =>
            {
                e.Handled = true;
                SafeCopyText(identity);
            };
            itemCopyUrl.Click += (s, e) =>
            {
                e.Handled = true;
                string url = "icc://settings/entry?page=" + Uri.EscapeDataString(pageTag)
                    + "&path=" + Uri.EscapeDataString(identity);
                SafeCopyText(url);
            };
            itemFavourite.Click += (s, e) =>
            {
                e.Handled = true;
                bool add = !SettingsTagResolver.IsFavourite(identity);
                ToggleFavourite(identity, add, favouriteChip);
                itemFavourite.Header = add
                    ? CommonStrings.SettingsItemRemoveFavourite
                    : CommonStrings.SettingsItemAddFavourite;
            };

            // 单击按钮即弹出菜单，且不触发卡片整卡 Click
            button.Click += (s, e) =>
            {
                e.Handled = true;
                button.ContextMenu.PlacementTarget = button;
                button.ContextMenu.IsOpen = true;
            };

            return button;
        }

        /// <summary>
        /// 复制文本。WPF 的 OLE flush 会被剪贴板上一持有者阻塞数秒，导致 UI 冻结，
        /// 故放到后台 STA 线程执行；被其它进程占用时可能抛 CLIPBRD_E_CANT_OPEN，重试后静默忽略，
        /// 避免异常冒泡触发全局崩溃窗口。
        /// </summary>
        private static void SafeCopyText(string text)
        {
            var thread = new System.Threading.Thread(() =>
            {
                for (int i = 0; i < 5; i++)
                {
                    try
                    {
                        Clipboard.SetText(text);
                        return;
                    }
                    catch (COMException)
                    {
                        System.Threading.Thread.Sleep(50);
                    }
                }
            });
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
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

        /// <summary>
        /// 取 SettingsExpander 模板内"组顶行"SettingsCard（ExpanderHeader 折叠按钮内容中的那张卡，
        /// 其上承载组开关/内容，如"启用启动动画"顶行）。Header 行始终显示，展开器物化后即可取到。
        /// </summary>
        private static SettingsCard FindExpanderGroupHeaderCard(SettingsExpander expander)
        {
            if (expander == null) return null;
            try { expander.ApplyTemplate(); } catch { }
            var headerToggle = FindVisualDescendantByName<ToggleButton>(expander, "ExpanderHeader");
            if (headerToggle == null) return null;
            return FindVisualChild<SettingsCard>(headerToggle);
        }

        private static T FindVisualDescendantByName<T>(DependencyObject parent, string name) where T : FrameworkElement
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typed && typed.Name == name) return typed;
                var nested = FindVisualDescendantByName<T>(child, name);
                if (nested != null) return nested;
            }
            return null;
        }
    }
}
