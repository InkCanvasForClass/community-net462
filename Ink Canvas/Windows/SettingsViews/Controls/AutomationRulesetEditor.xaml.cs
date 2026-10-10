using Ink_Canvas.WorkflowAutomation;
using Ink_Canvas.WorkflowAutomation.Enums;
using Ink_Canvas.WorkflowAutomation.Models;
using Ink_Canvas.WorkflowAutomation.Services;
using Ink_Canvas.Windows.SettingsViews.Pages;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Ink_Canvas.Windows.SettingsViews.Controls
{
    /// <summary>
    /// 规则集（条件）编辑控件，可复用于工作流与状态组。
    /// </summary>
    public partial class AutomationRulesetEditor : UserControl
    {
        public static readonly DependencyProperty RulesetProperty = DependencyProperty.Register(
            nameof(Ruleset), typeof(Ruleset), typeof(AutomationRulesetEditor),
            new PropertyMetadata(null, OnRulesetChanged));

        public Ruleset Ruleset
        {
            get => (Ruleset)GetValue(RulesetProperty);
            set => SetValue(RulesetProperty, value);
        }

        /// <summary>
        /// 编辑内容发生变化（页面据此保存配置）。
        /// </summary>
        public event EventHandler SettingsChanged;

        private bool _isLoaded;

        public AutomationRulesetEditor()
        {
            InitializeComponent();
            Loaded += (_, _) => { _isLoaded = true; RefreshState(); };
            Unloaded += (_, _) => { _isLoaded = false; };
        }

        private static void OnRulesetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not AutomationRulesetEditor editor) return;
            if (editor.Root != null) editor.Root.DataContext = e.NewValue;
            editor.RefreshState();
        }

        private void RaiseChanged()
        {
            if (!_isLoaded) return;
            RefreshState();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        internal void RefreshState()
        {
            if (EllipseRulesetState == null) return;
            var ruleset = Ruleset;
            if (ruleset == null)
            {
                EllipseRulesetState.Fill = Brushes.DarkGray;
                return;
            }

            try
            {
                AutomationBootstrap.Service?.RulesetService?.IsRulesetSatisfied(ruleset);
            }
            catch { }

            EllipseRulesetState.Fill = ruleset.State switch
            {
                2 => Brushes.Green,
                1 => Brushes.IndianRed,
                _ => Brushes.DarkGray
            };
        }

        private void ComboBoxRulesetMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded) return;
            if (Ruleset is Ruleset ruleset)
            {
                ruleset.Mode = ComboBoxRulesetMode.SelectedIndex == 0 ? RulesetLogicalMode.Or : RulesetLogicalMode.And;
                RaiseChanged();
            }
        }

        private void CheckBoxRulesetReversed_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            RaiseChanged();
        }

        private void BtnAddRuleGroup_Click(object sender, RoutedEventArgs e)
        {
            if (Ruleset is not Ruleset ruleset) return;
            ruleset.Groups.Add(new RuleGroup
            {
                Rules = new ObservableCollection<Rule> { new Rule() }
            });
            RaiseChanged();
        }

        private void BtnDeleteGroup_Click(object sender, RoutedEventArgs e)
        {
            if (Ruleset is not Ruleset ruleset) return;
            if (sender is not Button btn || btn.Tag is not RuleGroup group) return;
            ruleset.Groups.Remove(group);
            RaiseChanged();
        }

        private void BtnDuplicateGroup_Click(object sender, RoutedEventArgs e)
        {
            if (Ruleset is not Ruleset ruleset) return;
            if (sender is not Button btn || btn.Tag is not RuleGroup source) return;
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(source);
            var copy = Newtonsoft.Json.JsonConvert.DeserializeObject<RuleGroup>(json);
            if (copy != null)
            {
                ruleset.Groups.Add(copy);
                RaiseChanged();
            }
        }

        private void ComboBoxGroupMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded) return;
            if (sender is ComboBox cb && cb.Tag is RuleGroup group)
            {
                group.Mode = cb.SelectedIndex == 0 ? RulesetLogicalMode.Or : RulesetLogicalMode.And;
                RaiseChanged();
            }
        }

        private void CheckBoxGroupReversed_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            RaiseChanged();
        }

        private void CheckBoxGroupEnabled_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            RaiseChanged();
        }

        private void BtnAddRuleToGroup_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not RuleGroup group) return;
            var firstRule = AutomationRegistry.RegisteredRules.FirstOrDefault();
            group.Rules.Add(new Rule { Id = firstRule.Key ?? "" });
            RaiseChanged();
        }

        private void BtnRemoveRule_Click(object sender, RoutedEventArgs e)
        {
            if (Ruleset is not Ruleset ruleset) return;
            if (sender is not Button btn || btn.Tag is not Rule rule) return;
            foreach (var group in ruleset.Groups)
            {
                if (group.Rules.Contains(rule))
                {
                    group.Rules.Remove(rule);
                    RaiseChanged();
                    break;
                }
            }
        }

        private void ComboBoxRuleType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded) return;
            if (sender is ComboBox cb && cb.Tag is Rule rule)
            {
                var newId = cb.SelectedValue as string ?? "";
                if (rule.Id == newId) return;
                rule.Id = newId;
                EnsureRuleSettingsInstance(rule);
                RaiseChanged();
            }
        }

        private void CheckBoxRuleReversed_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            RaiseChanged();
        }

        private void RuleSettingsPresenter_SettingsChanged(object sender, EventArgs e)
        {
            RaiseChanged();
        }

        private static void EnsureRuleSettingsInstance(Rule rule)
        {
            if (!AutomationRegistry.RegisteredRules.TryGetValue(rule.Id, out var info))
                return;

            rule.Settings = AutomationWorkflowPage.EnsureSettingsInstance(rule.Settings, info.SettingsType);
        }
    }
}
