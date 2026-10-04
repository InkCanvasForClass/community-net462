using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using iNKORE.UI.WPF.Modern.Controls;

namespace Ink_Canvas.Windows.SettingsViews.Helpers
{
    /// <summary>
    /// 安全设置 SettingsExpander.IsExpanded。
    /// iNKORE.UI.WPF.Modern 0.10.2.1 的 ExpanderAnimationsHelper 在内层 Expander
    /// 的 ExpanderContent 模板部件尚未生成时展开会抛 NullReferenceException。
    /// 该附加属性会等到部件就绪后再展开，避免崩溃。
    /// 用法：helpers:SettingsExpanderSafety.SafeExpanded="{Binding ...}"
    /// </summary>
    public static class SettingsExpanderSafety
    {
        public static readonly DependencyProperty SafeExpandedProperty =
            DependencyProperty.RegisterAttached(
                "SafeExpanded",
                typeof(bool),
                typeof(SettingsExpanderSafety),
                new PropertyMetadata(false, OnSafeExpandedChanged));

        public static bool GetSafeExpanded(DependencyObject obj) => (bool)obj.GetValue(SafeExpandedProperty);
        public static void SetSafeExpanded(DependencyObject obj, bool value) => obj.SetValue(SafeExpandedProperty, value);

        private const int MaxRetries = 60;

        private static void OnSafeExpandedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (!(d is SettingsExpander expander)) return;

            if (!(e.NewValue is bool expanded) || !expanded)
            {
                expander.IsExpanded = false;
                return;
            }

            TryApply(expander, 0);
        }

        private static void TryApply(SettingsExpander expander, int attempt)
        {
            if (!GetSafeExpanded(expander))
            {
                expander.IsExpanded = false;
                return;
            }

            if (IsContentPartReady(expander))
            {
                expander.IsExpanded = true;
                return;
            }

            // ponytail: 最多重试 60 次（每帧一次）；若布局迟迟未就绪则保持折叠，不再崩溃。
            if (attempt >= MaxRetries) return;
            expander.Dispatcher.BeginInvoke(
                new Action(() => TryApply(expander, attempt + 1)),
                DispatcherPriority.Loaded);
        }

        private static bool IsContentPartReady(SettingsExpander expander)
        {
            expander.ApplyTemplate();
            if (expander.Template == null || VisualTreeHelper.GetChildrenCount(expander) == 0) return false;
            if (!(VisualTreeHelper.GetChild(expander, 0) is Expander inner)) return false;

            inner.ApplyTemplate();
            return inner.Template != null &&
                   inner.Template.FindName("ExpanderContent", inner) is FrameworkElement;
        }
    }
}
