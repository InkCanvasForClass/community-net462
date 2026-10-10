using Ink_Canvas.WorkflowAutomation.Abstractions;
using Ink_Canvas.WorkflowAutomation.Actions;
using Ink_Canvas.WorkflowAutomation.Services;
using System.Linq;

namespace Ink_Canvas.WorkflowAutomation.ActionHandlers
{
    /// <summary>
    /// 「执行动作组」行动处理器：调用对应动作组的触发/恢复。
    /// </summary>
    public class RunActionGroupActionHandler
    {
        public RunActionGroupActionHandler(IActionService actionService)
        {
            actionService.RegisterActionHandler(RunActionGroupAction.ActionId,
                (settings, guid) => Run(actionService, settings, revert: false));
            actionService.RegisterRevertHandler(RunActionGroupAction.ActionId,
                (settings, guid) => Run(actionService, settings, revert: true));
        }

        private static void Run(IActionService actionService, object settings, bool revert)
        {
            var s = settings as RunActionGroupActionSettings;
            if (s == null || string.IsNullOrEmpty(s.GroupId)) return;

            var service = AutomationBootstrap.Service;
            var group = service?.ActionGroups?.FirstOrDefault(g => g.Guid == s.GroupId);
            if (group == null) return;

            if (!AutomationGroupGuard.TryEnter(s.GroupId)) return;
            try
            {
                if (revert)
                    actionService.Revert(group);
                else
                    actionService.Invoke(group);
            }
            finally
            {
                AutomationGroupGuard.Exit(s.GroupId);
            }
        }
    }
}
