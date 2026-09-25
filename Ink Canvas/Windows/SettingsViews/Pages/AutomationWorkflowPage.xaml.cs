using Ink_Canvas.WorkflowAutomation;
using Ink_Canvas.WorkflowAutomation.Enums;
using Ink_Canvas.WorkflowAutomation.Models;
using Ink_Canvas.WorkflowAutomation.Services;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Page = iNKORE.UI.WPF.Modern.Controls.Page;

namespace Ink_Canvas.Windows.SettingsViews.Pages
{
    public partial class AutomationWorkflowPage : Page, INotifyPropertyChanged
    {
        private bool _isLoaded = false;
        private bool _isUpdatingEditor = false;
        private AutomationService Service => AutomationBootstrap.Service;

        // 静态属性供 XAML x:Static 绑定
        public static List<TriggerInfo> RegisteredTriggersList => AutomationRegistry.RegisteredTriggers;
        public static List<ActionRegistryInfo> RegisteredActionsList =>
            AutomationRegistry.RegisteredActions.Values.ToList();
        public static List<RuleRegistryInfo> RegisteredRulesList =>
            AutomationRegistry.RegisteredRules.Values.ToList();

        public AutomationWorkflowPage()
        {
            DataContext = this;
            InitializeComponent();
            Loaded += AutomationWorkflowPage_Loaded;
            Unloaded += AutomationWorkflowPage_Unloaded;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void AutomationWorkflowPage_Loaded(object sender, RoutedEventArgs e)
        {
            _isLoaded = true;
            RefreshWorkflowList();
        }

        private void AutomationWorkflowPage_Unloaded(object sender, RoutedEventArgs e)
        {
            _isLoaded = false;
        }

        #region Navigation

        private void NavigationListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded) return;

            SelectedWorkflow = NavigationListBox.SelectedItem as Workflow;

            if (NavigationListBox.SelectedItem is Workflow workflow)
            {
                WorkflowEditorPanel.Visibility = Visibility.Visible;
                EmptyStatePanel.Visibility = Visibility.Collapsed;
                UpdateEditorBindings(workflow);
            }
            else
            {
                WorkflowEditorPanel.Visibility = Visibility.Collapsed;
                EmptyStatePanel.Visibility = Visibility.Visible;
            }
        }

        private void RefreshWorkflowList()
        {
            // Remove old workflow items
            NavigationListBox.Items.Clear();

            // Add workflow items
            foreach (var workflow in Service.Workflows)
            {
                NavigationListBox.Items.Add(workflow);
            }
        }

        #endregion

        #region Workflow Editor

        private Workflow _selectedWorkflow;
        public Workflow SelectedWorkflow
        {
            get => _selectedWorkflow;
            private set
            {
                if (ReferenceEquals(value, _selectedWorkflow)) return;
                _selectedWorkflow = value;
                OnPropertyChanged();
            }
        }

        private void BtnAddWorkflow_Click(object sender, RoutedEventArgs e)
        {
            var workflow = new Workflow();
            workflow.ActionSet.Name = $"自定义自动化 {Service.Workflows.Count + 1}";
            // 初始化一个默认规则组
            workflow.Ruleset.Groups.Add(new RuleGroup
            {
                Rules = new ObservableCollection<Rule> { new Rule() }
            });
            Service.Workflows.Add(workflow);
            Service.SaveConfig("AddWorkflow");
            RefreshWorkflowList();
            NavigationListBox.SelectedIndex = NavigationListBox.Items.Count - 1;
        }

        private void BtnRemoveWorkflow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not Workflow workflow) return;
            Service.Workflows.Remove(workflow);
            Service.SaveConfig("RemoveWorkflow");
            RefreshWorkflowList();
            NavigationListBox.SelectedIndex = 0;
        }

        private void BtnDuplicateWorkflow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not Workflow source) return;
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(source);
            var copy = Newtonsoft.Json.JsonConvert.DeserializeObject<Workflow>(json);
            if (copy != null)
            {
                copy.ActionSet.Name += " (副本)";
                Service.Workflows.Add(copy);
                Service.SaveConfig("DuplicateWorkflow");
                RefreshWorkflowList();
                NavigationListBox.SelectedIndex = NavigationListBox.Items.Count - 1;
            }
        }

        private void ToggleWorkflowEnabled_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            Service.SaveConfig("WorkflowEnabledChanged");
        }

        private void UpdateEditorBindings(Workflow workflow)
        {
            _isUpdatingEditor = true;
            try
            {
                TextBoxWorkflowName.TextChanged -= TextBoxWorkflowName_TextChanged;
                TextBoxWorkflowName.Text = workflow.ActionSet.Name;
                TextBoxWorkflowName.TextChanged += TextBoxWorkflowName_TextChanged;

                CheckBoxIsRevertEnabled.IsChecked = workflow.ActionSet.IsRevertEnabled;
                ToggleIsConditionEnabled.IsOn = workflow.IsConditionEnabled;

                // 触发器
                TriggersItemsControl.ItemsSource = workflow.Triggers;

                // 行动
                ActionsItemsControl.ItemsSource = workflow.ActionSet.Actions;

                // 规则集 - 先评估更新 State，再设置 ItemsSource
                ComboBoxRulesetMode.SelectedIndex = workflow.Ruleset.Mode == RulesetLogicalMode.Or ? 0 : 1;
                CheckBoxRulesetReversed.IsChecked = workflow.Ruleset.IsReversed;
                UpdateRulesetStateIndicator(workflow.Ruleset);
                RuleGroupsItemsControl.ItemsSource = workflow.Ruleset.Groups;

                UpdateConditionVisibility(workflow.IsConditionEnabled);
                UpdateRevertHintVisibility(workflow.ActionSet.IsRevertEnabled);
            }
            finally
            {
                _isUpdatingEditor = false;
            }
        }

        private void UpdateRulesetStateIndicator(Ruleset ruleset)
        {
            if (ruleset == null)
            {
                EllipseRulesetState.Fill = Brushes.DarkGray;
                return;
            }

            // 评估规则集（会自动更新所有层级的 State）
            Service.RulesetService.IsRulesetSatisfied(ruleset);

            EllipseRulesetState.Fill = ruleset.State switch
            {
                2 => Brushes.Green,
                1 => Brushes.IndianRed,
                _ => Brushes.DarkGray
            };
        }

        private void TextBoxWorkflowName_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingEditor) return;
            if (SelectedWorkflow is Workflow workflow)
            {
                workflow.ActionSet.Name = TextBoxWorkflowName.Text;
                Service.SaveConfig("NameChanged");
            }
        }

        private void CheckBoxIsRevertEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded || _isUpdatingEditor) return;
            if (SelectedWorkflow is Workflow workflow)
            {
                workflow.ActionSet.IsRevertEnabled = CheckBoxIsRevertEnabled.IsChecked == true;
                UpdateRevertHintVisibility(workflow.ActionSet.IsRevertEnabled);
                Service.SaveConfig("RevertEnabledChanged");
            }
        }

        private void ToggleIsConditionEnabled_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded || _isUpdatingEditor) return;
            if (SelectedWorkflow is Workflow workflow)
            {
                workflow.IsConditionEnabled = ToggleIsConditionEnabled.IsOn;
                UpdateConditionVisibility(workflow.IsConditionEnabled);
                Service.SaveConfig("ConditionEnabledChanged");
            }
        }

        private void UpdateConditionVisibility(bool enabled)
        {
            ConditionDisabledHint.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
            ConditionEditorPanel.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateRevertHintVisibility(bool enabled)
        {
            RevertHintPanel.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        }

        // 触发器操作
        private void BtnAddTrigger_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedWorkflow is not Workflow workflow) return;
            var trigger = new TriggerSettings { Id = AutomationRegistry.RegisteredTriggers.FirstOrDefault()?.Id ?? "" };
            workflow.Triggers.Add(trigger);
            Service.SaveConfig("AddTrigger");
        }

        private void BtnRemoveTrigger_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedWorkflow is not Workflow workflow) return;
            if (sender is not Button btn || btn.Tag is not TriggerSettings trigger) return;
            workflow.Triggers.Remove(trigger);
            Service.SaveConfig("RemoveTrigger");
        }

        private void ComboBoxTriggerType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded) return;
            if (sender is ComboBox cb && cb.Tag is TriggerSettings trigger)
            {
                trigger.Id = cb.SelectedValue as string ?? "";
                Service.SaveConfig("TriggerTypeChanged");
            }
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

        // 行动操作
        private void BtnAddAction_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedWorkflow is not Workflow workflow) return;
            var firstAction = AutomationRegistry.RegisteredActions.FirstOrDefault();
            var action = new Ink_Canvas.WorkflowAutomation.Models.Action { Id = firstAction.Key ?? "" };
            workflow.ActionSet.Actions.Add(action);
            Service.SaveConfig("AddAction");
        }

        private void BtnRemoveAction_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedWorkflow is not Workflow workflow) return;
            if (sender is not Button btn || btn.Tag is not Ink_Canvas.WorkflowAutomation.Models.Action action) return;
            workflow.ActionSet.Actions.Remove(action);
            Service.SaveConfig("RemoveAction");
        }

        private void ComboBoxActionType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded || _isUpdatingEditor) return;
            if (sender is ComboBox cb && cb.Tag is Ink_Canvas.WorkflowAutomation.Models.Action action)
            {
                var newId = cb.SelectedValue as string ?? "";
                // ID 没有变化（例如 ComboBox 因绑定/页面切换重新加载触发 SelectionChanged），
                // 不应重置 Settings，否则会把用户已保存的设置覆盖为默认值（issue #560）。
                if (action.Id == newId) return;

                action.Id = newId;
                action.Settings = null;

                if (FindVisualChild<AutomationSettingsPresenter>(cb.Parent) is { } presenter)
                    presenter.RefreshContent();

                Service.SaveConfig("ActionTypeChanged");
            }
        }

        // 规则集操作
        private void ComboBoxRulesetMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded || _isUpdatingEditor) return;
            if (SelectedWorkflow is Workflow workflow)
            {
                workflow.Ruleset.Mode = ComboBoxRulesetMode.SelectedIndex == 0 ? RulesetLogicalMode.Or : RulesetLogicalMode.And;
                UpdateRulesetStateIndicator(workflow.Ruleset);
                Service.SaveConfig("RulesetModeChanged");
            }
        }

        private void CheckBoxRulesetReversed_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded || _isUpdatingEditor) return;
            if (SelectedWorkflow is Workflow workflow)
            {
                workflow.Ruleset.IsReversed = CheckBoxRulesetReversed.IsChecked == true;
                UpdateRulesetStateIndicator(workflow.Ruleset);
                Service.SaveConfig("RulesetReversedChanged");
            }
        }

        private void BtnAddRuleGroup_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedWorkflow is not Workflow workflow) return;
            workflow.Ruleset.Groups.Add(new RuleGroup
            {
                Rules = new ObservableCollection<Rule> { new Rule() }
            });
            Service.SaveConfig("AddRuleGroup");
        }

        private void BtnDeleteGroup_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedWorkflow is not Workflow workflow) return;
            if (sender is not Button btn || btn.Tag is not RuleGroup group) return;
            workflow.Ruleset.Groups.Remove(group);
            Service.SaveConfig("DeleteGroup");
        }

        private void BtnDuplicateGroup_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedWorkflow is not Workflow workflow) return;
            if (sender is not Button btn || btn.Tag is not RuleGroup source) return;
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(source);
            var copy = Newtonsoft.Json.JsonConvert.DeserializeObject<RuleGroup>(json);
            if (copy != null)
            {
                workflow.Ruleset.Groups.Add(copy);
                Service.SaveConfig("DuplicateGroup");
            }
        }

        private void ComboBoxGroupMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded) return;
            if (sender is ComboBox cb && cb.Tag is RuleGroup group)
            {
                group.Mode = cb.SelectedIndex == 0 ? RulesetLogicalMode.Or : RulesetLogicalMode.And;
                if (SelectedWorkflow is Workflow workflow)
                {
                    UpdateRulesetStateIndicator(workflow.Ruleset);
                }
                Service.SaveConfig("GroupModeChanged");
            }
        }

        private void CheckBoxGroupReversed_Changed(object sender, RoutedEventArgs e)
        {
            // IsReversed 绑定是 TwoWay，自动更新
            if (!_isLoaded) return;
            if (SelectedWorkflow is Workflow workflow)
            {
                UpdateRulesetStateIndicator(workflow.Ruleset);
            }
            Service.SaveConfig("GroupReversedChanged");
        }

        private void CheckBoxGroupEnabled_Toggled(object sender, RoutedEventArgs e)
        {
            // IsEnabled 绑定是 TwoWay，自动更新
            if (!_isLoaded) return;
            if (SelectedWorkflow is Workflow workflow)
            {
                UpdateRulesetStateIndicator(workflow.Ruleset);
            }
            Service.SaveConfig("GroupEnabledChanged");
        }

        private void BtnAddRuleToGroup_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not RuleGroup group) return;
            var firstRule = AutomationRegistry.RegisteredRules.FirstOrDefault();
            var rule = new Rule { Id = firstRule.Key ?? "" };
            group.Rules.Add(rule);
            if (SelectedWorkflow is Workflow workflow)
            {
                UpdateRulesetStateIndicator(workflow.Ruleset);
            }
            Service.SaveConfig("AddRule");
        }

        private void BtnRemoveRule_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not Rule rule) return;
            // 找到包含此规则的 RuleGroup
            if (SelectedWorkflow is Workflow workflow)
            {
                foreach (var group in workflow.Ruleset.Groups)
                {
                    if (group.Rules.Contains(rule))
                    {
                        group.Rules.Remove(rule);
                        UpdateRulesetStateIndicator(workflow.Ruleset);
                        Service.SaveConfig("RemoveRule");
                        break;
                    }
                }
            }
        }

        private void ComboBoxRuleType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded) return;
            if (sender is ComboBox cb && cb.Tag is Rule rule)
            {
                rule.Id = cb.SelectedValue as string ?? "";
                EnsureRuleSettingsInstance(rule);
                if (SelectedWorkflow is Workflow workflow)
                {
                    UpdateRulesetStateIndicator(workflow.Ruleset);
                }
                Service.SaveConfig("RuleTypeChanged");
            }
        }

        private void CheckBoxRuleReversed_Changed(object sender, RoutedEventArgs e)
        {
            // IsReversed 绑定是 TwoWay，自动更新
            if (!_isLoaded) return;
            if (SelectedWorkflow is Workflow workflow)
            {
                UpdateRulesetStateIndicator(workflow.Ruleset);
            }
            Service.SaveConfig("RuleReversedChanged");
        }

        // 触发/恢复按钮
        private void BtnInvokeAction_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedWorkflow is not Workflow workflow) return;
            Service.ActionService.Invoke(workflow.ActionSet);
            Service.SaveConfig("ManualInvokeAction");
        }

        private void BtnRevertAction_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedWorkflow is not Workflow workflow) return;
            Service.ActionService.Revert(workflow.ActionSet);
            Service.SaveConfig("ManualRevertAction");
        }

        private void RuleSettingsPresenter_SettingsChanged(object sender, EventArgs e)
        {
            if (SelectedWorkflow is Workflow workflow)
            {
                UpdateRulesetStateIndicator(workflow.Ruleset);
            }
            Service.SaveConfig("RuleSettingsChanged");
        }

        private void ActionSettingsPresenter_SettingsChanged(object sender, EventArgs e)
        {
            Service.SaveConfig("ActionSettingsChanged");
        }

        internal static object EnsureSettingsInstance(object settings, Type settingsType)
        {
            if (settingsType == null) return null;
            var actual = settings ?? Activator.CreateInstance(settingsType);

            if (actual is JToken token)
            {
                try
                {
                    actual = token.ToObject(settingsType);
                }
                catch
                {
                    actual = Activator.CreateInstance(settingsType);
                }
            }

            if (actual == null || actual.GetType() != settingsType)
            {
                actual = Activator.CreateInstance(settingsType);
            }

            return actual;
        }

        private static void EnsureRuleSettingsInstance(Rule rule)
        {
            if (!AutomationRegistry.RegisteredRules.TryGetValue(rule.Id, out var info))
                return;

            rule.Settings = EnsureSettingsInstance(rule.Settings, info.SettingsType);
        }

        #endregion
    }

    public class AutomationSettingsPresenter : ContentControl
    {
        public static readonly DependencyProperty ItemProperty = DependencyProperty.Register(
            nameof(Item), typeof(object), typeof(AutomationSettingsPresenter),
            new PropertyMetadata(null, OnItemChanged));

        private INotifyPropertyChanged _currentNotifySource;

        public object Item
        {
            get => GetValue(ItemProperty);
            set => SetValue(ItemProperty, value);
        }

        public event EventHandler SettingsChanged;

        public AutomationSettingsPresenter()
        {
            Loaded += (_, _) => RefreshContent();
            Unloaded += (_, _) => DetachCurrentItem();
        }

        private static void OnItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not AutomationSettingsPresenter presenter) return;
            presenter.DetachCurrentItem();
            presenter.AttachItem(e.NewValue);
            presenter.RefreshContent();
        }

        private void AttachItem(object item)
        {
            if (item is INotifyPropertyChanged notify)
            {
                _currentNotifySource = notify;
                _currentNotifySource.PropertyChanged += CurrentItem_PropertyChanged;
            }
        }

        private void DetachCurrentItem()
        {
            if (_currentNotifySource == null) return;
            _currentNotifySource.PropertyChanged -= CurrentItem_PropertyChanged;
            _currentNotifySource = null;
        }

        private void CurrentItem_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(Rule.Id) or nameof(Rule.Settings) or nameof(Ink_Canvas.WorkflowAutomation.Models.Action.Id) or nameof(Ink_Canvas.WorkflowAutomation.Models.Action.Settings))
            {
                RefreshContent();
            }
        }

        internal void RefreshContent()
        {
            var context = ResolveSettingsContext();
            if (context == null)
            {
                Visibility = Visibility.Collapsed;
                Content = null;
                return;
            }

            var editableProperties = context.SettingsType
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(p => p.CanRead && p.CanWrite)
                .ToArray();

            if (editableProperties.Length == 0)
            {
                Visibility = Visibility.Collapsed;
                Content = null;
                return;
            }

            Visibility = Visibility.Visible;

            var panel = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            foreach (var property in editableProperties)
            {
                var editor = CreateEditor(context.SettingsObject, property);
                if (editor != null)
                {
                    panel.Children.Add(editor);
                }
            }

            Content = panel.Children.Count > 0 ? panel : null;
            Visibility = panel.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private FrameworkElement CreateEditor(object settingsObject, PropertyInfo property)
        {
            var propertyType = property.PropertyType;
            var label = GetPropertyDisplayName(property.Name);
            var value = property.GetValue(settingsObject);

            if (propertyType == typeof(string))
            {
                var container = CreateLabeledContainer(label);
                var textBox = new TextBox
                {
                    Text = value as string ?? "",
                    MinWidth = 180,
                    Padding = new Thickness(8, 4, 8, 4)
                };
                textBox.TextChanged += (_, _) =>
                {
                    property.SetValue(settingsObject, textBox.Text);
                    RaiseSettingsChanged();
                };
                container.Children.Add(textBox);
                return container;
            }

            if (propertyType == typeof(bool))
            {
                var checkBox = new CheckBox
                {
                    Content = label,
                    IsChecked = value as bool? ?? false,
                    Margin = new Thickness(0, 18, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                checkBox.Checked += (_, _) =>
                {
                    property.SetValue(settingsObject, true);
                    RaiseSettingsChanged();
                };
                checkBox.Unchecked += (_, _) =>
                {
                    property.SetValue(settingsObject, false);
                    RaiseSettingsChanged();
                };
                return checkBox;
            }

            if (propertyType == typeof(int) || propertyType == typeof(double))
            {
                var container = CreateLabeledContainer(label);
                var textBox = new TextBox
                {
                    Text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
                    MinWidth = 100,
                    Padding = new Thickness(8, 4, 8, 4)
                };
                textBox.LostFocus += (_, _) =>
                {
                    try
                    {
                        var converted = propertyType == typeof(int)
                            ? int.Parse(textBox.Text, CultureInfo.InvariantCulture)
                            : double.Parse(textBox.Text, CultureInfo.InvariantCulture);
                        property.SetValue(settingsObject, converted);
                        RaiseSettingsChanged();
                    }
                    catch
                    {
                        textBox.Text = Convert.ToString(property.GetValue(settingsObject), CultureInfo.InvariantCulture) ?? "";
                    }
                };
                container.Children.Add(textBox);
                return container;
            }

            if (propertyType.IsEnum)
            {
                var container = CreateLabeledContainer(label);
                var comboBox = new ComboBox
                {
                    ItemsSource = Enum.GetValues(propertyType),
                    SelectedItem = value,
                    MinWidth = 120
                };
                comboBox.SelectionChanged += (_, _) =>
                {
                    if (comboBox.SelectedItem == null) return;
                    property.SetValue(settingsObject, comboBox.SelectedItem);
                    RaiseSettingsChanged();
                };
                container.Children.Add(comboBox);
                return container;
            }

            return null;
        }

        private static StackPanel CreateLabeledContainer(string label)
        {
            var container = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            container.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 11,
                Opacity = 0.72,
                Margin = new Thickness(0, 0, 0, 2)
            });
            return container;
        }

        private SettingsContext ResolveSettingsContext()
        {
            switch (Item)
            {
                case Rule rule:
                    if (!AutomationRegistry.RegisteredRules.TryGetValue(rule.Id, out var ruleInfo) || ruleInfo.SettingsType == null)
                        return null;
                    rule.Settings = AutomationWorkflowPage.EnsureSettingsInstance(rule.Settings, ruleInfo.SettingsType);
                    return new SettingsContext(rule.Settings!, ruleInfo.SettingsType);

                case Ink_Canvas.WorkflowAutomation.Models.Action action:
                    if (!AutomationRegistry.RegisteredActions.TryGetValue(action.Id, out var actionInfo) || actionInfo.SettingsType == null)
                        return null;
                    action.Settings = AutomationWorkflowPage.EnsureSettingsInstance(action.Settings, actionInfo.SettingsType);
                    return new SettingsContext(action.Settings!, actionInfo.SettingsType);

                default:
                    return null;
            }
        }

        private static string GetPropertyDisplayName(string propertyName)
        {
            return propertyName switch
            {
                "ProcessName" => "进程名",
                "TitleContains" => "标题包含",
                "IgnoreCase" => "忽略大小写",
                "Type" => "通知类型",
                "Message" => "通知内容",
                "SavePath" => "保存路径",
                "SaveAsXml" => "保存为 XML",
                "Fold" => "折叠",
                "EnterAnnotation" => "进入批注",
                "Topmost" => "置顶",
                _ => propertyName
            };
        }

        private void RaiseSettingsChanged()
        {
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private sealed record SettingsContext(object SettingsObject, Type SettingsType);
    }

    /// <summary>
    /// RulesetLogicalMode 到 int 的转换器，用于 ComboBox SelectedIndex 绑定
    /// </summary>
    public class RulesetLogicalModeToIntConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is RulesetLogicalMode mode)
                return (int)mode; // Or=0, And=1
            return 0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int i)
                return (RulesetLogicalMode)i;
            return RulesetLogicalMode.Or;
        }
    }
}
