namespace Cecilifier
{
	partial class Form1
	{
		/// <summary>
		/// Required designer variable.
		/// </summary>
		private System.ComponentModel.IContainer components = null;

		/// <summary>
		/// Clean up any resources being used.
		/// </summary>
		/// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
		protected override void Dispose(bool disposing)
		{
			if (disposing && (components != null))
			{
				components.Dispose();
			}
			base.Dispose(disposing);
		}

		#region Windows Form Designer generated code

		/// <summary>
		/// Required method for Designer support - do not modify
		/// the contents of this method with the code editor.
		/// </summary>
		private void InitializeComponent()
		{
			System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Form1));
			this.toolStripContainer1 = new System.Windows.Forms.ToolStripContainer();
			this.toolStrip1 = new System.Windows.Forms.ToolStrip();
			this.splitContainer1 = new System.Windows.Forms.SplitContainer();
			this.toolStripRun = new System.Windows.Forms.ToolStripButton();
			this.textEditorControl1 = new ICSharpCode.TextEditor.TextEditorControl();
			this.textEditorControl2 = new ICSharpCode.TextEditor.TextEditorControl();
			this.toolStripContainer1.ContentPanel.SuspendLayout();
			this.toolStripContainer1.TopToolStripPanel.SuspendLayout();
			this.toolStripContainer1.SuspendLayout();
			this.toolStrip1.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).BeginInit();
			this.splitContainer1.Panel1.SuspendLayout();
			this.splitContainer1.Panel2.SuspendLayout();
			this.splitContainer1.SuspendLayout();
			this.SuspendLayout();
			// 
			// toolStripContainer1
			// 
			// 
			// toolStripContainer1.ContentPanel
			// 
			this.toolStripContainer1.ContentPanel.Controls.Add(this.splitContainer1);
			this.toolStripContainer1.ContentPanel.Size = new System.Drawing.Size(1111, 680);
			this.toolStripContainer1.Dock = System.Windows.Forms.DockStyle.Fill;
			this.toolStripContainer1.Location = new System.Drawing.Point(0, 0);
			this.toolStripContainer1.Name = "toolStripContainer1";
			this.toolStripContainer1.Size = new System.Drawing.Size(1111, 705);
			this.toolStripContainer1.TabIndex = 0;
			this.toolStripContainer1.Text = "toolStripContainer1";
			// 
			// toolStripContainer1.TopToolStripPanel
			// 
			this.toolStripContainer1.TopToolStripPanel.Controls.Add(this.toolStrip1);
			// 
			// toolStrip1
			// 
			this.toolStrip1.Dock = System.Windows.Forms.DockStyle.None;
			this.toolStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.toolStripRun});
			this.toolStrip1.Location = new System.Drawing.Point(3, 0);
			this.toolStrip1.Name = "toolStrip1";
			this.toolStrip1.Size = new System.Drawing.Size(44, 25);
			this.toolStrip1.TabIndex = 0;
			// 
			// splitContainer1
			// 
			this.splitContainer1.Dock = System.Windows.Forms.DockStyle.Fill;
			this.splitContainer1.Location = new System.Drawing.Point(0, 0);
			this.splitContainer1.Name = "splitContainer1";
			this.splitContainer1.Orientation = System.Windows.Forms.Orientation.Horizontal;
			// 
			// splitContainer1.Panel1
			// 
			this.splitContainer1.Panel1.Controls.Add(this.textEditorControl1);
			// 
			// splitContainer1.Panel2
			// 
			this.splitContainer1.Panel2.Controls.Add(this.textEditorControl2);
			this.splitContainer1.Size = new System.Drawing.Size(1111, 680);
			this.splitContainer1.SplitterDistance = 370;
			this.splitContainer1.TabIndex = 0;
			// 
			// toolStripRun
			// 
			this.toolStripRun.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
			this.toolStripRun.Image = ((System.Drawing.Image)(resources.GetObject("toolStripRun.Image")));
			this.toolStripRun.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.toolStripRun.Name = "toolStripRun";
			this.toolStripRun.Size = new System.Drawing.Size(32, 22);
			this.toolStripRun.Text = "Run";
			this.toolStripRun.Click += new System.EventHandler(this.toolStripRun_Click);
			// 
			// textEditorControl1
			// 
			this.textEditorControl1.Dock = System.Windows.Forms.DockStyle.Fill;
			this.textEditorControl1.IsReadOnly = false;
			this.textEditorControl1.Location = new System.Drawing.Point(0, 0);
			this.textEditorControl1.Name = "textEditorControl1";
			this.textEditorControl1.Size = new System.Drawing.Size(1111, 370);
			this.textEditorControl1.TabIndex = 0;
			this.textEditorControl1.Text = "public partial class Teste : object\r\n{\r\n\tprivate int i, j;\r\n\tprotected string n;\r" +
    "\n}\r\n\r\nclass Gen<T>\r\n{\r\n}\r\n";
			// 
			// textEditorControl2
			// 
			this.textEditorControl2.Dock = System.Windows.Forms.DockStyle.Fill;
			this.textEditorControl2.IsReadOnly = false;
			this.textEditorControl2.Location = new System.Drawing.Point(0, 0);
			this.textEditorControl2.Name = "textEditorControl2";
			this.textEditorControl2.Size = new System.Drawing.Size(1111, 306);
			this.textEditorControl2.TabIndex = 0;
			this.textEditorControl2.Text = "textEditorControl2";
			// 
			// Form1
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(1111, 705);
			this.Controls.Add(this.toolStripContainer1);
			this.Name = "Form1";
			this.Text = "Form1";
			this.toolStripContainer1.ContentPanel.ResumeLayout(false);
			this.toolStripContainer1.TopToolStripPanel.ResumeLayout(false);
			this.toolStripContainer1.TopToolStripPanel.PerformLayout();
			this.toolStripContainer1.ResumeLayout(false);
			this.toolStripContainer1.PerformLayout();
			this.toolStrip1.ResumeLayout(false);
			this.toolStrip1.PerformLayout();
			this.splitContainer1.Panel1.ResumeLayout(false);
			this.splitContainer1.Panel2.ResumeLayout(false);
			((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).EndInit();
			this.splitContainer1.ResumeLayout(false);
			this.ResumeLayout(false);

		}

		#endregion

		private System.Windows.Forms.ToolStripContainer toolStripContainer1;
		private System.Windows.Forms.SplitContainer splitContainer1;
		private System.Windows.Forms.ToolStrip toolStrip1;
		private System.Windows.Forms.ToolStripButton toolStripRun;
		private ICSharpCode.TextEditor.TextEditorControl textEditorControl1;
		private ICSharpCode.TextEditor.TextEditorControl textEditorControl2;

	}
}

