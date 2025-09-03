using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Cecilifier.Core.ApiDriver;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Naming;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mono.Cecil;
using CecilOpCodes = Mono.Cecil.Cil.OpCodes;

using static Cecilifier.Core.Misc.CodeGenerationHelpers;

namespace Cecilifier.Core.AST
{
    internal partial class SyntaxWalkerBase : CSharpSyntaxWalker
    {
        internal SyntaxWalkerBase(IVisitorContext ctx)
        {
            Context = ctx;
            DefaultParameterExtractorVisitor.Initialize(ctx);
        }

        public IVisitorContext Context { get; }

        protected static void AddCecilExpressions(IVisitorContext context, IEnumerable<string> exps)
        {
            foreach (var exp in exps)
            {
                WriteCecilExpression(context, exp);
            }
        }

        protected void AddCecilExpression(string exp)
        {
            WriteCecilExpression(Context, exp);
        }

        protected void AddCecilExpression(string format, params object[] args)
        {
            Context.Generate(string.Format(format, args));
            Context.WriteNewLine();
        }

        protected void AddCilInstruction(string ilVar, OpCode opCode, ITypeSymbol type)
        {
            var operand = Context.TypeResolver.Resolve(type);
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
            var instVar = Context.Naming.Instruction(opCode.OpCodeName());
            AddCecilExpression($"var {instVar} = {ilVar}.Create({opCode.ConstantName()}{operandStr});");

            return instVar;
        }

        protected string CreateCilInstruction(string ilVar, string instVar, OpCode opCode, object operand = null)
        {
            var operandStr = operand == null ? string.Empty : $", {operand}";
            AddCecilExpression($"var {instVar} = {ilVar}.Create({opCode.ConstantName()}{operandStr});");
            return instVar;
        }

        protected void LoadLiteralValue(string ilVar, ITypeSymbol type, string value, UsageResult usageResult, SyntaxNode parent)
        {
            if (LoadDefaultValueForTypeParameter(ilVar, type, parent))
                return;

            if (type.SpecialType == SpecialType.None && type.IsValueType && type.TypeKind != TypeKind.Pointer || type.SpecialType == SpecialType.System_DateTime)
            {
                Context.EmitCilInstruction(ilVar, OpCodes.Initobj, Context.TypeResolver.Resolve(type));
                return;
            }

            switch (type.SpecialType)
            {
                case SpecialType.System_Object:
                case SpecialType.System_Collections_IEnumerator:
                case SpecialType.System_Collections_Generic_IEnumerator_T:
                case SpecialType.System_Collections_IEnumerable:
                case SpecialType.System_Collections_Generic_IEnumerable_T:
                case SpecialType.System_IDisposable:
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
                        Context.EmitCilInstruction(ilVar, OpCodes.Ldstr, SymbolDisplay.FormatLiteral(value, true));
                    break;

                case SpecialType.System_Char:
                    LoadLiteralToStackHandlingCallOnValueTypeLiterals(ilVar, type, (int) value[0], usageResult);
                    break;

                case SpecialType.System_Byte:
                case SpecialType.System_SByte:
                case SpecialType.System_Int16:
                case SpecialType.System_UInt16:
                case SpecialType.System_Int32:
                case SpecialType.System_UInt32:
                case SpecialType.System_Int64:
                case SpecialType.System_UInt64:
                case SpecialType.System_Double:
                case SpecialType.System_Single:
                    LoadLiteralToStackHandlingCallOnValueTypeLiterals(ilVar, type, value, usageResult);
                    break;

                case SpecialType.System_Boolean:
                    LoadLiteralToStackHandlingCallOnValueTypeLiterals(ilVar, type, Boolean.Parse(value) ? 1 : 0, usageResult);
                    break;

                case SpecialType.System_IntPtr:
                case SpecialType.System_UIntPtr:
                    LoadLiteralToStackHandlingCallOnValueTypeLiterals(ilVar, type, value, usageResult);
                    Context.EmitCilInstruction(ilVar, OpCodes.Conv_I);
                    break;

                default:
                    throw new ArgumentException($"Literal {value} of type {type.SpecialType} not supported yet.");
            }
        }

        private bool LoadDefaultValueForTypeParameter(string ilVar, ITypeSymbol type, SyntaxNode parent)
        {
            if (type is not ITypeParameterSymbol typeParameterSymbol)
                return false;

            var resolvedType = Context.TypeResolver.Resolve(type);
            
            // in an assignment expression we already have memory allocated to hold the value
            // in this case we don´t need to add a local variable.
            if (parent is AssignmentExpressionSyntax assignment)
            {
                var targetOfAssignmentSymbol = Context.SemanticModel.GetSymbolInfo(assignment.Left).Symbol.EnsureNotNull();
                var loadAddressOpcode = targetOfAssignmentSymbol.LoadAddressOpcodeForMember(); // target of assignment may be a local, field or parameter so we need to figure out the correct opcode to load its address
                var storageVariable = Context.DefinitionVariables.GetVariable(targetOfAssignmentSymbol.Name, targetOfAssignmentSymbol.ToVariableMemberKind(), targetOfAssignmentSymbol.Kind == SymbolKind.Local ? string.Empty : targetOfAssignmentSymbol.ContainingSymbol.ToDisplayString());
                
                Context.EmitCilInstruction(ilVar, loadAddressOpcode, storageVariable.VariableName);
                Context.EmitCilInstruction(ilVar, OpCodes.Initobj, resolvedType);
            }
            else if (parent.Parent is VariableDeclaratorSyntax equalsValueClauseSyntax)
            {
                // scenario: T t = default(T);
                var targetOfAssignmentSymbol = Context.SemanticModel.GetDeclaredSymbol(equalsValueClauseSyntax).EnsureNotNull();
                var storageVariable = Context.DefinitionVariables.GetVariable(targetOfAssignmentSymbol.Name, targetOfAssignmentSymbol.ToVariableMemberKind(), string.Empty);
                
                Context.EmitCilInstruction(ilVar, OpCodes.Ldloca_S, storageVariable.VariableName);
                Context.EmitCilInstruction(ilVar, OpCodes.Initobj, resolvedType);
            }
            else
            {
                // no variable exists yet (for instance, passing `default(T)` as a parameter) so we add one.
                var storageVariable = Context.AddLocalVariableToCurrentMethod(type.Name, resolvedType);
                
                Context.EmitCilInstruction(ilVar, OpCodes.Ldloca_S, storageVariable.VariableName);
                Context.EmitCilInstruction(ilVar, OpCodes.Initobj, resolvedType);
                if (!typeParameterSymbol.IsTypeParameterConstrainedToReferenceType() && parent is MemberAccessExpressionSyntax mae && mae.Parent.IsKind(SyntaxKind.InvocationExpression))
                {
                    // scenario: default(T).ToString()
                    Context.EmitCilInstruction(ilVar, OpCodes.Ldloca_S, storageVariable.VariableName);
                    Context.SetFlag(Constants.ContextFlags.MemberReferenceRequiresConstraint, resolvedType);
                }
                else
                {
                    Context.EmitCilInstruction(ilVar, OpCodes.Ldloc, storageVariable.VariableName);
                }
            }
            return true;
        }

        private void LoadLiteralToStackHandlingCallOnValueTypeLiterals(string ilVar, ITypeSymbol literalType, object literalValue, UsageResult usageResult)
        {
            var opCode = literalType.LoadOpCodeFor();
            Context.EmitCilInstruction(ilVar, opCode, literalValue);
            if (usageResult.Kind == UsageKind.CallTarget)
            {
                var tempLocalName = StoreTopOfStackInLocalVariable(Context, ilVar, "tmp", literalType).VariableName;
                if (!usageResult.Target.IsVirtual && SymbolEqualityComparer.Default.Equals(usageResult.Target.ContainingType, Context.RoslynTypeSystem.SystemObject))
                {
                    Context.EmitCilInstruction(ilVar, OpCodes.Ldloc, tempLocalName);
                    Context.EmitCilInstruction(ilVar, OpCodes.Box, Context.TypeResolver.Resolve(literalType));
                }
                else
                    Context.EmitCilInstruction(ilVar, OpCodes.Ldloca_S, tempLocalName);
            }
        }

        protected IMethodSymbol DeclaredSymbolFor<T>(T node) where T : BaseMethodDeclarationSyntax
        {
            return Context.GetDeclaredSymbol(node);
        }

        protected INamedTypeSymbol DeclaredSymbolFor(TypeDeclarationSyntax node)
        {
            return Context.GetDeclaredSymbol(node).EnsureNotNull<ITypeSymbol, INamedTypeSymbol>();
        }

        protected void WithCurrentMethod(string declaringTypeName, string localVariable, string methodName, string[] paramTypes, int typeParameterCount, Action<string> action)
        {
            using (Context.DefinitionVariables.WithCurrentMethod(declaringTypeName, methodName, paramTypes, typeParameterCount, localVariable))
            {
                action(methodName);
            }
        }

        protected string TypeModifiersToCecil(INamedTypeSymbol typeSymbol, SyntaxTokenList modifiers) => Context.ApiDefinitionsFactory.MappedTypeModifiersFor(typeSymbol, modifiers);

        //TODO: Probably we need to abstract this one also
        internal static string ModifiersToCecil<TEnumAttr>(
            IEnumerable<SyntaxToken> modifiers,
            string defaultAccessibility,
            Func<SyntaxToken, IEnumerable<string>> mapAttribute) where TEnumAttr : Enum
        {
            var targetEnum = typeof(TEnumAttr).Name;

            var finalModifierList = new List<SyntaxToken>(modifiers);

            var accessibilityModifiers = string.Empty;
            IsInternalProtected(finalModifierList, ref accessibilityModifiers);
            IsPrivateProtected(finalModifierList, ref accessibilityModifiers);

            var modifierStr = finalModifierList
                .SelectMany(mapAttribute)
                .Where(attr => !string.IsNullOrEmpty(attr))
                .Aggregate(new StringBuilder(), (acc, curr) => acc.AppendModifier($"{targetEnum}.{curr}"));

            modifierStr.Append(accessibilityModifiers);

            if (!modifiers.Any(m => m.IsKind(SyntaxKind.PrivateKeyword) || m.IsKind(SyntaxKind.InternalKeyword) || m.IsKind(SyntaxKind.PrivateKeyword) || m.IsKind(SyntaxKind.PublicKeyword) || m.IsKind(SyntaxKind.ProtectedKeyword)))
                modifierStr.AppendModifier($"{targetEnum}.{defaultAccessibility}");

            return modifierStr.ToString();

            void IsInternalProtected(List<SyntaxToken> tokens, ref string s)
            {
                if (HandleModifiers(tokens, SyntaxKind.InternalKeyword, SyntaxKind.ProtectedKeyword))
                    s = $"{targetEnum}.FamORAssem";
            }

            void IsPrivateProtected(List<SyntaxToken> tokens, ref string s)
            {
                if (HandleModifiers(tokens, SyntaxKind.PrivateKeyword, SyntaxKind.ProtectedKeyword))
                    s = $"{targetEnum}.FamANDAssem";
            }

            bool HandleModifiers(List<SyntaxToken> tokens, SyntaxKind first, SyntaxKind second)
            {
                if (tokens.Any(c => c.IsKind(first)) && tokens.Any(c => c.IsKind(second)))
                {
                    tokens.RemoveAll(c => c.IsKind(first) || c.IsKind(second));
                    return true;
                }

                return false;
            }
        }

        protected static void WriteCecilExpression(IVisitorContext context, CecilifierInterpolatedStringHandler value)
        {
            WriteCecilExpression(context, value.Result);
        }

        private static void WriteCecilExpression(IVisitorContext context, string value)
        {
            context.Generate(value);
            //TODO: DO we need this new line with the use of the ISH ?
            context.WriteNewLine();
        }

        protected string ResolveExpressionType(ExpressionSyntax expression)
        {
            var typeInfo = Context.GetTypeInfo(expression);
            var type = (typeInfo.Type ?? typeInfo.ConvertedType).EnsureNotNull();
            return Context.TypeResolver.Resolve(type);
        }

        protected string ResolveType(TypeSyntax type)
        {
            // Special case types that Context.GetTypeInfo() is not able to handle. As of Oct/2024 only the ones below are requires such special handling.
            var typeToCheck = type switch
            {
                RefTypeSyntax refType  => refType.Type,
                ScopedTypeSyntax scopedTypeSyntax => scopedTypeSyntax.Type, // `scoped` types have the same semantics as a `non scoped` one; `scoped` only changes how variables of the type can be captured/used
                                                                            // (and this is handled entirely by the compiler) 
                _ => type
            };
        
            var typeInfo = Context.GetTypeInfo(typeToCheck);

            TypeDeclarationVisitor.EnsureForwardedTypeDefinition(Context, typeInfo.Type, Array.Empty<TypeParameterSyntax>());

            var resolvedType = Context.TypeResolver.Resolve(typeInfo.Type);
            //TODO: Can't this check be moved inside the Resolve() method as the other checks for arrays,
            return type is RefTypeSyntax ? resolvedType.MakeByReferenceType() : resolvedType;
        }

        protected void ProcessParameter(string ilVar, SimpleNameSyntax node, IParameterSymbol paramSymbol)
        {
            var method = (IMethodSymbol) paramSymbol.ContainingSymbol;
            var declaringMethodName = method.ToDisplayString();
            var operand = Context.DefinitionVariables.GetVariable(paramSymbol.Name, VariableMemberKind.Parameter, declaringMethodName).VariableName;
            if (HandleLoadAddress(ilVar, paramSymbol.Type, node, OpCodes.Ldarga, operand))
                return;

            if (InlineArrayProcessor.HandleInlineArrayConversionToSpan(Context, ilVar, paramSymbol.Type, node, OpCodes.Ldarga_S, paramSymbol.Name, VariableMemberKind.Parameter, declaringMethodName))
                return;
            
            Utils.EnsureNotNull(node.Parent);
            // We only support non-capturing lambda expressions so we handle those as static (even if the code does not mark them explicitly as such)
            // if/when we decide to support lambdas that captures variables/fields/params/etc we will probably need to revisit this.
            var adjustedParameterIndex = paramSymbol.Ordinal + (method.IsStatic || method.MethodKind == MethodKind.AnonymousFunction || method.MethodKind == MethodKind.LocalFunction ? 0 : 1);
            if (adjustedParameterIndex > 3)
            {
                Context.EmitCilInstruction(ilVar, OpCodes.Ldarg, adjustedParameterIndex);
            }
            else
            {
                OpCode[] optimizedLdArgs = { OpCodes.Ldarg_0, OpCodes.Ldarg_1, OpCodes.Ldarg_2, OpCodes.Ldarg_3 };
                Context.EmitCilInstruction(ilVar, optimizedLdArgs[adjustedParameterIndex]);
            }

            HandlePotentialDelegateInvocationOn(node, paramSymbol.Type, ilVar);
            HandlePotentialRefLoad(ilVar, node, paramSymbol.Type);
        }

        protected void ProcessField(string ilVar, SimpleNameSyntax node, IFieldSymbol fieldSymbol)
        {
            var nodeParent = (CSharpSyntaxNode) node.Parent;
            Debug.Assert(nodeParent != null);

            fieldSymbol.EnsureFieldExists(Context, node);
            var resolvedFieldVariable = fieldSymbol.FieldResolverExpression(Context);
            
            if (fieldSymbol.HasConstantValue && fieldSymbol.IsConst)
            {
                LoadLiteralToStackHandlingCallOnValueTypeLiterals(
                    ilVar,
                    fieldSymbol.Type,
                    fieldSymbol.Type.SpecialType switch
                    {
                        SpecialType.System_String => $"\"{fieldSymbol.ConstantValue}\"",
                        SpecialType.System_Boolean => (bool) fieldSymbol.ConstantValue ? 1 : 0,
                        _ => fieldSymbol.ConstantValue
                    },
                    nodeParent.Accept(UsageVisitor.GetInstance(Context)));
                return;
            }

            if (!fieldSymbol.IsStatic && node.IsMemberAccessThroughImplicitThis())
                Context.EmitCilInstruction(ilVar, OpCodes.Ldarg_0);

            if (HandleLoadAddress(ilVar, fieldSymbol.Type, node, fieldSymbol.IsStatic ? OpCodes.Ldsflda : OpCodes.Ldflda, resolvedFieldVariable))
            {
                return;
            }

            if (fieldSymbol.IsVolatile)
                Context.EmitCilInstruction(ilVar, OpCodes.Volatile);

            var opCode = fieldSymbol.LoadOpCodeForFieldAccess();
            Context.EmitCilInstruction(ilVar, opCode, resolvedFieldVariable);
            HandlePotentialDelegateInvocationOn(node, fieldSymbol.Type, ilVar);
            HandlePotentialRefLoad(ilVar, node, fieldSymbol.Type);
        }

        protected void ProcessLocalVariable(string ilVar, SimpleNameSyntax localVarSyntax, ILocalSymbol symbol)
        {
            var operand = Context.DefinitionVariables.GetVariable(symbol.Name, VariableMemberKind.LocalVariable).VariableName;
            if (HandleLoadAddress(ilVar, symbol.Type, localVarSyntax, OpCodes.Ldloca, operand))
                return;

            if (InlineArrayProcessor.HandleInlineArrayConversionToSpan(Context, ilVar, symbol.Type, localVarSyntax, OpCodes.Ldloca_S, symbol.Name, VariableMemberKind.LocalVariable))
                return;

            Context.EmitCilInstruction(ilVar, OpCodes.Ldloc, operand);

            HandlePotentialDelegateInvocationOn(localVarSyntax, symbol.Type, ilVar);
            HandlePotentialFixedLoad(ilVar, symbol);
            HandlePotentialRefLoad(ilVar, localVarSyntax, symbol.Type);
        }
        private void HandlePotentialFixedLoad(string ilVar, ILocalSymbol symbol)
        {
            if (!symbol.IsFixed)
                return;

            Context.EmitCilInstruction(ilVar, OpCodes.Conv_U);
        }

        protected bool HandleLoadAddress(string ilVar, ITypeSymbol loadedType, CSharpSyntaxNode node, OpCode loadOpCode, string operand)
        {
            var parentNode = (CSharpSyntaxNode)node.Parent;
            return HandleCallOnTypeParameter() || HandleCallOnValueType() || HandleRefAssignment() || HandleParameter() || HandleInlineArrayElementAccess();

            bool HandleCallOnValueType()
            {
                if (!loadedType.IsValueType)
                    return false;

                // in this case we need to call System.Index.GetOffset(int32) on a value type (System.Index)
                // which requires the address of the value type.
                var isSystemIndexUsedAsIndex = IsSystemIndexUsedAsIndex(loadedType, parentNode);
                var usageResult = parentNode!.Accept(UsageVisitor.GetInstance(Context).WithTargetNode(node));
                if (isSystemIndexUsedAsIndex || parentNode.IsKind(SyntaxKind.AddressOfExpression) || IsPseudoAssignmentToValueType() || node.IsMemberAccessOnElementAccess() || usageResult.Kind == UsageKind.CallTarget)
                {
                    if (usageResult.Kind == UsageKind.CallTarget && SymbolEqualityComparer.Default.Equals(usageResult.Target.ContainingType, Context.RoslynTypeSystem.SystemObject) && !usageResult.Target.IsVirtual)
                    {
                        Dictionary<short, OpCode> ordinaryLoadMapping = new()
                        {
                            [CecilOpCodes.Ldarga.Value] = OpCodes.Ldarg,
                            [CecilOpCodes.Ldarga_S.Value] = OpCodes.Ldarg_S,
                            
                            [CecilOpCodes.Ldloca.Value] = OpCodes.Ldloc,
                            [CecilOpCodes.Ldloca_S.Value] = OpCodes.Ldloc_S,
                            
                            [CecilOpCodes.Ldflda.Value] = OpCodes.Ldfld,
                            [CecilOpCodes.Ldsflda.Value] = OpCodes.Ldsfld,

                            [CecilOpCodes.Ldelema.Value] = loadedType.LdelemOpCode()
                        };
                        
                        if (!ordinaryLoadMapping.TryGetValue(loadOpCode.Value, out var ordinaryLoad))
                            throw new InvalidOperationException($"Cannot find ordinary load op code for {loadOpCode}");

                        if (loadOpCode == OpCodes.Ldelem)
                            operand = null;
                        
                        Context.EmitCilInstruction(ilVar, ordinaryLoad, operand);
                        Context.EmitCilInstruction(ilVar, OpCodes.Box, Context.TypeResolver.Resolve(loadedType));
                    }
                    else
                        Context.EmitCilInstruction(ilVar, loadOpCode, operand);
                    
                    if (!Context.HasFlag(Constants.ContextFlags.Fixed) && parentNode.IsKind(SyntaxKind.AddressOfExpression))
                        Context.EmitCilInstruction(ilVar, OpCodes.Conv_U);

                    // calls to virtual methods on custom value types needs to be constrained (don't know why, but the generated IL for such scenarios does `constrains`).
                    // the only methods that falls into this category are virtual methods on Object (ToString()/Equals()/GetHashCode())
                    if (usageResult.Target is { IsOverride: true } && usageResult.Target.ContainingType.IsNonPrimitiveValueType(Context))
                        Context.SetFlag(Constants.ContextFlags.MemberReferenceRequiresConstraint, Context.TypeResolver.Resolve(loadedType));
                    return true;
                }

                return false;
            }

            bool HandleCallOnTypeParameter()
            {
                if (loadedType is not ITypeParameterSymbol typeParameter)
                    return false;

                if (typeParameter.HasReferenceTypeConstraint || typeParameter.IsReferenceType)
                    return false;

                if (parentNode.Accept(UsageVisitor.GetInstance(Context)) != UsageKind.CallTarget)
                    return false;

                if (loadOpCode == OpCodes.Ldelema)
                {
                    Context.EmitCilInstruction(ilVar, OpCodes.Readonly);
                }
                
                Context.EmitCilInstruction(ilVar, loadOpCode, operand);
                Context.SetFlag(Constants.ContextFlags.MemberReferenceRequiresConstraint, Context.TypeResolver.Resolve(loadedType));
                return true;
            }

            bool HandleRefAssignment()
            {
                if (!(parentNode is RefExpressionSyntax refExpression))
                    return false;

                var assignedValueSymbol = Context.SemanticModel.GetSymbolInfo(refExpression.Expression);
                if (assignedValueSymbol.Symbol.IsByRef())
                    return false;

                Context.EmitCilInstruction(ilVar, loadOpCode, operand);
                return true;
            }

            bool HandleParameter()
            {
                if (!(parentNode is ArgumentSyntax argument) || !argument.RefOrOutKeyword.IsKind(SyntaxKind.RefKeyword))
                    return false;

                if (Context.SemanticModel.GetSymbolInfo(argument.Expression).Symbol?.IsByRef() == false)
                {
                    Context.EmitCilInstruction(ilVar, loadOpCode, operand);
                    return true;
                }
                return false;
            }

            bool HandleInlineArrayElementAccess()
            {
                if (!node.Parent.IsKind(SyntaxKind.ElementAccessExpression) || !loadedType.TryGetAttribute<InlineArrayAttribute>(out var _))
                    return false;
                
                Context.EmitCilInstruction(ilVar, loadOpCode, operand);
                return true;
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
            var assigment = expression.Ancestors().OfType<AssignmentExpressionSyntax>().SingleOrDefault();
            var mae = expression.Ancestors().OfType<MemberAccessExpressionSyntax>().SingleOrDefault(c => c.Expression == expression);

            if (mae != null)
            {
                needsLoadIndirect = sourceIsByRef && !type.IsValueType;
            }
            else if (assigment != null)
            {
                var targetIsByRef = Context.SemanticModel.GetSymbolInfo(assigment.Left).Symbol.IsByRef();
                needsLoadIndirect =
                    assigment.Left != expression &&
                    (assigment.Right == expression && sourceIsByRef && !targetIsByRef // simple assignment like: nonRef = ref;
                    || sourceIsByRef && !assigment.Right.IsKind(SyntaxKind.RefExpression)); // complex assignment like: nonRef = ref + 10;
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
                var opCode = type.LdindOpCodeFor();
                Context.EmitCilInstruction(ilVar, opCode, opCode == OpCodes.Ldobj ? Context.TypeResolver.Resolve(type) : null);
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
                    ILocalSymbol { Type: IFunctionPointerTypeSymbol } local => ((IFunctionPointerTypeSymbol) local.Type).Signature,
                    ILocalSymbol { Type: INamedTypeSymbol } local => ((INamedTypeSymbol) local.Type).DelegateInvokeMethod,
                    IParameterSymbol { Type: INamedTypeSymbol } param => ((INamedTypeSymbol) param.Type).DelegateInvokeMethod,
                    IParameterSymbol { Type: IFunctionPointerTypeSymbol } param => ((IFunctionPointerTypeSymbol) param.Type).Signature,
                    IFieldSymbol { Type: INamedTypeSymbol } field => ((INamedTypeSymbol) field.Type).DelegateInvokeMethod,
                    IFieldSymbol { Type: IFunctionPointerTypeSymbol } field => ((IFunctionPointerTypeSymbol) field.Type).Signature,
                    IPropertySymbol { Type: INamedTypeSymbol } field => ((INamedTypeSymbol) field.Type).DelegateInvokeMethod,
                    IPropertySymbol { Type: IFunctionPointerTypeSymbol } field => ((IFunctionPointerTypeSymbol) field.Type).Signature,
                    IMethodSymbol m => m,
                    _ => throw new NotImplementedException($"Found not supported symbol {symbol.ToDisplayString()} ({symbol.GetType().Name}) when trying to find index of argument ({argument})")
                };

                Debug.Assert(method != null);
                if (method.Parameters.Length > argumentIndex)
                    return method.Parameters[argumentIndex];
                        
                Debug.Assert(method.Parameters.Last().IsParams);
                return method.Parameters.Last();
            }

            var elementAccess = argument.Ancestors().OfType<ElementAccessExpressionSyntax>().SingleOrDefault();
            if (elementAccess != null)
            {
                var indexerSymbol = Context.SemanticModel.GetIndexerGroup(elementAccess.Expression).FirstOrDefault();
                if (indexerSymbol != null)
                {
                    var argumentIndex = argument.Ancestors().OfType<BracketedArgumentListSyntax>().Single().Arguments.IndexOf(argument);
                    return indexerSymbol.Parameters.ElementAt(argumentIndex);
                }
            }

            return null;
        }

        private bool IsSystemIndexUsedAsIndex(ITypeSymbol symbol, SyntaxNode node)
        {
            if (symbol.MetadataToken != Context.RoslynTypeSystem.SystemIndex.MetadataToken)
                return false;

            return node.Parent.IsKind(SyntaxKind.BracketedArgumentList);
        }

        private void HandlePotentialDelegateInvocationOn(SimpleNameSyntax node, ITypeSymbol typeSymbol, string ilVar)
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
            var resolvedMethod = localDelegateDeclaration != null
                ? $"{localDelegateDeclaration}.Methods.Single(m => m.Name == \"Invoke\")"
                : ((IMethodSymbol) typeSymbol.GetMembers("Invoke").SingleOrDefault()).MethodResolverExpression(Context);

            OnLastInstructionLoadingTargetOfInvocation();
            Context.EmitCilInstruction(ilVar, OpCodes.Callvirt, resolvedMethod);
        }

        /// <summary>
        /// This method must be called when the target of a method invocation has been pushed to the stack
        /// This mechanism is used primarily by <see cref="ExpressionVisitor"/> for fixing call sites (<see cref="ExpressionVisitor.HandleMethodInvocation"/>). 
        /// </summary>
        protected virtual void OnLastInstructionLoadingTargetOfInvocation() { }

        protected void HandleAttributesInMemberDeclaration(in SyntaxList<AttributeListSyntax> nodeAttributeLists, Func<AttributeTargetSpecifierSyntax, SyntaxKind, bool> predicate, SyntaxKind toMatch, string whereToAdd)
        {
            var attributeLists = nodeAttributeLists.Where(c => predicate(c.Target, toMatch));
            HandleAttributesInMemberDeclaration(attributeLists, whereToAdd);
        }

        protected static bool TargetDoesNotMatch(AttributeTargetSpecifierSyntax target, SyntaxKind operand) => target == null || !target.Identifier.IsKind(operand);
        protected static bool TargetMatches(AttributeTargetSpecifierSyntax target, SyntaxKind operand) => target != null && target.Identifier.IsKind(operand);

        protected void HandleAttributesInMemberDeclaration(IEnumerable<AttributeListSyntax> attributeLists, string varName)
        {
            HandleAttributesInMemberDeclaration(Context, attributeLists, varName);
        }

        protected static void HandleAttributesInTypeParameter(IVisitorContext context, IEnumerable<TypeParameterSyntax> typeParameters)
        {
            foreach (var typeParameter in typeParameters)
            {
                var symbol = context.SemanticModel.GetDeclaredSymbol(typeParameter).EnsureNotNull();
                var parentName = symbol.TypeParameterKind == TypeParameterKind.Method ? symbol.DeclaringMethod?.OriginalDefinition.ToDisplayString() : symbol.DeclaringType?.OriginalDefinition.ToDisplayString();
                var typeParamVariable = context.DefinitionVariables.GetVariable(typeParameter.Identifier.Text, VariableMemberKind.TypeParameter, parentName);
                if (!typeParamVariable.IsValid)
                    throw new Exception($"Failed to find variable for {symbol.ToDisplayString()}");
                
                HandleAttributesInMemberDeclaration(context, typeParameter.AttributeLists, typeParamVariable.VariableName);
            }
        }

        private static void HandleAttributesInMemberDeclaration(IVisitorContext context, IEnumerable<AttributeListSyntax> attributeLists, string targetDeclarationVar)
        {
            foreach (var attribute in attributeLists.SelectMany(al => al.Attributes))
            {
                var attributeType = context.SemanticModel.GetSymbolInfo(attribute).Symbol.EnsureNotNull<ISymbol, IMethodSymbol>().ContainingType;

                //https://github.com/adrianoc/cecilifier/issues/311
                TypeDeclarationVisitor.EnsureForwardedTypeDefinition(context, attributeType, attributeType.OriginalDefinition.TypeParameters.SelectMany(tp => tp.DeclaringSyntaxReferences).Select(dsr => (TypeParameterSyntax) dsr.GetSyntax()));

                var attrsExp = attributeType.AttributeKind() switch
                    {
                        AttributeKind.DllImport => ProcessDllImportAttribute(context, attribute, targetDeclarationVar),
                        AttributeKind.StructLayout => ProcessStructLayoutAttribute(attribute, targetDeclarationVar),
                        _ => ProcessNormalMemberAttribute(context, attribute, targetDeclarationVar)
                    };
                
                AddCecilExpressions(context, attrsExp);
            }
        }

        private static IEnumerable<string> ProcessDllImportAttribute(IVisitorContext context, AttributeSyntax attribute, string methodVar)
        {
            var moduleName = attribute.ArgumentList?.Arguments.First().ToFullString();
            var existingModuleVar = context.DefinitionVariables.GetVariable(moduleName, VariableMemberKind.ModuleReference);

            var moduleVar = existingModuleVar.IsValid
                ? existingModuleVar.VariableName
                : context.Naming.SyntheticVariable("dllImportModule", ElementKind.LocalVariable);

            var exps = new List<string>
            {
                $"{methodVar}.PInvokeInfo = new PInvokeInfo({ PInvokeAttributesFrom(attribute) }, { EntryPoint() }, {moduleVar});",
                $"{methodVar}.Body = null;",
                $"{methodVar}.ImplAttributes = {MethodImplAttributes()};",
            };

            if (!existingModuleVar.IsValid)
            {
                exps.InsertRange(0, new[]
                {
                    $"var {moduleVar} = new ModuleReference({moduleName});",
                    $"assembly.MainModule.ModuleReferences.Add({moduleVar});",
                });
            }

            context.DefinitionVariables.RegisterNonMethod("", moduleName, VariableMemberKind.ModuleReference, moduleVar);

            return exps;

            string EntryPoint() => attribute.ArgumentList?.Arguments.FirstOrDefault(arg => arg.NameEquals?.Name.Identifier.Text == "EntryPoint")?.Expression.ToString() ?? "\"\"";

            string MethodImplAttributes()
            {
                var preserveSig = Boolean.Parse(AttributePropertyOrDefaultValue(attribute, "PreserveSig", "true"));
                return preserveSig
                    ? "MethodImplAttributes.PreserveSig | MethodImplAttributes.Managed"
                    : "MethodImplAttributes.Managed";
            }

            StringBuilder CallingConventionFrom(AttributeSyntax attr)
            {
                var callConventionSpan = (attr.ArgumentList?.Arguments.FirstOrDefault(arg => arg.NameEquals?.Name.Identifier.Text == "CallingConvention")?.Expression.ToFullString()
                                           ?? "Winapi").AsSpan();

                // ensures we use the enum member simple name; Parse() fails if we pass a qualified enum member
                var index = callConventionSpan.LastIndexOf('.');
                callConventionSpan = callConventionSpan.Slice(index + 1);

                return new StringBuilder(CallingConventionToCecil(Enum.Parse<CallingConvention>(callConventionSpan)));
            }

            string CharSetFrom(AttributeSyntax attr)
            {
                var enumMemberName = AttributePropertyOrDefaultValue(attr, "CharSet", "None").AsSpan();

                // Only use the actual enum member name Parse() fails if we pass a qualified enum member
                var index = enumMemberName.LastIndexOf('.');
                enumMemberName = enumMemberName.Slice(index + 1);

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
                    .AppendModifier(ThrowOnUnmappableCharFrom(attr))
                    .ToString();
            }

            string AttributePropertyOrDefaultValue(AttributeSyntax attr, string propertyName, string defaultValue)
            {
                return attr.ArgumentList?.Arguments.FirstOrDefault(arg => arg.NameEquals?.Name.Identifier.Text == propertyName)?.Expression.ToFullString() ?? defaultValue;
            }
        }

        private static IEnumerable<string> ProcessStructLayoutAttribute(AttributeSyntax attribute, string typeVar)
        {
            Debug.Assert(attribute.ArgumentList != null);
            if (attribute.ArgumentList.Arguments.Count == 0 || attribute.ArgumentList.Arguments.All(a => a.NameEquals == null))
                return Array.Empty<string>();

            return new[]
            {
                $"{typeVar}.ClassSize = { AssignedValue(attribute, "Size") };",
                $"{typeVar}.PackingSize = { AssignedValue(attribute, "Pack") };",
            };

            static int AssignedValue(AttributeSyntax attribute, string parameterName)
            {
                // whenever Size/Pack are omitted the corresponding property should be set to 0. See Ecma-335 II 22.8.
                var parameterAssignmentExpression = (LiteralExpressionSyntax) attribute.ArgumentList?.Arguments.FirstOrDefault(a => a.NameEquals?.Name.Identifier.Text == parameterName)?.Expression;
                return parameterAssignmentExpression?.TryGetLiteralValueFor<int>(out var ret) == true ? ret : 0;
            }
        }

        private static string CallingConventionToCecil(CallingConvention callingConvention)
        {
            var pinvokeAttribute = callingConvention switch
            {
                CallingConvention.Cdecl => PInvokeAttributes.CallConvCdecl,
                CallingConvention.Winapi => PInvokeAttributes.CallConvWinapi,
                CallingConvention.FastCall => PInvokeAttributes.CallConvFastcall,
                CallingConvention.StdCall => PInvokeAttributes.CallConvStdCall,
                CallingConvention.ThisCall => PInvokeAttributes.CallConvThiscall,

                _ => throw new Exception($"Unexpected calling convention: {callingConvention}")
            };

            return $"PInvokeAttributes.{pinvokeAttribute.ToString()}";
        }

        private static IEnumerable<string> ProcessNormalMemberAttribute(IVisitorContext context, AttributeSyntax attribute, string targetDeclarationVar)
        {
            var attrsExp = CecilDefinitionsFactory.Attribute(targetDeclarationVar, context, attribute, (attrType, attrArgs) =>
            {
                var typeVar = context.TypeResolver.ResolveLocalVariableType(attrType);
                if (typeVar == null)
                {
                    //attribute is not declared in the same assembly....
                    var ctorArgumentTypes = $"new Type[{attrArgs.Length}] {{ {string.Join(",", attrArgs.Select(arg => $"typeof({context.GetTypeInfo(arg.Expression).Type?.Name})"))} }}";
                    return Utils.ImportFromMainModule($"typeof({attrType.FullyQualifiedName()}).GetConstructor({ctorArgumentTypes})");
                }

                // Attribute is defined in the same assembly. We need to find the variable that holds its "ctor declaration"
                var attrCtor = attrType.GetMembers().OfType<IMethodSymbol>().SingleOrDefault(m => m.MethodKind == MethodKind.Constructor && m.Parameters.Length == attrArgs.Length);
                context.EnsureForwardedMethod(attrCtor);

                return attrCtor.MethodResolverExpression(context);
            });

            return attrsExp;
        }

        protected void LogUnsupportedSyntax(SyntaxNode node)
        {
            LogWarning($"Syntax {node.Kind()} ({node.HumanReadableSummary()}) is not supported.", node);
            var lineSpan = node.GetLocation().GetLineSpan();
            AddCecilExpression($"/* Syntax '{node.Kind()}' is not supported in {lineSpan.Path} ({lineSpan.Span.Start.Line + 1},{lineSpan.Span.Start.Character + 1}):\n------\n{node}\n----*/");
        }

        private void LogWarning(string message, SyntaxNode node)
        {
            Context.EmitWarning($"{message}\nGenerated code may not compile, or if it compiles, produce invalid results.", node);
        }

        // Methods implementing explicit interfaces, static abstract methods from interfaces and overriden methods with covariant return types
        // needs to explicitly specify which methods they override.
        protected void AddToOverridenMethodsIfAppropriated(string methodVar, IMethodSymbol method)
        {
            var overridenMethod = GetOverridenMethod(method);
            if (overridenMethod != null)
                WriteCecilExpression(Context, $"{methodVar}.Overrides.Add({overridenMethod});");
        }
        
        protected string GetOverridenMethod(IMethodSymbol method)
        {
            // first check explicit interface implementation...
            var overridenMethod = method?.ExplicitInterfaceImplementations.FirstOrDefault();
            if (overridenMethod == null)
            {
                if (method.HasCovariantReturnType())
                {
                    overridenMethod = method.OverriddenMethod;
                }
                else
                {
                    // if it is not an explicit interface implementation check for abstract static method from interfaces
                    var lastDeclared = method.FindLastDefinition(method.ContainingType.Interfaces);
                    if (lastDeclared == null || SymbolEqualityComparer.Default.Equals(lastDeclared, method) || lastDeclared.ContainingType.TypeKind != TypeKind.Interface || method.IsStatic == false)
                        return null;

                    overridenMethod = lastDeclared;
                }
            }

            return overridenMethod.MethodResolverExpression(Context);
        }
    }
}
