using CommunityToolkit.Mvvm.ComponentModel;

namespace Ink_Canvas.WorkflowAutomation.Models
{
    /// <summary>
    /// 代表一个可复用的状态组（命名的规则集），可在正常自动化中通过「状态组满足」规则调用。
    /// </summary>
    public class StateGroup : ObservableObject
    {
        private string _guid = System.Guid.NewGuid().ToString();
        private string _name = Properties.AutomationStrings.Automation_DefaultStateGroupName;
        private Ruleset _ruleset = new();

        /// <summary>
        /// 状态组 Guid，用于标识与引用。
        /// </summary>
        public string Guid
        {
            get => _guid;
            set
            {
                if (value == _guid) return;
                _guid = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 名称
        /// </summary>
        public string Name
        {
            get => _name;
            set
            {
                if (value == _name) return;
                _name = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 规则集
        /// </summary>
        public Ruleset Ruleset
        {
            get => _ruleset;
            set
            {
                if (value == _ruleset) return;
                _ruleset = value;
                OnPropertyChanged();
            }
        }
    }
}
