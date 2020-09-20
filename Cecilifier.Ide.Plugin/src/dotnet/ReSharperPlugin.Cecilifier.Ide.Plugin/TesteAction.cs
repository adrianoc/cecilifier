using JetBrains.Application.DataContext;
using JetBrains.Application.UI.Actions;
using JetBrains.Application.UI.ActionsRevised.Menu;
using JetBrains.Util;

namespace SampleReSharperPlugin
{
    [Action("ActionShowMessageBox", "Show message box", Id = 543210)]
    public class ShowMessageBoxAction : BaseAction, IExecutableAction
    {
        protected override void RunAction(IDataContext context, DelegateExecute nextExecute)
        {
            var solution = context.GetData(JetBrains.ProjectModel.DataContext.ProjectModelDataConstants.SOLUTION);
            MessageBox.ShowInfo(solution?.SolutionFile != null
                ? $"{solution.SolutionFile?.Name} solution is opened"
                : "No solution is opened");
        }
    }
    
    public abstract class BaseAction : IExecutableAction
    {
        public bool Update(IDataContext context, ActionPresentation presentation, DelegateUpdate nextUpdate)
        {
            return true; // function result indicates whether the menu item is enabled or disabled
        }

        public void Execute(IDataContext context, DelegateExecute nextExecute)
        {
            RunAction(context, nextExecute);
        }

        protected abstract void RunAction(IDataContext context, DelegateExecute nextExecute);
    }
}
