using System.Collections.ObjectModel;

namespace Ink_Canvas.WorkflowAutomation.Models
{
    /// <summary>
    /// 自动化配置文件根对象。旧版本配置文件为裸的工作流数组，加载时兼容处理。
    /// </summary>
    public class AutomationConfig
    {
        public ObservableCollection<Workflow> Workflows { get; set; } = new ObservableCollection<Workflow>();
        public ObservableCollection<StateGroup> StateGroups { get; set; } = new ObservableCollection<StateGroup>();
        public ObservableCollection<ActionSet> ActionGroups { get; set; } = new ObservableCollection<ActionSet>();
    }
}
