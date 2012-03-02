using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Cecilifier.Core.Extensions;
using Roslyn.Compilers;
using Roslyn.Compilers.CSharp;

namespace Cecilifier.Core.AST
{
	class TestVisitor : SyntaxWalker
	{
		protected override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
		{
			Console.Write("{0}", node);
		}

		protected override void VisitArgumentList(ArgumentListSyntax node)
		{
			Console.Write("(");
			base.VisitArgumentList(node);
			Console.WriteLine(")");
		}
		protected override void VisitArgument(ArgumentSyntax node)
		{
			Console.Write("{0}", node);
			base.VisitArgument(node);
		}
	}

	class CecilifierVisitor : SyntaxWalker
	{
		public CecilifierVisitor(SemanticModel semanticModel)
		{
			this.semanticModel = semanticModel;
		}

		protected override void VisitInvocationExpression(InvocationExpressionSyntax node)
		{
			//new TestVisitor().Visit(node);
			AddCecilExpression(" IE: {0}", node);
			base.VisitInvocationExpression(node);
		}

		protected override void VisitBlock(BlockSyntax node)
		{
			var parent = nodeStack.Peek().SyntaxNode;
			if (parent.Kind == SyntaxKind.MethodDeclaration)
			{
				Console.WriteLine("------ METODY BLOCK --------> {0}",node);
			}

			base.VisitBlock(node);
		}

		protected override void VisitNamespaceDeclaration(NamespaceDeclarationSyntax node)
		{
			try
			{
				var namespaceHierarchy = node.AncestorsAndSelf().OfType<NamespaceDeclarationSyntax>().Reverse();
				currentNameSpace = namespaceHierarchy.Aggregate("",(acc, curr) => acc + "." + curr.Name.GetText());

				currentNameSpace = currentNameSpace.StartsWith(".") ? currentNameSpace.Substring(1) : currentNameSpace;
				base.VisitNamespaceDeclaration(node);
			}
			finally
			{
				currentNameSpace = string.Empty;
			}
		}

		protected override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
		{
			HandleInterfaceDeclaration(node);
			base.VisitInterfaceDeclaration(node);
		}

		protected override void VisitClassDeclaration(ClassDeclarationSyntax node)
		{
			HandleClassDeclaration(node, ProcessBase(node));
			base.VisitClassDeclaration(node);
		}

		protected override void VisitFieldDeclaration(FieldDeclarationSyntax node)
		{
			foreach (var field in node.Declaration.Variables)
			{
				var fieldAttributes = FieldModifiersToCecil(node);

				var type = ResolveLocalVariable(node.Declaration.Type.PlainName) ?? ResolveType(node.Declaration.Type);
				var fieldId = string.Format("ft{0}", NextLocalVariableId());
				var fieldType = ProcessRequiredModifiers(node, type) ?? type;
				var fieldDeclaration = string.Format("var {0} = new FieldDefinition(\"{1}\", {2}, {3});", 
																fieldId, 
																field.Identifier.Value, 
																fieldAttributes, 
																fieldType);
				
				AddCecilExpression(fieldDeclaration);
				AddCecilExpression("{0}.Fields.Add({1});", LocalVariableNameForId(currentTypeId), fieldId);
			}

			base.VisitFieldDeclaration(node);
		}

		protected override void VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
		{
			var declaringType = (TypeDeclarationSyntax)node.Parent;
			typeToTypeInfo[declaringType].CtorInjector = delegate { };

			var returnType = semanticModel.Compilation.Assembly.GetSpecialType(SpecialType.System_Void);
			ProcessMethodDeclaration(node, "ctor", ".ctor", ResolveType(returnType), simpleName =>
			{
				var ctorLocalVar = LocalVariableNameForCurrentNode();
				var ilVar = LocalVariableNameFor("il", declaringType.Identifier.ValueText, simpleName);
				var declaringTypelocalVar = ResolveLocalVariable(declaringType.Identifier.ValueText);
				AddCecilExpression(@"{0}.Body.Instructions.Add({1}.Create(OpCodes.Ldarg_0));", ctorLocalVar, ilVar);
				AddCecilExpression(@"{0}.Body.Instructions.Add({1}.Create(OpCodes.Call, assembly.MainModule.Import(DefaultCtorFor({2}.BaseType.Resolve(), assembly))));", ctorLocalVar, ilVar, declaringTypelocalVar);
				
				base.VisitConstructorDeclaration(node);
			} );
		}

		protected override void VisitMethodDeclaration(MethodDeclarationSyntax node)
		{
			ProcessMethodDeclaration(node, node.Identifier.ValueText, MethodNameOf(node), ResolveType(node.ReturnType), _ => base.VisitMethodDeclaration(node));
		}

		protected override void VisitParameter(ParameterSyntax node)
		{
			var methodVar = LocalVariableNameForCurrentNode();
			AddCecilExpression("{0}.Parameters.Add(new ParameterDefinition(\"{1}\", ParameterAttributes.None, {2}));", methodVar, node.Identifier.ValueText, ResolveType(node.TypeOpt));
			base.VisitParameter(node);
		}

		protected override void VisitPropertyDeclaration(PropertyDeclarationSyntax node)
		{
			AddCecilExpression("[PropertyDeclaration] {0}", node);
			base.VisitPropertyDeclaration(node);
		}

		protected override void VisitAccessorList(AccessorListSyntax node)
		{
			AddCecilExpression("[AccessorListSyntax] {0}", node);
			base.VisitAccessorList(node);
		}

		protected override void VisitAnonymousMethodExpression(AnonymousMethodExpressionSyntax node)
		{
			AddCecilExpression("[AnonymousMethodExpression] {0}", node);
			base.VisitAnonymousMethodExpression(node);
		}

		protected override void VisitAnonymousObjectCreationExpression(AnonymousObjectCreationExpressionSyntax node)
		{
			AddCecilExpression("[AnonymousObjectCreationExpression] {0}", node);
			base.VisitAnonymousObjectCreationExpression(node);
		}

		protected override void VisitArgument(ArgumentSyntax node)
		{
			AddCecilExpression("[Argument] {0}", node);
			base.VisitArgument(node);
		}

		protected override void VisitArgumentList(ArgumentListSyntax node)
		{
			AddCecilExpression("[ArgumentList] {0}", node);
			base.VisitArgumentList(node);
		}

		protected override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
		{
			AddCecilExpression(" MAE: {0}", node);
			base.VisitMemberAccessExpression(node);
		}

		protected override void VisitConstructorInitializer(ConstructorInitializerSyntax node)
		{
			AddCecilExpression("---> CI: {0}", node);
			base.VisitConstructorInitializer(node);
		}

		public string Output
		{
			get
			{
				EnsureCurrentTypeHasADefaultCtor();
				return buffer.ToString();
			}
		}

		private void ProcessMethodDeclaration<T>(T node, string simpleName, string fqName, string returnType, Action<string> runWithCurrent) where T : BaseMethodDeclarationSyntax
		{
			var declaringType = (TypeDeclarationSyntax)node.Parent;
			var declaringTypeName = declaringType.Identifier.ValueText;

			var methodVar = LocalVariableNameFor("method", declaringTypeName, simpleName);
			AddCecilExpression("var {0} = new MethodDefinition(\"{1}\", {2}, {3});", methodVar, fqName, MethodModifiersToCecil(node), returnType);
			AddCecilExpression("{0}.Methods.Add({1});", ResolveLocalVariable(declaringTypeName), methodVar);

			var isAbstract = semanticModel.GetDeclaredSymbol(node).IsAbstract;
			string ilVar = null;
			if (!isAbstract)
			{
				ilVar = LocalVariableNameFor("il", declaringTypeName, simpleName);
				AddCecilExpression(@"var {0} = {1}.Body.GetILProcessor();", ilVar, methodVar);
			}

			WithCurrentNode(node, methodVar, simpleName, runWithCurrent);

			if (!isAbstract) AddCecilExpression(@"{0}.Body.Instructions.Add({1}.Create(OpCodes.Ret));", methodVar, ilVar);
		}
		
		private string ProcessRequiredModifiers(FieldDeclarationSyntax fieldDeclaration, string originalType)
		{
			if (fieldDeclaration.Modifiers.Any(m => m.ContextualKind == SyntaxKind.VolatileKeyword))
			{
				var id = string.Format("mod_req{0}", NextLocalVariableId());
				var mod_req = string.Format("var {0} = new RequiredModifierType({1}, {2});", id, originalType, ImportExpressionFor(typeof(IsVolatile)));
				AddCecilExpression(mod_req);
				return id;
			}

			return null;
		}

		private string MethodModifiersToCecil(BaseMethodDeclarationSyntax methodDeclaration)
		{
			var modifiers = MapExplicityModifiers(methodDeclaration);

			var defaultAccessibility = "MethodAttributes.Private";
			if (modifiers == string.Empty)
			{
				var methodSymbol = (MethodSymbol) semanticModel.GetDeclaredSymbol(methodDeclaration);
				if (IsExplicityMethodImplementation(methodSymbol))
				{
					modifiers = "MethodAttributes.Virtual | MethodAttributes.NewSlot | MethodAttributes.Final";
				}
				else
				{
					var lastDeclaredIn = methodSymbol.FindLastDefinition();
					if (lastDeclaredIn.ContainingType.TypeKind == TypeKind.Interface)
					{
						modifiers = "MethodAttributes.Virtual | MethodAttributes.NewSlot | " + (lastDeclaredIn.ContainingType == methodSymbol.ContainingType ? "MethodAttributes.Abstract" : "MethodAttributes.Final");
						defaultAccessibility = lastDeclaredIn.ContainingType == methodSymbol.ContainingType ? "MethodAttributes.Public" : "MethodAttributes.Private";
					}
				}
			}

			var validModifiers = RemoveSourceModifiersWithNoILEquivalent(methodDeclaration);

			var cecilModifiersStr = ModifiersToCecil("MethodAttributes", validModifiers.ToList(), defaultAccessibility);
			if (methodDeclaration.Kind == SyntaxKind.ConstructorDeclaration)
			{
				cecilModifiersStr = cecilModifiersStr.AppendModifier(CtorFlags);
			}
			return cecilModifiersStr + " | MethodAttributes.HideBySig".AppendModifier(modifiers);
		}

		private static string MapExplicityModifiers(BaseMethodDeclarationSyntax methodDeclaration)
		{
			foreach (var mod in methodDeclaration.Modifiers)
			{
				switch (mod.Kind)
				{
					case SyntaxKind.VirtualKeyword:  return "MethodAttributes.Virtual | MethodAttributes.NewSlot";
					case SyntaxKind.OverrideKeyword: return "MethodAttributes.Virtual";
					case SyntaxKind.AbstractKeyword: return "MethodAttributes.Virtual | MethodAttributes.NewSlot | MethodAttributes.Abstract";
					case SyntaxKind.SealedKeyword:   return "MethodAttributes.Final";
					case SyntaxKind.NewKeyword:      return "??? new ??? dont know yet!";
				}
			}
			return string.Empty;
		}

		private static bool IsExplicityMethodImplementation(MethodSymbol methodSymbol)
		{
			return methodSymbol.ExplicitInterfaceImplementations.Count > 0;
		}

		private static IEnumerable<SyntaxToken> RemoveSourceModifiersWithNoILEquivalent(BaseMethodDeclarationSyntax methodDeclaration)
		{
			return methodDeclaration.Modifiers.Where(
				mod => (mod.Kind != SyntaxKind.OverrideKeyword 
				        && mod.Kind != SyntaxKind.AbstractKeyword 
				        && mod.Kind != SyntaxKind.VirtualKeyword 
				        && mod.Kind != SyntaxKind.SealedKeyword));
		}

		private static string TypeModifiersToCecil(TypeDeclarationSyntax node)
		{
			var convertedModifiers = ModifiersToCecil("TypeAttributes", node.Modifiers, string.Empty);
			var typeAttribute = node.Kind == SyntaxKind.ClassDeclaration
									? "TypeAttributes.AnsiClass | TypeAttributes.BeforeFieldInit"
									: "TypeAttributes.Interface | TypeAttributes.Abstract";

			return typeAttribute.AppendModifier(convertedModifiers);
		}

		private static string FieldModifiersToCecil(FieldDeclarationSyntax node)
		{
			return ModifiersToCecil("FieldAttributes", node.Modifiers, string.Empty);
		}

		private static string ModifiersToCecil(string targetEnum, IEnumerable<SyntaxToken> modifiers, string @default)
		{
			var validModifiers = modifiers.Where(ExcludeHasNoMetadataRepresentation);
			if (validModifiers.Count() == 0) return @default;

			var cecilModifierStr = validModifiers.Aggregate("", (acc, token) => acc + (ModifiersSeparator + targetEnum + "." + token.MapModifier()));
			return cecilModifierStr.Substring(ModifiersSeparator.Length);
		}

		private static bool ExcludeHasNoMetadataRepresentation(SyntaxToken token)
		{
			return token.ContextualKind != SyntaxKind.PartialKeyword && token.ContextualKind != SyntaxKind.VolatileKeyword;
		}

		private void SetDeclaringType(TypeDeclarationSyntax classDeclaration, string localVariable)
		{
			typeToTypeInfo[classDeclaration] = new TypeInfo(localVariable);
			if (classDeclaration.Parent.Kind == SyntaxKind.ClassDeclaration)
			{
				AddCecilExpression("{0}.DeclaringType = {1};", localVariable, typeToTypeInfo[(TypeDeclarationSyntax) classDeclaration.Parent]);
			}
		}

		private int NextLocalVariableTypeId()
		{
			return ++currentTypeId;
		}

		private int NextLocalVariableId()
		{
			return ++currentFieldId;
		}

		private string ImportExpressionFor(Type type)
		{
			return ImportExpressionFor(type.FullName);
		}

		private static string ImportExpressionFor(string typeName)
		{
			return string.Format("assembly.MainModule.Import(typeof({0}))", typeName);
		}

		private string ResolveType(string typeName)
		{
			//return ResolveLocalVariable(typeName) ?? ImportExpressionFor(typeName);
			return ImportExpressionFor(typeName);
		}

		private string ResolveLocalVariable(string typeName)
		{
			var typeDeclaration = typeToTypeInfo.Keys.OfType<TypeDeclarationSyntax>().Where(td => td.Identifier.ValueText == typeName).SingleOrDefault();
			return typeDeclaration != null ? typeToTypeInfo[typeDeclaration].LocalVariable : null;
		}

		private string ResolveType(TypeSyntax type)
		{
			switch(type.Kind)
			{
				case SyntaxKind.PredefinedType: return ResolveType(semanticModel.GetSemanticInfo(type).Type);
				case SyntaxKind.ArrayType: return "new ArrayType(" + ResolveType(type.DescendentNodes().OfType<TypeSyntax>().Single()) + ")";
			}

			return ResolveType(type.PlainName);
		}

		private static string ResolveType(TypeSymbol typeSymbol)
		{
			return "assembly.MainModule.TypeSystem." + typeSymbol.Name;
		}


		private string ProcessBase(ClassDeclarationSyntax classDeclaration)
		{
			var classSymbol = (TypeSymbol) semanticModel.GetDeclaredSymbol(classDeclaration);
			var baseTypeName = classSymbol.BaseType.Name;
			
			return ResolveLocalVariable(baseTypeName) ?? ResolveType(baseTypeName);
		}

		private IEnumerable<string> ImplementedInterfacesFor(BaseListSyntax bases)
		{
			if (bases == null) yield break;

			foreach (var @base in bases.Types)
			{
				var info = semanticModel.GetSemanticInfo(@base);
				if (info.Type.TypeKind == TypeKind.Interface)
				{
					var itfFQName = @base.DescendentTokens().OfType<SyntaxToken>().Aggregate("", (acc, curr) => acc + curr.ValueText);
					yield return itfFQName;
				}
			}
		}

		private string MethodNameOf(MethodDeclarationSyntax method)
		{
			var symbol = semanticModel.GetDeclaredSymbol(method);
			return symbol.Name;
		}

		private void EnsureCurrentTypeHasADefaultCtor()
		{
			foreach (var pair in typeToTypeInfo)
			{
				pair.Value.CtorInjector(pair.Value.LocalVariable, pair.Key);
				pair.Value.CtorInjector = delegate { };
			}
		}

		private void HandleInterfaceDeclaration(TypeDeclarationSyntax node)
		{
			HandleTypeDeclaration(node, string.Empty, delegate {});	
		}

		private void HandleClassDeclaration(TypeDeclarationSyntax node, string baseType)
		{
			HandleTypeDeclaration(node, baseType, DefaultCtorInjector);	
		}

		private void HandleTypeDeclaration(TypeDeclarationSyntax node, string baseType, Action<string, TypeDeclarationSyntax> ctorInjector)
		{
			EnsureCurrentTypeHasADefaultCtor();

			var varName = LocalVariableNameForId(NextLocalVariableTypeId());

			AddCecilExpression("TypeDefinition {0} = new TypeDefinition(\"{1}\", \"{2}\", {3}{4});", varName, currentNameSpace, node.Identifier.Value, TypeModifiersToCecil(node), !string.IsNullOrWhiteSpace(baseType) ? ", " + baseType : "");

			foreach (var itfName in ImplementedInterfacesFor(node.BaseListOpt))
			{
				AddCecilExpression("{0}.Interfaces.Add({1});", varName, ResolveType(itfName));
			}

			AddCecilExpression("assembly.MainModule.Types.Add({0});", varName);

			SetDeclaringType(node, varName);

			typeToTypeInfo[node].CtorInjector = ctorInjector;
		}

		private void DefaultCtorInjector(string localVar, TypeDeclarationSyntax declaringClass)
		{
			var ctorLocalVar = TempLocalVar("ctor");
			AddCecilExpression(@"var {0} = new MethodDefinition("".ctor"", {1} | {2} | MethodAttributes.HideBySig, assembly.MainModule.TypeSystem.Void);", ctorLocalVar, CtorFlags, DefaultCtorAccessibilityFor(declaringClass));
			AddCecilExpression(@"{0}.Methods.Add({1});", localVar, ctorLocalVar);
			var ilVar = TempLocalVar("il");
			AddCecilExpression(@"var {0} = {1}.Body.GetILProcessor();", ilVar, ctorLocalVar);

			AddCecilExpression(@"{0}.Body.Instructions.Add({1}.Create(OpCodes.Ldarg_0));", ctorLocalVar, ilVar);
			AddCecilExpression(@"{0}.Body.Instructions.Add({1}.Create(OpCodes.Call, assembly.MainModule.Import(DefaultCtorFor({2}.BaseType.Resolve(), assembly))));", ctorLocalVar, ilVar, localVar);
			AddCecilExpression(@"{0}.Body.Instructions.Add({1}.Create(OpCodes.Ret));", ctorLocalVar, ilVar);
		}

		private string DefaultCtorAccessibilityFor(TypeDeclarationSyntax declaringClass)
		{
			return declaringClass.Modifiers.Any(m => m.Kind == SyntaxKind.AbstractKeyword) 
								? "MethodAttributes.Family"
								: "MethodAttributes.Public";
		}

		private void AddCecilExpression(string format, params object[] args)
		{
			buffer.AppendFormat("{0}\r\n", string.Format(format, args));
		}

		private void WithCurrentNode(MemberDeclarationSyntax node, string localVariable, string typeName, Action<string> action)
		{
			try
			{
				nodeStack.Push(new LocalVariable(node, localVariable));
				action(typeName);
			}
			finally
			{
				nodeStack.Pop();
			}
		}

		private string TempLocalVar(string prefix)
		{
			return prefix + NextLocalVariableId();
		}

		private static string LocalVariableNameForId(int localVarId)
		{
			return "t" + localVarId;
		}

		private string LocalVariableNameFor(string prefix, params string[] parts)
		{
			return parts.Aggregate(prefix, (acc, curr) => acc + "_" + curr);
		}

		private string LocalVariableNameForCurrentNode()
		{
			var node = nodeStack.Peek();
			return node.VarName;
		}

		private const string ModifiersSeparator = " | ";
		private const string CtorFlags = "MethodAttributes.RTSpecialName | MethodAttributes.SpecialName";

		private StringBuilder buffer = new StringBuilder();
		private int currentTypeId;
		private int currentFieldId;
		private readonly SemanticModel semanticModel;
		private IDictionary<TypeDeclarationSyntax, TypeInfo> typeToTypeInfo = new Dictionary<TypeDeclarationSyntax, TypeInfo>();
		private Stack<LocalVariable> nodeStack = new Stack<LocalVariable>();
		private string currentNameSpace;

		class LocalVariable
		{
			public LocalVariable(SyntaxNode node, string localVariable)
			{
				VarName = localVariable;
				SyntaxNode = node;
			}

			public string VarName { get; set; }
			public SyntaxNode SyntaxNode { get; set; }
		}

		private	class TypeInfo
		{
			public readonly string LocalVariable;
			public Action<string, TypeDeclarationSyntax> CtorInjector;

			public TypeInfo(string localVariable)
			{
				LocalVariable = localVariable;
				CtorInjector = delegate { };
			}

			public override string ToString()
			{
				return LocalVariable;
			}
		}
	}
}
