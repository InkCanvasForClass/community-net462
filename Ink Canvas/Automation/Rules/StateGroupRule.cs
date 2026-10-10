using Ink_Canvas.WorkflowAutomation.Models;
using Ink_Canvas.WorkflowAutomation.Services;
using System.Linq;

namespace Ink_Canvas.WorkflowAutomation.Rules
{
    /// <summary>
    /// 「状态组满足」规则设置
    /// </summary>
    public class StateGroupRuleSettings
    {
        /// <summary>
        /// 引用的状态组 Guid
        /// </summary>
        [GroupSelector(GroupKind.State)]
        public string GroupId { get; set; } = "";
    }

    /// <summary>
    /// 判断指定状态组（复用规则集）是否满足的规则。
    /// </summary>
    public static class StateGroupRule
    {
        public const string RuleId = "inkcanvas.stategroup";

        public static RuleRegistryInfo Register()
        {
            var info = new RuleRegistryInfo(RuleId, Properties.AutomationStrings.Automation_Rule_StateGroup, "TagMultiple")
            {
                SettingsType = typeof(StateGroupRuleSettings)
            };
            info.Handle = Evaluate;
            return info;
        }

        public static bool Evaluate(object settings)
        {
            var s = settings as StateGroupRuleSettings;
            if (s == null || string.IsNullOrEmpty(s.GroupId)) return false;

            if (!AutomationGroupGuard.TryEnter(s.GroupId)) return false;
            try
            {
                var service = AutomationBootstrap.Service;
                var group = service?.StateGroups?.FirstOrDefault(g => g.Guid == s.GroupId);
                if (group == null) return false;
                return service.RulesetService.IsRulesetSatisfied(group.Ruleset);
            }
            catch
            {
                return false;
            }
            finally
            {
                AutomationGroupGuard.Exit(s.GroupId);
            }
        }
    }
}
