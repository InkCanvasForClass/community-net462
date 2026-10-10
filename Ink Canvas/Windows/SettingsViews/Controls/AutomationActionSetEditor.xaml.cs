using Ink_Canvas.WorkflowAutomation;
using Ink_Canvas.WorkflowAutomation.Models;
using Ink_Canvas.WorkflowAutomation.Services;
using Ink_Canvas.Windows.SettingsViews.Pages;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ActionModel = Ink_Canvas.WorkflowAutomation.Models.Action;

namespace Ink_Canvas.Windows.SettingsViews.Controls
{
    /// <summary>
    /// 动作组（行动）编辑控件，可复用于工作流与动作组。
    /// </summary>
    public partial class AutomationActionSetEditor : UserControl
    {
        public static readonly DependencyProperty ActionSetProperty = DependencyProperty.Register(
            nameof(ActionSet), typeof(ActionSet), typeof(AutomationActionSetEditor),
            new PropertyMetadata(null, OnActionSetChanged));

        public ActionSet ActionSet
        {
            get => (ActionSet)GetValue(ActionSetProperty);
            set => SetValue(ActionSetProperty, value);
        }

        /// <summary>
        /// 编辑内容发生变化（页面据此保存配置）。
        /// </summary>
        public event EventHandler SettingsChanged;

        private bool _isLoaded;

        public AutomationActionSetEditor()
        {
            InitializeComponent();
            Loaded += (_, _) => { _isLoaded = true; };
            Unloaded += (_, _) => { _isLoaded = false; };
        }

        private static void OnActionSetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not AutomationActionSetEditor editor) return;
            if (editor.Root != null) editor.Root.DataContext = e.NewValue;
        }

        private void RaiseChanged()
        {
            if (!_isLoaded) return;
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void BtnAddAction_Click(object sender, RoutedEventArgs e)
        {
            if (ActionSet is not ActionSet actionSet) return;
            var firstAction = AutomationRegistry.RegisteredActions.FirstOrDefault();
            actionSet.Actions.Add(new ActionModel { Id = firstAction.Key ?? "" });
            RaiseChanged();
        }

        private void BtnRemoveAction_Click(object sender, RoutedEventArgs e)
        {
            if (ActionSet is not ActionSet actionSet) return;
            if (sender is not Button btn || btn.Tag is not ActionModel action) return;
            actionSet.Actions.Remove(action);
            RaiseChanged();
        }

        private void ComboBoxActionType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded) return;
            if (sender is ComboBox cb && cb.Tag is ActionModel action)
            {
                var newId = cb.SelectedValue as string ?? "";
                if (action.Id == newId) return;

                action.Id = newId;
                action.Settings = null;

                if (FindVisualChild<AutomationSettingsPresenter>(cb.Parent) is { } presenter)
                    presenter.RefreshContent();

                RaiseChanged();
            }
        }

        private void ActionSettingsPresenter_SettingsChanged(object sender, EventArgs e)
        {
            RaiseChanged();
        }

        private static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T target) return target;

                var result = FindVisualChild<T>(child);
                if (result != null) return result;
            }

            return null;
        }
    }
}
