using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using JetBrains.Application.DataContext;
using JetBrains.Application.UI.Actions;
using JetBrains.Application.UI.ActionsRevised.Menu;
using JetBrains.Application.UI.ActionSystem.ActionsRevised.Menu;
using JetBrains.DocumentManagers.Transactions;
using JetBrains.IDE;
using JetBrains.ProjectModel;
using JetBrains.ProjectModel.DataContext;
using JetBrains.ReSharper.Feature.Services.Util;
using JetBrains.ReSharper.Psi.DataContext;
using JetBrains.ReSharper.Psi.Files;
using JetBrains.Util;
using JetBrains.Util.Logging;

namespace ReSharperPlugin.Cecilifier.Ide.Plugin
{
    [Action("CecilifierAction", "Run cecilifier on current document.")]
    public class CecilifierAction : IActionWithExecuteRequirement, IExecutableAction
    {
        public IActionRequirement GetRequirement(IDataContext dataContext)
        {
            return CommitAllDocumentsRequirement.TryGetInstance(dataContext);
        }

        public bool Update(IDataContext context, ActionPresentation presentation, DelegateUpdate nextUpdate)
        {
            return true;
        }

        public void Execute(IDataContext context, DelegateExecute nextExecute)
        {
            var docView = context.GetData(PsiDataConstants.PSI_DOCUMENT_VIEW);
            if (docView != null)
            {
                var toBeCecilified = docView.DefaultSourceFile.SortedSourceFiles.AggregateString((acc, curr) => acc.Append(curr.Document.GetText()));
            
                var referenceListFilePath = Path.GetTempFileName();
                WriteAssemblyReferenceFilePaths(context, referenceListFilePath);
            
                var filePath = Path.GetTempFileName();
                File.WriteAllText(filePath, toBeCecilified);
            
                var psi = new ProcessStartInfo();
                psi.RedirectStandardError = true;
                psi.RedirectStandardOutput = true;
                psi.FileName = "dotnet";
                psi.Arguments = $"/home/adriano/Development/Adriano/study/DotNet/Cecilifier/Cecilifier.App/bin/Debug/netcoreapp3.1/Cecilifier.App.dll {filePath} {referenceListFilePath}";

                //TODO: figure out correct path.
                var cecilifierProcess = Process.Start(psi);
                if (!cecilifierProcess.WaitForExit(5000))
                {
                    MessageBox.ShowError("Cecilifier process is taking to long to run (more than 5s)", "Timeout");
                    return;
                }

                if (cecilifierProcess.ExitCode != 0)
                {
                    var stderr = cecilifierProcess.StandardError.ReadToEnd();
                    //Logger.Root.Log(LoggingLevel.ERROR, $"Failed to run cecilifier:{Environment.NewLine}{stderr}");

                    //TODO: log to rider instead.
                    MessageBox.ShowError($"Cecilifier process returned error code {cecilifierProcess.ExitCode}{Environment.NewLine}{stderr}", "Error");
                    return;
                }
                
                var cecilifiedCode = File.ReadAllText(filePath);
                var proj = context.GetData(ProjectModelDataConstants.PROJECT);
                var cecilifiedProjectFile = AddNewItemHelper.AddFile(
                     proj.ProjectFile.ParentFolder, 
                     $"{Path.GetFileNameWithoutExtension(docView.DefaultSourceFile.SortedSourceFiles.FirstNotNull().Name)}.Cecilified.cs", 
                     cecilifiedCode,
                     new FileCreationParameters(BuildAction.NONE));

                EditorManager.GetInstance(proj.GetSolution()).OpenProjectFileAsync(cecilifiedProjectFile, OpenFileOptions.DefaultActivate);
            }
            else
            {
                MessageBox.ShowError("Cannot retrieve documents...", "Info!");
            }

        }

        private static void WriteAssemblyReferenceFilePaths(IDataContext context, string referenceListFilePath)
        {
            var st = new StringBuilder();
            var project = context.GetData(ProjectModelDataConstants.PROJECT);
            var refs = project.GetAssemblyReferences(project.TargetFrameworkIds.FirstNotNull());
            foreach (var referencedAssembly in refs)
            {
                st.AppendLine($"{referencedAssembly.ReferenceTarget.HintLocation}");
            }

            File.WriteAllText(referenceListFilePath, st.ToString());
        }
    }
}
