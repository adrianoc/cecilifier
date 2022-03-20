using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Naming;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Cecilifier.Core.AST
{
    internal class SyntaxWalkerBase : CSharpSyntaxWalker
    {
        private const string ModifiersSeparator = " | ";
            
        internal SyntaxWalkerBase(IVisitorContext ctx)
        {
            Context = ctx;
            DefaultParameterExtractorVisitor.Initialize(ctx);
        }

        protected IVisitorContext Context { get; }

        protected void AddCecilExpressions(IEnumerable<string> exps)
        {
            foreach (var exp in exps)
            {
                AddCecilExpression(exp);
            }
        }

        protected void AddCecilExpression(string exp)
        {
            WriteCecilExpression(Context, exp);
        }

        protected void AddCecilExpression(string format, params object[] args)
        {
            WriteCecilExpression(Context, format, args);
        }

        protected void AddMethodCall(string ilVar, IMethodSymbol method, bool isAccessOnThisOrObjectCreation = false)
        {
            var opCode = (method.IsStatic || method.IsDefinedInCurrentType(Context) && isAccessOnThisOrObjectCreation || method.ContainingType.IsValueType) && !(method.IsVirtual || method.IsAbstract)
                ? OpCodes.Call
                : OpCodes.Callvirt;
            
            if (method.IsStatic)
            {
                opCode = OpCodes.Call;
            }
            
            if (method.IsGenericMethod && method.IsDefinedInCurrentType(Context))
            {
                // if the method in question is a generic method and it is defined in the same assembly create a generic instance
                var resolvedMethodVar = Context.Naming.MemberReference(method.Name);
                var m1 = $"var {resolvedMethodVar} = {method.MethodResolverExpression(Context)};";
                
                var genInstVar = Context.Naming.GenericInstance(method);
                var m = $"var {genInstVar} = new GenericInstanceMethod({resolvedMethodVar});";
                AddCecilExpression(m1);
                AddCecilExpression(m);
                foreach (var t in method.TypeArguments)
                    AddCecilExpression($"{genInstVar}.GenericArguments.Add({Context.TypeResolver.Resolve(t)});");

                Context.EmitCilInstruction(ilVar, opCode, genInstVar);
            }
            else
            {
                var operand = method.MethodResolverExpression(Context);
                Context.EmitCilInstruction(ilVar, opCode, operand);
            }
        }

        protected void InsertCilInstructionAfter<T>(LinkedListNode<string> instruction, string ilVar, OpCode opCode, T arg = default)
        {
            var instVar = CreateCilInstruction(ilVar, opCode, arg);
            Context.MoveLineAfter(Context.CurrentLine, instruction);

            AddCecilExpression($"{ilVar}.Append({instVar});");
            Context.MoveLineAfter(Context.CurrentLine, instruction.Next);
        }

        protected void AddCilInstruction(string ilVar, OpCode opCode, ITypeSymbol type)
        {
            string operand = Context.TypeResolver.Resolve(type);
            Context.EmitCilInstruction(ilVar, opCode, operand);
        }

        protected string AddCilInstructionWithLocalVariable(string ilVar, OpCode opCode)
        {
            var instVar = CreateCilInstruction(ilVar, opCode);
            AddCecilExpression($"{ilVar}.Append({instVar});");
            
            return instVar;
        }

        protected string CreateCilInstruction(string ilVar, OpCode opCode, object operand = null)
        {
            var operandStr = operand == null ? string.Empty : $", {operand}";
            var instVar = Context.Naming.Instruction(opCode.Code.ToString());
            AddCecilExpression($"var {instVar} = {ilVar}.Create({opCode.ConstantName()}{operandStr});");

            return instVar;
        }

        protected string AddLocalVariableWithResolvedType(string localVarName, DefinitionVariable methodVar, string resolvedVarType)
        {
            var cecilVarDeclName = Context.Naming.SyntheticVariable(localVarName, ElementKind.LocalVariable);
            
            AddCecilExpression("var {0} = new VariableDefinition({1});", cecilVarDeclName, resolvedVarType);
            AddCecilExpression("{0}.Body.Variables.Add({1});", methodVar.VariableName, cecilVarDeclName);

            Context.DefinitionVariables.RegisterNonMethod(string.Empty, localVarName, VariableMemberKind.LocalVariable, cecilVarDeclName);

            return cecilVarDeclName;
        }
        
        protected string AddLocalVariableToCurrentMethod(string localVarName, string varType)
        {
            var currentMethod = Context.DefinitionVariables.GetLastOf(VariableMemberKind.Method);
            if (!currentMethod.IsValid)
                throw new InvalidOperationException("Could not resolve current method declaration variable.");

            return AddLocalVariableWithResolvedType(localVarName, currentMethod, varType);
        }
        
        protected void LoadLiteralValue(string ilVar, ITypeSymbol type, string value, bool isTargetOfCall)
        {
            switch (type.SpecialType)
            {
                case SpecialType.System_Object:
                case SpecialType.None:
                    if (type.TypeKind == TypeKind.Pointer)
                    {
                        Context.EmitCilInstruction(ilVar, OpCodes.Ldc_I4_0);
                        Context.EmitCilInstruction(ilVar, OpCodes.Conv_U);
                    }
                    else
                        Context.EmitCilInstruction(ilVar, OpCodes.Ldnull);
                    break;

                case SpecialType.System_String:
                    if (value == null)
                        Context.EmitCilInstruction(ilVar, OpCodes.Ldnull);
                    else
                        Context.EmitCilInstruction(ilVar, OpCodes.Ldstr, value);
                    break;

                case SpecialType.System_Char:
                    LoadLiteralToStackHandlingCallOnValueTypeLiterals(ilVar, type, (int) value[1], isTargetOfCall);
                    break;
                
                case SpecialType.System_Int16:
                case SpecialType.System_Int32:
                case SpecialType.System_Int64:
                case SpecialType.System_Double:
                case SpecialType.System_Single:
                    LoadLiteralToStackHandlingCallOnValueTypeLiterals(ilVar, type, value, isTargetOfCall);
                    break;

                case SpecialType.System_Boolean:
                    LoadLiteralToStackHandlingCallOnValueTypeLiterals(ilVar, type, Boolean.Parse(value) ? 1 : 0, isTargetOfCall);
                    break;

                default:
                    throw new ArgumentException($"Literal {value} of type {type.SpecialType} not supported yet.");
            }
        }
        
        protected void LoadLiteralToStackHandlingCallOnValueTypeLiterals(string ilVar, ITypeSymbol literalType, object literalValue, bool isTargetOfCall)
        {
            var opCode = LoadOpCodeFor(literalType);
            Context.EmitCilInstruction(ilVar, opCode, literalValue);
            if (isTargetOfCall) 
                StoreTopOfStackInLocalVariableAndLoadItsAddress(ilVar, literalType);
        }

        private OpCode LoadOpCodeFor(ITypeSymbol type)
        {
            switch (type.SpecialType)
            {
                case SpecialType.System_Single:
                    return OpCodes.Ldc_R4;

                case SpecialType.System_Double:
                    return OpCodes.Ldc_R8;

                case SpecialType.System_Byte:
                case SpecialType.System_SByte:
                case SpecialType.System_Int16:
                case SpecialType.System_Int32:
                case SpecialType.System_UInt16:
                case SpecialType.System_UInt32:
                    return OpCodes.Ldc_I4;

                case SpecialType.System_UInt64:
                case SpecialType.System_Int64:
                    return OpCodes.Ldc_I8;

                case SpecialType.System_Char:
                    return OpCodes.Ldc_I4;

                case SpecialType.System_Boolean:
                    return OpCodes.Ldc_I4;

                case SpecialType.System_String:
                    return OpCodes.Ldstr;

                case SpecialType.None:
                    return OpCodes.Ldnull;
            }
            
            throw new ArgumentException($"Literal type {type} not supported.", nameof(type));
        }

        protected void StoreTopOfStackInLocalVariableAndLoadItsAddress(string ilVar, ITypeSymbol type)
        {
            var tempLocalName = AddLocalVariableWithResolvedType("tmp", Context.DefinitionVariables.GetLastOf(VariableMemberKind.Method), Context.TypeResolver.Resolve(type));
            Context.EmitCilInstruction(ilVar, OpCodes.Stloc, tempLocalName);
            Context.EmitCilInstruction(ilVar, OpCodes.Ldloca_S, tempLocalName);
        }        

        protected IMethodSymbol DeclaredSymbolFor<T>(T node) where T : BaseMethodDeclarationSyntax
        {
            return Context.GetDeclaredSymbol(node);
        }

        protected ITypeSymbol DeclaredSymbolFor(TypeDeclarationSyntax node)
        {
            return Context.GetDeclaredSymbol(node);
        }

        protected void WithCurrentMethod(string declaringTypeName, string localVariable, string methodName, string[] paramTypes, Action<string> action)
        {
            using (Context.DefinitionVariables.WithCurrentMethod(declaringTypeName, methodName, paramTypes, localVariable))
            {
                action(methodName);
            }
        }

        protected string ImportExpressionForType(Type type)
        {
            return Utils.ImportFromMainModule($"typeof({type.FullName})");
        }

        protected string TypeModifiersToCecil(BaseTypeDeclarationSyntax node)
        {
            var hasStaticCtor = node.DescendantNodes().OfType<ConstructorDeclarationSyntax>().Any(d => d.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword)));
            var typeAttributes = CecilDefinitionsFactory.DefaultTypeAttributeFor(node.Kind(), hasStaticCtor);
            if (IsNestedTypeDeclaration(node))
            {
                return typeAttributes.AppendModifier(ModifiersToCecil(node.Modifiers, m => "TypeAttributes.Nested" + m.ValueText.PascalCase()));
            }

            var convertedModifiers = ModifiersToCecil(node.Modifiers, "TypeAttributes", "NotPublic", MapAttribute);
            return typeAttributes.AppendModifier(convertedModifiers);

            IEnumerable<string> MapAttribute(SyntaxToken token)
            {
                var mapped = token.Kind() switch
                {
                    SyntaxKind.InternalKeyword => "NotPublic",
                    SyntaxKind.ProtectedKeyword => "Family",
                    SyntaxKind.PrivateKeyword => "Private",
                    SyntaxKind.PublicKeyword => "Public",
                    SyntaxKind.StaticKeyword => "Static",
                    SyntaxKind.AbstractKeyword => "Abstract",
                    SyntaxKind.SealedKeyword => "Sealed",
                    _ => throw new ArgumentException()
                };
                
                return new[] { mapped };
            }
        }

        private static bool IsNestedTypeDeclaration(SyntaxNode node)
        {
            Utils.EnsureNotNull(node.Parent);
            return node.Parent.Kind() != SyntaxKind.NamespaceDeclaration && node.Parent.Kind() != SyntaxKind.CompilationUnit;
        }
        
        protected static string ModifiersToCecil(IReadOnlyList<SyntaxToken> modifiers, string targetEnum, string defaultAccessibility, Func<SyntaxToken, IEnumerable<string>> mapAttribute = null)
        {
            var finalModifierList = new List<SyntaxToken>(modifiers);

            var accessibilityModifiers = string.Empty;
            IsInternalProtected(finalModifierList, ref accessibilityModifiers);
            IsPrivateProtected(finalModifierList, ref accessibilityModifiers);

            mapAttribute ??= MapAttributeForMembers;
            
            var modifierStr = finalModifierList.Where(ExcludeHasNoCILRepresentation).SelectMany(mapAttribute).Aggregate("", (acc, curr) => (acc.Length > 0 ? $"{acc} | " : "") + $"{targetEnum}." + curr) + accessibilityModifiers;
            
            if(!modifiers.Any(m => m.IsKind(SyntaxKind.PrivateKeyword) || m.IsKind(SyntaxKind.InternalKeyword) || m.IsKind(SyntaxKind.PrivateKeyword) || m.IsKind(SyntaxKind.PublicKeyword) || m.IsKind(SyntaxKind.ProtectedKeyword)))
                return modifierStr.AppendModifier($"{targetEnum}.{defaultAccessibility}");

            return modifierStr;
            
            void IsInternalProtected(List<SyntaxToken> tokens, ref string s)
            {
                if (HandleModifiers(tokens, SyntaxFactory.Token(SyntaxKind.InternalKeyword), SyntaxFactory.Token(SyntaxKind.ProtectedKeyword))) 
                    s = $"{targetEnum}.FamORAssem";
            }

            void IsPrivateProtected(List<SyntaxToken> tokens, ref string s)
            {
                if (HandleModifiers(tokens, SyntaxFactory.Token(SyntaxKind.PrivateKeyword), SyntaxFactory.Token(SyntaxKind.ProtectedKeyword))) 
                    s = $"{targetEnum}.FamANDAssem";
            }

            bool HandleModifiers(List<SyntaxToken> tokens, SyntaxToken first, SyntaxToken second)
            {
                if (tokens.Any(c => c.IsKind(first.Kind())) && tokens.Any(c => c.IsKind(second.Kind())))
                {
                    tokens.RemoveAll(c => c.IsKind(first.Kind()) || c.IsKind(second.Kind()));
                    return true;
                }

                return false;
            }
            
            IEnumerable<string> MapAttributeForMembers(SyntaxToken token)
            {
                switch (token.Kind())
                {
                    case SyntaxKind.InternalKeyword: return new[] { "Assembly" };
                    case SyntaxKind.ProtectedKeyword: return new[] { "Family" };
                    case SyntaxKind.PrivateKeyword: return new[] { "Private" };
                    case SyntaxKind.PublicKeyword: return new[] { "Public" };
                    case SyntaxKind.StaticKeyword: return new[] { "Static" };
                    case SyntaxKind.AbstractKeyword: return new[] { "Abstract" };
                    case SyntaxKind.ConstKeyword: return new[] { "Literal", "Static" };
                    case SyntaxKind.ReadOnlyKeyword: return new[] { "InitOnly" };
                }

                throw new ArgumentException($"Unsupported attribute name: {token.Kind().ToString()}");
            }
        }

        private static string ModifiersToCecil(IEnumerable<SyntaxToken> modifiers, Func<SyntaxToken, string> map)
        {
            var cecilModifierStr = modifiers.Aggregate("", (acc, token) =>
                acc + ModifiersSeparator + map(token));

            if (cecilModifierStr.Length > 0)
            {
                cecilModifierStr = cecilModifierStr.Substring(ModifiersSeparator.Length);
            }

            return cecilModifierStr;
        }

        protected static void WriteCecilExpression(IVisitorContext context, string format, params object[] args)
        {
            context.WriteCecilExpression(string.Format(format, args));
            context.WriteNewLine();
        }

        protected static void WriteCecilExpression(IVisitorContext context, string value)
        {
            context.WriteCecilExpression(value);
            context.WriteNewLine();
        }

        private static bool ExcludeHasNoCILRepresentation(SyntaxToken token)
        {
            return !token.IsKind(SyntaxKind.PartialKeyword) 
                   && !token.IsKind(SyntaxKind.VolatileKeyword) 
                   && !token.IsKind(SyntaxKind.UnsafeKeyword)
                   && !token.IsKind(SyntaxKind.AsyncKeyword)
                   && !token.IsKind(SyntaxKind.ExternKeyword);
        }

        protected string ResolveExpressionType(ExpressionSyntax expression)
        {
            Utils.EnsureNotNull(expression);
            var info = Context.GetTypeInfo(expression);
            return Context.TypeResolver.Resolve(info.Type);
        }
        
        protected string ResolveType(TypeSyntax type)
        {
            var typeToCheck = type is RefTypeSyntax refType ? refType.Type : type;
            var typeInfo = Context.GetTypeInfo(typeToCheck);

            var resolvedType = Context.TypeResolver.Resolve(typeInfo.Type);
            return type is RefTypeSyntax ? resolvedType.MakeByReferenceType() : resolvedType;
        }

        protected void ProcessParameter(string ilVar, SimpleNameSyntax node, IParameterSymbol paramSymbol)
        {
            var parent = (CSharpSyntaxNode) node.Parent;
            var method = (IMethodSymbol) paramSymbol.ContainingSymbol;
            
            if (HandleLoadAddress(ilVar, paramSymbol.Type, parent, OpCodes.Ldarga, paramSymbol.Name, VariableMemberKind.Parameter, (method.AssociatedSymbol ?? method).ToDisplayString()))
                return;

            Utils.EnsureNotNull(node.Parent);
            //TODO: Add a test for parameter index for static/instance members.
            var adjustedParameterIndex = paramSymbol.Ordinal + (method.IsStatic ? 0 : 1);
            if (adjustedParameterIndex > 3)
            {
                Context.EmitCilInstruction(ilVar, OpCodes.Ldarg, adjustedParameterIndex);
            }
            else
            {
                OpCode[] optimizedLdArgs = {OpCodes.Ldarg_0, OpCodes.Ldarg_1, OpCodes.Ldarg_2, OpCodes.Ldarg_3};
                var loadOpCode = optimizedLdArgs[adjustedParameterIndex];
                Context.EmitCilInstruction(ilVar, loadOpCode);
            }
            HandlePotentialDelegateInvocationOn(node, paramSymbol.Type, ilVar);
            HandlePotentialRefLoad(ilVar, node, paramSymbol.Type);
        }

        protected bool HandleLoadAddress(string ilVar, ITypeSymbol symbol, CSharpSyntaxNode node, OpCode opCode, string symbolName, VariableMemberKind variableMemberKind, string parentName = null)
        {
            return HandleCallOnValueType() || HandleRefAssignment() || HandleParameter();
            
            bool HandleCallOnValueType()
            {
                if (!symbol.IsValueType)
                    return false;
                
                // in this case we need to call System.Index.GetOffset(int32) on a value type (System.Index)
                // which requires the address of the value type.
                var isSystemIndexUsedAsIndex = IsSystemIndexUsedAsIndex(symbol, node);
                if (isSystemIndexUsedAsIndex || node.IsKind(SyntaxKind.AddressOfExpression) || IsPseudoAssignmentToValueType() || node.Accept(new UsageVisitor(Context)) == UsageKind.CallTarget)
                {
                    string operand = Context.DefinitionVariables.GetVariable(symbolName, variableMemberKind, parentName).VariableName;
                    Context.EmitCilInstruction(ilVar, opCode, operand);
                    if (!Context.HasFlag(Constants.ContextFlags.Fixed) && node.IsKind(SyntaxKind.AddressOfExpression))
                        Context.EmitCilInstruction(ilVar, OpCodes.Conv_U);

                    return true;
                }

                return false;
            }
            
            bool HandleRefAssignment()
            {
                if (!(node is RefExpressionSyntax refExpression))
                    return false;
                
                var assignedValueSymbol = Context.SemanticModel.GetSymbolInfo(refExpression.Expression);
                if (assignedValueSymbol.Symbol.IsByRef())
                    return false;

                string operand = Context.DefinitionVariables.GetVariable(symbolName, variableMemberKind, parentName).VariableName;
                Context.EmitCilInstruction(ilVar, opCode, operand);
                return true;
            }

            bool HandleParameter()
            {
                if (!(node is ArgumentSyntax argument) || !argument.RefOrOutKeyword.IsKind(SyntaxKind.RefKeyword))
                    return false;

                if (Context.SemanticModel.GetSymbolInfo(argument.Expression).Symbol?.IsByRef() == false)
                {
                    string operand = Context.DefinitionVariables.GetVariable(symbolName, variableMemberKind, parentName).VariableName;
                    Context.EmitCilInstruction(ilVar, opCode, operand);
                    return true;
                }
                return false;
            }
            
            bool IsPseudoAssignmentToValueType() => Context.HasFlag(Constants.ContextFlags.PseudoAssignmentToIndex);
        }
        
        protected void HandlePotentialRefLoad(string ilVar, SyntaxNode expression, ITypeSymbol type)
        {
            var needsLoadIndirect = false;
            
            var sourceSymbol = Context.SemanticModel.GetSymbolInfo(expression).Symbol;
            var sourceIsByRef = sourceSymbol.IsByRef();
        
            var returnLikeNode = (SyntaxNode) expression.Ancestors().OfType<ArrowExpressionClauseSyntax>().SingleOrDefault() 
                                 ?? expression.Ancestors().OfType<ReturnStatementSyntax>().SingleOrDefault();
            
            var argument = expression.Ancestors().OfType<ArgumentSyntax>().FirstOrDefault();
            var assigment = expression.Ancestors().OfType<AssignmentExpressionSyntax>().SingleOrDefault(c => c.Left != expression);
            var mae = expression.Ancestors().OfType<MemberAccessExpressionSyntax>().SingleOrDefault(c => c.Expression == expression);

            if (mae != null)
            {
                needsLoadIndirect = sourceIsByRef && !type.IsValueType;
            }
            else if (assigment != null)
            {
                var targetIsByRef = Context.SemanticModel.GetSymbolInfo(assigment.Left).Symbol.IsByRef();
                needsLoadIndirect = (assigment.Right == expression && sourceIsByRef && !targetIsByRef) // simple assignment like: nonRef = ref;
                                    || sourceIsByRef; // complex assignment like: nonRef = ref + 10;
            }
            else if (argument != null)
            {
                var parameterSymbol = ParameterSymbolFromArgumentSyntax(argument);
                var targetIsByRef = parameterSymbol.IsByRef();
        
                needsLoadIndirect = sourceIsByRef && !targetIsByRef;
            }
            else if (returnLikeNode != null)
            {
                var method = returnLikeNode.Ancestors().OfType<MemberDeclarationSyntax>().First();
                bool returnTypeIsByRef = Context.SemanticModel.GetDeclaredSymbol(method).IsByRef();
                
                needsLoadIndirect = sourceIsByRef && !returnTypeIsByRef;
            }
            
            if (needsLoadIndirect)
            {
                OpCode opCode = LoadIndirectOpCodeFor(type.SpecialType);
                Context.EmitCilInstruction(ilVar, opCode);
            }
        }

        private IParameterSymbol ParameterSymbolFromArgumentSyntax(ArgumentSyntax argument)
        {
            var invocation = argument.Ancestors().OfType<InvocationExpressionSyntax>().FirstOrDefault();
            if (invocation != null && invocation.ArgumentList.Arguments.Contains(argument))
            {
                var argumentIndex = argument.Ancestors().OfType<ArgumentListSyntax>().First().Arguments.IndexOf(argument);
                var symbol = Context.SemanticModel.GetSymbolInfo(invocation.Expression).Symbol;
                var method = symbol switch
                {
                    ILocalSymbol { Type: IFunctionPointerTypeSymbol } local => ((IFunctionPointerTypeSymbol)local.Type).Signature,
                    ILocalSymbol { Type: INamedTypeSymbol } local => ((INamedTypeSymbol)local.Type).DelegateInvokeMethod,
                    IParameterSymbol { Type: INamedTypeSymbol } param => ((INamedTypeSymbol)param.Type).DelegateInvokeMethod,
                    IParameterSymbol { Type: IFunctionPointerTypeSymbol } param => ((IFunctionPointerTypeSymbol)param.Type).Signature,
                    IFieldSymbol { Type: INamedTypeSymbol } field => ((INamedTypeSymbol)field.Type).DelegateInvokeMethod,
                    IFieldSymbol { Type: IFunctionPointerTypeSymbol } field => ((IFunctionPointerTypeSymbol)field.Type).Signature,
                    IPropertySymbol { Type: INamedTypeSymbol } field => ((INamedTypeSymbol)field.Type).DelegateInvokeMethod,
                    IPropertySymbol { Type: IFunctionPointerTypeSymbol } field => ((IFunctionPointerTypeSymbol)field.Type).Signature,
                    IMethodSymbol m => m,
                    _ => throw new NotImplementedException($"Found not supported symbol {symbol.ToDisplayString()} ({symbol.GetType().Name}) when trying to find index of argument ({argument})")
                };

                return method.Parameters[argumentIndex];
            }

            var elementAccess = argument.Ancestors().OfType<ElementAccessExpressionSyntax>().SingleOrDefault();
            if (elementAccess != null)
            {
                var indexerSymbol= Context.SemanticModel.GetIndexerGroup(elementAccess.Expression).FirstOrDefault();
                if (indexerSymbol != null)
                {
                    var argumentIndex = argument.Ancestors().OfType<BracketedArgumentListSyntax>().Single().Arguments.IndexOf(argument);
                    return indexerSymbol.Parameters.ElementAt(argumentIndex);
                }
            }

            return null;
        }

        protected OpCode LoadIndirectOpCodeFor(SpecialType type)
        {
            return type switch
            {
                SpecialType.System_Single => OpCodes.Ldind_R4,
                SpecialType.System_Double => OpCodes.Ldind_R8,
                SpecialType.System_SByte => OpCodes.Ldind_I1,
                SpecialType.System_Byte => OpCodes.Ldind_U1,
                SpecialType.System_Int16 => OpCodes.Ldind_I2,
                SpecialType.System_UInt16 => OpCodes.Ldind_U2,
                SpecialType.System_Int32 => OpCodes.Ldind_I4,
                SpecialType.System_UInt32 => OpCodes.Ldind_U4,
                SpecialType.System_Int64 => OpCodes.Ldind_I8,
                SpecialType.System_UInt64 => OpCodes.Ldind_I8,
                SpecialType.System_Char => OpCodes.Ldind_U2,
                SpecialType.System_Boolean => OpCodes.Ldind_U1,
                SpecialType.System_Object => OpCodes.Ldind_Ref,
                SpecialType.None => OpCodes.Ldind_Ref,
                
                _ => throw new ArgumentException($"Literal type {type} not supported.", nameof(type))
            };
        }

        private bool IsSystemIndexUsedAsIndex(ITypeSymbol symbol, CSharpSyntaxNode node)
        {
            if (symbol.MetadataToken != Context.RoslynTypeSystem.SystemIndex.MetadataToken)
                return false;
 
            return node.Parent.IsKind(SyntaxKind.BracketedArgumentList);
        }

        protected void HandlePotentialDelegateInvocationOn(SimpleNameSyntax node, ITypeSymbol typeSymbol, string ilVar)
        {
            var invocation = node.Parent as InvocationExpressionSyntax;
            if (invocation == null || invocation.Expression != node)
            {
                return;
            }

            if (typeSymbol is IFunctionPointerTypeSymbol functionPointer)
            {
                var operand = CecilDefinitionsFactory.CallSite(Context.TypeResolver, functionPointer);
                Context.EmitCilInstruction(ilVar, OpCodes.Calli, operand);
                return;
            }

            var localDelegateDeclaration = Context.TypeResolver.ResolveLocalVariableType(typeSymbol);
            if (localDelegateDeclaration != null)
            {
                var operand = $"{localDelegateDeclaration}.Methods.Single(m => m.Name == \"Invoke\")";
                Context.EmitCilInstruction(ilVar, OpCodes.Callvirt, operand);
            }
            else
            {
                var invokeMethod = (IMethodSymbol) typeSymbol.GetMembers("Invoke").SingleOrDefault();
                var resolvedMethod = invokeMethod.MethodResolverExpression(Context);
                Context.EmitCilInstruction(ilVar, OpCodes.Callvirt, resolvedMethod);
            }
        }

        protected void HandleAttributesInMemberDeclaration(in SyntaxList<AttributeListSyntax> nodeAttributeLists, Func<AttributeTargetSpecifierSyntax, SyntaxKind, bool> predicate, SyntaxKind toMatch, string whereToAdd)
        {
            var attributeLists = nodeAttributeLists.Where(c => predicate(c.Target, toMatch));
            HandleAttributesInMemberDeclaration(attributeLists, whereToAdd);
        }

        protected static bool TargetDoesNotMatch(AttributeTargetSpecifierSyntax target, SyntaxKind operand) => target == null || !target.Identifier.IsKind(operand);
        protected static bool TargetMatches(AttributeTargetSpecifierSyntax target, SyntaxKind operand) => target != null && target.Identifier.IsKind(operand);
        
        protected void HandleAttributesInMemberDeclaration(IEnumerable<AttributeListSyntax> attributeLists, string varName)
        {
            if (!attributeLists.Any())
                return;

            foreach (var attribute in attributeLists.SelectMany(al => al.Attributes))
            {
                var attrsExp = Context.SemanticModel.GetSymbolInfo(attribute.Name).Symbol.IsDllImportCtor()
                    ? ProcessDllImportAttribute(attribute, varName)
                    : ProcessNormalMemberAttribute(attribute, varName);
                
                AddCecilExpressions(attrsExp);
            }
        }

        private IEnumerable<string> ProcessDllImportAttribute(AttributeSyntax attribute, string methodVar)
        {
            var moduleName = attribute.ArgumentList?.Arguments.First().ToFullString();
            var existingModuleVar = Context.DefinitionVariables.GetVariable(moduleName, VariableMemberKind.ModuleReference);
            
            var moduleVar = existingModuleVar.IsValid 
                ?  existingModuleVar.VariableName
                :  Context.Naming.SyntheticVariable("dllImportModule", ElementKind.LocalVariable); 

            var exps = new List<string>
            {
                $"{methodVar}.PInvokeInfo = new PInvokeInfo({ PInvokeAttributesFrom(attribute) }, { EntryPoint() }, {moduleVar});",
                $"{methodVar}.Body = null;",
                $"{methodVar}.ImplAttributes = {MethodImplAttributes()};",
            };

            if (!existingModuleVar.IsValid)
            {
                exps.InsertRange(0, new []
                {
                    $"var {moduleVar} = new ModuleReference({moduleName});",
                    $"assembly.MainModule.ModuleReferences.Add({moduleVar});",
                });
            }

            Context.DefinitionVariables.RegisterNonMethod("", moduleName, VariableMemberKind.ModuleReference, moduleVar);
            
            return exps;
            
            string EntryPoint() => attribute?.ArgumentList?.Arguments.FirstOrDefault(arg => arg.NameEquals?.Name.Identifier.Text == "EntryPoint")?.Expression.ToString() ?? "\"\"";

            string MethodImplAttributes()
            {
                var preserveSig = Boolean.Parse(AttributePropertyOrDefaultValue(attribute, "PreserveSig", "true"));
                return preserveSig 
                    ? "MethodImplAttributes.PreserveSig | MethodImplAttributes.Managed" 
                    : "MethodImplAttributes.Managed";
            }

            string CallingConventionFrom(AttributeSyntax attr)
            {
                var callingConventionStr = attr.ArgumentList?.Arguments.FirstOrDefault(arg => arg.NameEquals?.Name.Identifier.Text == "CallingConvention")?.Expression.ToFullString() 
                                           ?? "Winapi";

                // ensures we use the enum member simple name; Parse() fails if we pass a qualified enum member
                var index = callingConventionStr.LastIndexOf('.');
                callingConventionStr = callingConventionStr.Substring(index + 1);
                
                return CallingConventionToCecil(Enum.Parse<CallingConvention>(callingConventionStr));
            }

            string CharSetFrom(AttributeSyntax attr)
            {
                var enumMemberName = AttributePropertyOrDefaultValue(attr, "CharSet", "None");

                // Only use the actual enum member name Parse() fails if we pass a qualified enum member
                var index = enumMemberName.LastIndexOf('.');
                enumMemberName = enumMemberName.Substring(index + 1);

                var charSet = Enum.Parse<CharSet>(enumMemberName);
                return charSet == CharSet.None ? string.Empty : $"PInvokeAttributes.CharSet{charSet}";
            }

            string SetLastErrorFrom(AttributeSyntax attr)
            {
                var setLastError = bool.Parse(AttributePropertyOrDefaultValue(attr, "SetLastError", "false"));
                return setLastError ? "PInvokeAttributes.SupportsLastError" : string.Empty;
            }

            string ExactSpellingFrom(AttributeSyntax attr)
            {
                var exactSpelling = bool.Parse(AttributePropertyOrDefaultValue(attr, "ExactSpelling", "false"));
                return exactSpelling ? "PInvokeAttributes.NoMangle" : string.Empty;
            }

            string BestFitMappingFrom(AttributeSyntax attr)
            {
                var bestFitMapping = bool.Parse(AttributePropertyOrDefaultValue(attr, "BestFitMapping", "true"));
                return bestFitMapping ? "PInvokeAttributes.BestFitEnabled" : "PInvokeAttributes.BestFitDisabled";
            }

            string ThrowOnUnmappableCharFrom(AttributeSyntax attr)
            {
                var bestFitMapping = bool.Parse(AttributePropertyOrDefaultValue(attr, "ThrowOnUnmappableChar", "false"));
                return bestFitMapping ? "PInvokeAttributes.ThrowOnUnmappableCharEnabled" : "PInvokeAttributes.ThrowOnUnmappableCharDisabled";
            }

            // For more information and default values see
            // https://docs.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.dllimportattribute
            string PInvokeAttributesFrom(AttributeSyntax attr)
            {
                return CallingConventionFrom(attr)
                    .AppendModifier(CharSetFrom(attr))
                    .AppendModifier(SetLastErrorFrom(attr))
                    .AppendModifier(ExactSpellingFrom(attr))
                    .AppendModifier(BestFitMappingFrom(attr))
                    .AppendModifier(ThrowOnUnmappableCharFrom(attr));
            }

            string AttributePropertyOrDefaultValue(AttributeSyntax attr, string propertyName, string defaultValue)
            {
                return attr.ArgumentList?.Arguments.FirstOrDefault(arg => arg.NameEquals?.Name.Identifier.Text == propertyName)?.Expression.ToFullString() ?? defaultValue;
            }
        }

        private string CallingConventionToCecil(CallingConvention callingConvention)
        {
            var enumMemberName = callingConvention switch
            {
                CallingConvention.Cdecl => PInvokeAttributes.CallConvCdecl.ToString(),
                CallingConvention.Winapi => PInvokeAttributes.CallConvWinapi.ToString(),
                CallingConvention.FastCall => PInvokeAttributes.CallConvFastcall.ToString(),
                CallingConvention.StdCall => PInvokeAttributes.CallConvStdCall.ToString(),
                CallingConvention.ThisCall => PInvokeAttributes.CallConvThiscall.ToString(),

                _ => throw new Exception($"Unexpected calling convention: {callingConvention}")
            };

            return $"PInvokeAttributes.{enumMemberName}";
        }

        private IEnumerable<string> ProcessNormalMemberAttribute(AttributeSyntax attribute, string varName)
        {
            var attrsExp = CecilDefinitionsFactory.Attribute(varName, Context, attribute, (attrType, attrArgs) =>
            {
                var typeVar = Context.TypeResolver.ResolveLocalVariableType(attrType);
                if (typeVar == null)
                {
                    //attribute is not declared in the same assembly....
                    var ctorArgumentTypes = $"new Type[{attrArgs.Length}] {{ {string.Join(",", attrArgs.Select(arg => $"typeof({Context.GetTypeInfo(arg.Expression).Type?.Name})"))} }}";
                    return Utils.ImportFromMainModule($"typeof({attrType.AssemblyQualifiedName()}).GetConstructor({ctorArgumentTypes})");
                }
            
                // Attribute is defined in the same assembly. We need to find the variable that holds its "ctor declaration"
                var attrCtor = attrType.GetMembers().OfType<IMethodSymbol>().SingleOrDefault(m => m.MethodKind == MethodKind.Constructor && m.Parameters.Length == attrArgs.Length);
                var attrCtorVar = Context.DefinitionVariables.GetMethodVariable(attrCtor.AsMethodDefinitionVariable());
                if (!attrCtorVar.IsValid)
                    throw new Exception($"Could not find variable for {attrCtor.ContainingType.Name} ctor.");
            
                return attrCtorVar.VariableName;
            });

            return attrsExp;
        }

        protected void LogUnsupportedSyntax(SyntaxNode node)
        {
            var lineSpan = node.GetLocation().GetLineSpan();
            AddCecilExpression($"/* Syntax '{node.Kind()}' is not supported in {lineSpan.Path} ({lineSpan.Span.Start.Line + 1},{lineSpan.Span.Start.Character + 1}):\n------\n{node}\n----*/");
        }
    }
}
