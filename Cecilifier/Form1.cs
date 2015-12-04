using System;
using System.IO;
using System.Text;
using System.Windows.Forms;
using ICSharpCode.TextEditor.Document;
using Microsoft.CodeAnalysis.CSharp;

namespace Cecilifier
{
	public partial class Form1 : Form
	{
		public Form1()
		{
			InitializeComponent();

			string dir = "resources";
			
			if (Directory.Exists(dir))
			{
				var syntaxProvider = new FileSyntaxModeProvider(dir);
				HighlightingManager.Manager.AddSyntaxModeFileProvider(syntaxProvider);
				textEditorControl1.SetHighlighting("C#");
				textEditorControl2.SetHighlighting("C#");
			}
		}

		private void toolStripRun_Click(object sender, EventArgs e)
		{

			//var ast = CSharpSyntaxTree.ParseText(textEditorControl1.Text);
			//            var syntaxTree = SyntaxTree.ParseCompilationUnit(textEditorControl1.Text);
			//            var syntaxTree2 = SyntaxTree.ParseCompilationUnit(@"
			//
			//class Gen<T>
			//{
			//}
			//
			//public partial class Teste : object
			//{
			//	private int i, j;
			//	protected string n;
			//}
			//");

			//            Debug.WriteLine("       == : {0}", syntaxTree == syntaxTree2);
			//            Debug.WriteLine("    EQUALS: {0}", syntaxTree.Equals(syntaxTree2));
			//            Debug.WriteLine("Equivalent: {0}", Syntax.AreEquivalent(syntaxTree, syntaxTree2, false));
			//            var usingDirective = Syntax.UsingDirective(name: Syntax.ParseName("System"));

			//var visitor = new ASTVisitor();
			//visitor.Visit(syntaxTree.Root);

			var sourceStream = new MemoryStream(Encoding.UTF8.GetBytes(textEditorControl1.Text));
			textEditorControl2.Text = Core.Cecilifier.Process(sourceStream).ReadToEnd();
		}


	}
}
