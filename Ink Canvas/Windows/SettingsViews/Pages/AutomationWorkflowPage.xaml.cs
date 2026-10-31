using Ink_Canvas.WorkflowAutomation;
using Ink_Canvas.WorkflowAutomation.Enums;
using Ink_Canvas.WorkflowAutomation.Models;
using Ink_Canvas.WorkflowAutomation.Services;
using Ink_Canvas.Helpers;
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
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using Page = iNKORE.UI.WPF.Modern.Controls.Page;

namespace Ink_Canvas.Windows.SettingsViews.Pages
{
    public partial class AutomationWorkflowPage : Page, INotifyPropertyChanged
    {
        private bool _isLoaded = false;
        private bool _isUpdatingEditor = false;
        private int _category = 0; // 0=自动化, 1=状态组, 2=动作组

        // 右侧详情列宽：未选中 20%，选中 50%
        private const double EmptyRatio = 0.30;
        private const double SelectedRatio = 0.50;
        private double _detailRatio = EmptyRatio;

        // 用户手动拖过分隔条后，不再自动改宽度
        private bool _manualResize = false;

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
            NavigationListBox.ItemsSource = Service.Workflows;
            StateGroupsListBox.ItemsSource = Service.StateGroups;
            ActionGroupsListBox.ItemsSource = Service.ActionGroups;
            if (ComboBoxCategory.SelectedIndex < 0) ComboBoxCategory.SelectedIndex = 0;
            UpdateCategoryUI();
        }

        private void AutomationWorkflowPage_Unloaded(object sender, RoutedEventArgs e)
        {
            _isLoaded = false;
        }

        #region Navigation

        private void ComboBoxCategory_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded) return;
            _category = Math.Max(0, ComboBoxCategory.SelectedIndex);
            UpdateCategoryUI();
        }

        private void HideAllEditors()
        {
            WorkflowEditorPanel.Visibility = Visibility.Collapsed;
            StateGroupEditorPanel.Visibility = Visibility.Collapsed;
            ActionGroupEditorPanel.Visibility = Visibility.Collapsed;
            EmptyStatePanel.Visibility = Visibility.Collapsed;
        }

        /// <summary>按比例设置右侧详情列宽（瞬切）。手动拖过分隔条后不再自动调整。</summary>
        private void ApplyDetailWidth(double ratio)
        {
            if (_manualResize) return;

            double total = RootGrid.ActualWidth;
            if (total <= 0) return;

            // Width 是显式值，Auto 列会据此定宽；先清掉可能的动画残留
            DetailHost.BeginAnimation(FrameworkElement.WidthProperty, null);
            DetailHost.Width = total * ratio;
        }

        private void RootGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ApplyDetailWidth(_detailRatio);
        }

        private void Splitter_DragStarted(object sender, DragStartedEventArgs e)
        {
            // 用户手动接管列宽：停止 20%/50% 自动缩放
            _manualResize = true;
            DetailHost.BeginAnimation(FrameworkElement.WidthProperty, null);
            DetailHost.Width = double.NaN;
        }

        /// <summary>显示右侧详情：隐藏占位提示，编辑器从右滑入+淡入。</summary>
        private void ShowEditor(UIElement editor)
        {
            EmptyStatePanel.Visibility = Visibility.Collapsed;
            _detailRatio = SelectedRatio;
            ApplyDetailWidth(_detailRatio);
            AnimationsHelper.ShowWithSlideFromRightAndFade(editor);
        }

        /// <summary>收起右侧详情：编辑器淡出后由 helper 置 Collapsed，占位提示恢复。</summary>
        private void HideEditor(UIElement editor)
        {
            _detailRatio = EmptyRatio;
            ApplyDetailWidth(_detailRatio);
            AnimationsHelper.HideWithFadeOut(editor);
            EmptyStatePanel.Visibility = Visibility.Visible;
        }

        private void UpdateCategoryUI()
        {
            NavigationListBox.Visibility = _category == 0 ? Visibility.Visible : Visibility.Collapsed;
            StateGroupsListBox.Visibility = _category == 1 ? Visibility.Visible : Visibility.Collapsed;
            ActionGroupsListBox.Visibility = _category == 2 ? Visibility.Visible : Visibility.Collapsed;

            BtnAddItem.Content = _category switch
            {
                1 => Properties.AutomationStrings.AutoWf_AddStateGroup,
                2 => Properties.AutomationStrings.AutoWf_AddActionGroup,
                _ => Properties.AutomationStrings.AutoWf_AddWorkflow
            };

            if (_category != 0) NavigationListBox.SelectedItem = null;
            if (_category != 1) StateGroupsListBox.SelectedItem = null;
            if (_category != 2) ActionGroupsListBox.SelectedItem = null;

            HideAllEditors();

            _detailRatio = EmptyRatio;
            ApplyDetailWidth(_detailRatio);

            // 重新进入页面时当前分类的 SelectedItem 会被保留，而选中项未变时
            // SelectionChanged 不会触发，HideAllEditors 收起的面板就再也回不来
            // （表现为：列表高亮但右栏空白）。这里按现有选中项主动恢复。
            var active = _category == 1 ? StateGroupsListBox
                : _category == 2 ? ActionGroupsListBox
                : NavigationListBox;

            if (active.SelectedItem != null)
            {
                // 不自动选中任何项：进入页面与切换分类均保持空状态，等待用户主动点选
                switch (_category)
                {
                    case 1: StateGroupsListBox_SelectionChanged(active, null); break;
                    case 2: ActionGroupsListBox_SelectionChanged(active, null); break;
                    default: NavigationListBox_SelectionChanged(active, null); break;
                }
            }
            else
            {
                EmptyStatePanel.Visibility = Visibility.Visible;
            }
        }

        private void NavigationListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded) return;
            if (_category != 0) return;

            SelectedWorkflow = NavigationListBox.SelectedItem as Workflow;

            if (NavigationListBox.SelectedItem is Workflow workflow)
            {
                ShowEditor(WorkflowEditorPanel);
                StateGroupEditorPanel.Visibility = Visibility.Collapsed;
                ActionGroupEditorPanel.Visibility = Visibility.Collapsed;
                UpdateEditorBindings(workflow);
            }
            else
            {
                HideEditor(WorkflowEditorPanel);
            }
        }

        private void StateGroupsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded || _category != 1) return;

            if (StateGroupsListBox.SelectedItem is StateGroup group)
            {
                StateGroupEditorPanel.DataContext = group;
                StateGroupRulesetEditor.Ruleset = group.Ruleset;
                _isUpdatingEditor = true;
                TextBoxStateGroupName.Text = group.Name;
                _isUpdatingEditor = false;

                WorkflowEditorPanel.Visibility = Visibility.Collapsed;
                ActionGroupEditorPanel.Visibility = Visibility.Collapsed;
                ShowEditor(StateGroupEditorPanel);
                StateGroupRulesetEditor.RefreshState();
            }
            else
            {
                HideEditor(StateGroupEditorPanel);
            }
        }

        private void ActionGroupsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded || _category != 2) return;

            if (ActionGroupsListBox.SelectedItem is ActionSet set)
            {
                ActionGroupEditorPanel.DataContext = set;
                ActionGroupActionSetEditor.ActionSet = set;
                _isUpdatingEditor = true;
                TextBoxActionGroupName.Text = set.Name;
                _isUpdatingEditor = false;

                WorkflowEditorPanel.Visibility = Visibility.Collapsed;
                StateGroupEditorPanel.Visibility = Visibility.Collapsed;
                ShowEditor(ActionGroupEditorPanel);
            }
            else
            {
                HideEditor(ActionGroupEditorPanel);
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

        private void BtnAddItem_Click(object sender, RoutedEventArgs e)
        {
            switch (_category)
            {
                case 1: AddStateGroup(); break;
                case 2: AddActionGroup(); break;
                default: AddWorkflow(); break;
            }
        }

        private void AddWorkflow()
        {
            var workflow = new Workflow();
            workflow.ActionSet.Name = string.Format(Properties.AutomationStrings.Automation_Workflow_DefaultNameFormat, Service.Workflows.Count + 1);
            workflow.Ruleset.Groups.Add(new RuleGroup
            {
                Rules = new ObservableCollection<Rule> { new Rule() }
            });
            Service.Workflows.Add(workflow);
            Service.SaveConfig("AddWorkflow");
            NavigationListBox.SelectedItem = workflow;
        }

        private void AddStateGroup()
        {
            var group = new StateGroup
            {
                Name = string.Format("{0} {1}", Properties.AutomationStrings.Automation_DefaultStateGroupName, Service.StateGroups.Count + 1)
            };
            group.Ruleset.Groups.Add(new RuleGroup
            {
                Rules = new ObservableCollection<Rule> { new Rule() }
            });
            Service.StateGroups.Add(group);
            Service.SaveConfig("AddStateGroup");
            StateGroupsListBox.SelectedItem = group;
        }

        private void AddActionGroup()
        {
            var group = new ActionSet
            {
                Name = string.Format("{0} {1}", Properties.AutomationStrings.Automation_DefaultActionGroupName, Service.ActionGroups.Count + 1)
            };
            Service.ActionGroups.Add(group);
            Service.SaveConfig("AddActionGroup");
            ActionGroupsListBox.SelectedItem = group;
        }

        private void BtnRemoveWorkflow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not Workflow workflow) return;
            Service.Workflows.Remove(workflow);
            Service.SaveConfig("RemoveWorkflow");
        }

        private void BtnDuplicateWorkflow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not Workflow source) return;
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(source);
            var copy = Newtonsoft.Json.JsonConvert.DeserializeObject<Workflow>(json);
            if (copy != null)
            {
                copy.ActionSet.Name += Properties.AutomationStrings.Automation_Workflow_CopySuffix;
                Service.Workflows.Add(copy);
                Service.SaveConfig("DuplicateWorkflow");
                NavigationListBox.SelectedItem = copy;
            }
        }

        private void BtnRemoveStateGroup_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not StateGroup group) return;
            Service.StateGroups.Remove(group);
            Service.SaveConfig("RemoveStateGroup");
        }

        private void BtnDuplicateStateGroup_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not StateGroup source) return;
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(source);
            var copy = Newtonsoft.Json.JsonConvert.DeserializeObject<StateGroup>(json);
            if (copy != null)
            {
                copy.Guid = Guid.NewGuid().ToString();
                copy.Name += Properties.AutomationStrings.Automation_Workflow_CopySuffix;
                Service.StateGroups.Add(copy);
                Service.SaveConfig("DuplicateStateGroup");
                StateGroupsListBox.SelectedItem = copy;
            }
        }

        private void BtnRemoveActionGroup_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not ActionSet set) return;
            Service.ActionGroups.Remove(set);
            Service.SaveConfig("RemoveActionGroup");
        }

        private void BtnDuplicateActionGroup_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not ActionSet source) return;
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(source);
            var copy = Newtonsoft.Json.JsonConvert.DeserializeObject<ActionSet>(json);
            if (copy != null)
            {
                copy.Guid = Guid.NewGuid().ToString();
                copy.Name += Properties.AutomationStrings.Automation_Workflow_CopySuffix;
                Service.ActionGroups.Add(copy);
                Service.SaveConfig("DuplicateActionGroup");
                ActionGroupsListBox.SelectedItem = copy;
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

                // 条件与行动（显式赋值，避免 DataContext 为页面自身时绑定失败）
                RulesetEditor.Ruleset = workflow.Ruleset;
                ActionSetEditor.ActionSet = workflow.ActionSet;

                UpdateConditionVisibility(workflow.IsConditionEnabled);
                UpdateRevertHintVisibility(workflow.ActionSet.IsRevertEnabled);
            }
            finally
            {
                _isUpdatingEditor = false;
            }
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

        private void TextBoxStateGroupName_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingEditor) return;
            if (StateGroupEditorPanel.DataContext is StateGroup group)
            {
                group.Name = TextBoxStateGroupName.Text;
                Service.SaveConfig("StateGroupNameChanged");
            }
        }

        private void TextBoxActionGroupName_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingEditor) return;
            if (ActionGroupEditorPanel.DataContext is ActionSet set)
            {
                set.Name = TextBoxActionGroupName.Text;
                Service.SaveConfig("ActionGroupNameChanged");
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
                var newId = cb.SelectedValue as string ?? "";
                if (trigger.Id == newId) return;
                trigger.Id = newId;
                Service.SaveConfig("TriggerTypeChanged");
            }
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

        private void BtnInvokeActionGroup_Click(object sender, RoutedEventArgs e)
        {
            if (ActionGroupEditorPanel.DataContext is not ActionSet set) return;
            Service.ActionService.Invoke(set);
            Service.SaveConfig("ManualInvokeActionGroup");
        }

        private void BtnRevertActionGroup_Click(object sender, RoutedEventArgs e)
        {
            if (ActionGroupEditorPanel.DataContext is not ActionSet set) return;
            Service.ActionService.Revert(set);
            Service.SaveConfig("ManualRevertActionGroup");
        }

        private void RulesetEditor_SettingsChanged(object sender, EventArgs e)
        {
            Service.SaveConfig("RulesetChanged");
        }

        private void ActionSetEditor_SettingsChanged(object sender, EventArgs e)
        {
            Service.SaveConfig("ActionSetChanged");
        }

        private void GroupSettingsChanged(object sender, EventArgs e)
        {
            Service.SaveConfig("GroupSettingsChanged");
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

            var groupAttr = property.GetCustomAttribute<GroupSelectorAttribute>();
            if (groupAttr != null && propertyType == typeof(string))
            {
                return CreateGroupSelector(settingsObject, property, groupAttr, label);
            }

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

        private FrameworkElement CreateGroupSelector(object settingsObject, PropertyInfo property, GroupSelectorAttribute attr, string label)
        {
            // 横向排列：与同行其他 ComboBox 共享一条基线，避免上下堆叠导致的错位
            var container = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            container.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 11,
                Opacity = 0.72,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            });

            var comboBox = new ComboBox
            {
                MinWidth = 160,
                MaxWidth = 260,
                Height = 30,
                VerticalAlignment = VerticalAlignment.Center,
                DisplayMemberPath = "Name",
                SelectedValuePath = "Guid"
            };

            var service = AutomationBootstrap.Service;
            System.Collections.IEnumerable items = null;
            if (service != null)
            {
                items = attr.Kind == GroupKind.State
                    ? (System.Collections.IEnumerable)service.StateGroups
                    : service.ActionGroups;
            }
            comboBox.ItemsSource = items;
            comboBox.SelectedValue = property.GetValue(settingsObject) as string ?? "";

            comboBox.SelectionChanged += (_, _) =>
            {
                var id = comboBox.SelectedValue as string ?? "";
                if ((property.GetValue(settingsObject) as string ?? "") == id) return;
                property.SetValue(settingsObject, id);
                RaiseSettingsChanged();
            };

            container.Children.Add(comboBox);
            return container;
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
                "ProcessName" => Properties.AutomationStrings.Automation_Field_ProcessName,
                "TitleContains" => Properties.AutomationStrings.Automation_Field_TitleContains,
                "IgnoreCase" => Properties.AutomationStrings.Automation_Field_IgnoreCase,
                "Type" => Properties.AutomationStrings.Automation_Field_NotificationType,
                "Message" => Properties.AutomationStrings.Automation_Field_NotificationContent,
                "SavePath" => Properties.AutomationStrings.Automation_Field_SavePath,
                "SaveAsXml" => Properties.AutomationStrings.Automation_Field_SaveAsXml,
                "Fold" => Properties.AutomationStrings.Automation_Field_Fold,
                "EnterAnnotation" => Properties.AutomationStrings.Automation_Field_EnterAnnotation,
                "Topmost" => Properties.AutomationStrings.Automation_Field_Topmost,
                "GroupId" => Properties.AutomationStrings.Automation_Field_GroupId,
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
