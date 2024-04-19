using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Naming;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mono.Cecil;
using Mono.Cecil.Cil;
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

        protected IVisitorContext Context { get; }

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
            Context.WriteCecilExpression(string.Format(format, args));
            Context.WriteNewLine();
        }

        protected void AddMethodCall(string ilVar, IMethodSymbol method, MethodDispatchInformation dispatchInformation = MethodDispatchInformation.MostLikelyVirtual)
        {
            var needsVirtualDispatch = (method.IsVirtual || method.IsAbstract || method.IsOverride) && !method.ContainingType.IsPrimitiveType();

            var opCode = !method.IsStatic
                         && dispatchInformation != MethodDispatchInformation.NonVirtual
                         && (dispatchInformation != MethodDispatchInformation.MostLikelyNonVirtual || needsVirtualDispatch) 
                         && (method.ContainingType.TypeKind == TypeKind.TypeParameter || !method.ContainingType.IsValueType || needsVirtualDispatch)
                ? OpCodes.Callvirt
                : OpCodes.Call;

            if (!method.IsDefinedInCurrentAssembly(Context))
                EnsureForwardedMethod(Context, method);
            
            var operand = method.MethodResolverExpression(Context);
            if (method.IsGenericMethod && (method.IsDefinedInCurrentAssembly(Context) || method.TypeArguments.Any(t => t.TypeKind == TypeKind.TypeParameter)))
            {
                // If the generic method is an open one or if it is defined in the same assembly then the call need to happen in the generic instance method (note that for 
                // methods defined in the snippet being cecilified, even if 'method' represents a generic instance method, MethodResolverExpression() will return the open
                // generic one instead).
                operand = operand.MakeGenericInstanceMethod(Context, method.Name, method.TypeArguments.Select(t => Context.TypeResolver.Resolve(t)).ToArray());
            }

            if (Context.TryGetFlag(Constants.ContextFlags.MemberReferenceRequiresConstraint, out var constrainedType))
            {
                Context.EmitCilInstruction(ilVar, OpCodes.Constrained, constrainedType);
                Context.ClearFlag(Constants.ContextFlags.MemberReferenceRequiresConstraint);
            }
            Context.EmitCilInstruction(ilVar, opCode, operand);
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
            var instVar = Context.Naming.Instruction(opCode.Code.ToString());
            AddCecilExpression($"var {instVar} = {ilVar}.Create({opCode.ConstantName()}{operandStr});");

            return instVar;
        }

        protected string CreateCilInstruction(string ilVar, string instVar, OpCode opCode, object operand = null)
        {
            var operandStr = operand == null ? string.Empty : $", {operand}";
            AddCecilExpression($"var {instVar} = {ilVar}.Create({opCode.ConstantName()}{operandStr});");
            return instVar;
        }

        protected void LoadLiteralValue(string ilVar, ITypeSymbol type, string value, bool isTargetOfCall, SyntaxNode parent)
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
                    LoadLiteralToStackHandlingCallOnValueTypeLiterals(ilVar, type, (int) value[0], isTargetOfCall);
                    break;

                case SpecialType.System_Byte:
                case SpecialType.System_SByte:
                case SpecialType.System_Int16:
                case SpecialType.System_Int32:
                case SpecialType.System_UInt32:
                case SpecialType.System_Int64:
                case SpecialType.System_UInt64:
                case SpecialType.System_Double:
                case SpecialType.System_Single:
                    LoadLiteralToStackHandlingCallOnValueTypeLiterals(ilVar, type, value, isTargetOfCall);
                    break;

                case SpecialType.System_Boolean:
                    LoadLiteralToStackHandlingCallOnValueTypeLiterals(ilVar, type, Boolean.Parse(value) ? 1 : 0, isTargetOfCall);
                    break;

                case SpecialType.System_IntPtr:
                    LoadLiteralToStackHandlingCallOnValueTypeLiterals(ilVar, type, value, isTargetOfCall);
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
                var storageVariable = AddLocalVariableToCurrentMethod(Context, type.Name, resolvedType);
                
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

        private void LoadLiteralToStackHandlingCallOnValueTypeLiterals(string ilVar, ITypeSymbol literalType, object literalValue, bool isTargetOfCall)
        {
            var opCode = literalType.LoadOpCodeFor();
            Context.EmitCilInstruction(ilVar, opCode, literalValue);
            if (isTargetOfCall)
                StoreTopOfStackInLocalVariableAndLoadItsAddress(ilVar, literalType);
        }

        private void StoreTopOfStackInLocalVariableAndLoadItsAddress(string ilVar, ITypeSymbol type, string variableName = "tmp")
        {
            var tempLocalName = StoreTopOfStackInLocalVariable(Context, ilVar, variableName, type).VariableName;
            Context.EmitCilInstruction(ilVar, OpCodes.Ldloca_S, tempLocalName);
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

        protected static string TypeModifiersToCecil(INamedTypeSymbol typeSymbol, SyntaxTokenList modifiers)
        {
            var hasStaticCtor = typeSymbol.Constructors.Any(ctor => ctor.IsStatic && !ctor.IsImplicitlyDeclared);
            var typeAttributes = new StringBuilder(CecilDefinitionsFactory.DefaultTypeAttributeFor(typeSymbol.TypeKind, hasStaticCtor));
            AppendStructLayoutTo(typeSymbol, typeAttributes);
            if (typeSymbol.ContainingType != null)
            {
                if (modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword)))
                {
                    typeAttributes = typeAttributes.AppendModifier(Constants.Cecil.StaticTypeAttributes);
                    modifiers = modifiers.Remove(SyntaxFactory.Token(SyntaxKind.StaticKeyword));
                }
                return typeAttributes.AppendModifier(ModifiersToCecil(modifiers, m => "TypeAttributes.Nested" + m.ValueText.PascalCase())).ToString();
            }

            var convertedModifiers = ModifiersToCecil<TypeAttributes>(modifiers, "NotPublic", MapAttribute);
            return typeAttributes.AppendModifier(convertedModifiers).ToString();

            IEnumerable<string> MapAttribute(SyntaxToken token)
            {
                var isModifierWithNoILRepresentation =
                    token.IsKind(SyntaxKind.PartialKeyword)
                    || token.IsKind(SyntaxKind.VolatileKeyword)
                    || token.IsKind(SyntaxKind.UnsafeKeyword)
                    || token.IsKind(SyntaxKind.AsyncKeyword)
                    || token.IsKind(SyntaxKind.ExternKeyword)
                    || token.IsKind(SyntaxKind.ReadOnlyKeyword)
                    || token.IsKind(SyntaxKind.RefKeyword);

                if (isModifierWithNoILRepresentation)
                    return Array.Empty<string>();

                var mapped = token.Kind() switch
                {
                    SyntaxKind.InternalKeyword => "NotPublic",
                    SyntaxKind.ProtectedKeyword => "Family",
                    SyntaxKind.PrivateKeyword => "Private",
                    SyntaxKind.PublicKeyword => "Public",
                    SyntaxKind.StaticKeyword => "Abstract | TypeAttributes.Sealed",
                    SyntaxKind.AbstractKeyword => "Abstract",
                    SyntaxKind.SealedKeyword => "Sealed",

                    _ => throw new ArgumentException()
                };

                return new[] { mapped };
            }
        }

        private static void AppendStructLayoutTo(ITypeSymbol typeSymbol, StringBuilder typeAttributes)
        {
            if (typeSymbol.TypeKind != TypeKind.Struct)
                return;

            if (!typeSymbol.TryGetAttribute<StructLayoutAttribute>(out var structLayoutAttribute))
            {
                typeAttributes.AppendModifier("TypeAttributes.SequentialLayout");
            }
            else
            {
                var specifiedLayout = ((LayoutKind) structLayoutAttribute.ConstructorArguments.First().Value) switch
                {
                    LayoutKind.Auto => "TypeAttributes.AutoLayout",
                    LayoutKind.Explicit => "TypeAttributes.ExplicitLayout",
                    LayoutKind.Sequential => "TypeAttributes.SequentialLayout",
                    _ => throw new ArgumentException($"Invalid StructLayout value for {typeSymbol.Name}")
                };

                typeAttributes.AppendModifier(specifiedLayout);
            }
        }

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

        private static string ModifiersToCecil(IEnumerable<SyntaxToken> modifiers, Func<SyntaxToken, string> map)
        {
            var cecilModifierStr = modifiers.Aggregate(new StringBuilder(), (acc, token) =>
            {
                acc.AppendModifier(map(token));
                return acc;
            });

            return cecilModifierStr.ToString();
        }

        protected static void WriteCecilExpression(IVisitorContext context, string value)
        {
            context.WriteCecilExpression(value);
            context.WriteNewLine();
        }

        protected string ResolveExpressionType(ExpressionSyntax expression)
        {
            Utils.EnsureNotNull(expression);
            var info = Context.GetTypeInfo(expression);
            return Context.TypeResolver.Resolve(info.Type);
        }

        protected string ResolveType(TypeSyntax type)
        {
            // TODO: Ensure there are tests covering all the derived types from TypeSyntax (https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.csharp.syntax.typesyntax?view=roslyn-dotnet-4.7.0)
            var typeToCheck = type switch
            {
                RefTypeSyntax refType  => refType.Type,
                ScopedTypeSyntax scopedTypeSyntax => scopedTypeSyntax.Type,
                _ => type
            };
        
            var typeInfo = Context.GetTypeInfo(typeToCheck);

            TypeDeclarationVisitor.EnsureForwardedTypeDefinition(Context, typeInfo.Type, Array.Empty<TypeParameterSyntax>());

            var resolvedType = Context.TypeResolver.Resolve(typeInfo.Type);
            return type is RefTypeSyntax ? resolvedType.MakeByReferenceType() : resolvedType;
        }

        protected void ProcessParameter(string ilVar, SimpleNameSyntax node, IParameterSymbol paramSymbol)
        {
            var method = (IMethodSymbol) paramSymbol.ContainingSymbol;
            var declaringMethodName = method.ToDisplayString();
            if (HandleLoadAddressOnStorage(ilVar, paramSymbol.Type, node, OpCodes.Ldarga, paramSymbol.Name, VariableMemberKind.Parameter, declaringMethodName))
                return;

            if (InlineArrayProcessor.HandleInlineArrayConversionToSpan(Context, ilVar, paramSymbol.Type, node, OpCodes.Ldarga_S, paramSymbol.Name, VariableMemberKind.Parameter, declaringMethodName))
                return;
            
            Utils.EnsureNotNull(node.Parent);
            // We only support non-capturing lambda expressions so we handle those as static (even if the code does not mark them explicitly as such)
            // if/when we decide to support lambdas that captures variables/fields/params/etc we will probably need to revisit this.
            var adjustedParameterIndex = paramSymbol.Ordinal + (method.IsStatic || method.MethodKind == MethodKind.AnonymousFunction ? 0 : 1);
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
                    nodeParent.Accept(UsageVisitor.GetInstance(Context)) == UsageKind.CallTarget);
                return;
            }

            var fieldDeclarationVariable = fieldSymbol.EnsureFieldExists(Context, node);

            if (!fieldSymbol.IsStatic && !node.IsQualifiedAccessToTypeOrMember())
                Context.EmitCilInstruction(ilVar, OpCodes.Ldarg_0);

            if (HandleLoadAddressOnStorage(ilVar, fieldSymbol.Type, node, fieldSymbol.IsStatic ? OpCodes.Ldsflda : OpCodes.Ldflda, fieldSymbol.Name, VariableMemberKind.Field, fieldSymbol.ContainingType.ToDisplayString()))
            {
                return;
            }

            if (fieldSymbol.IsVolatile)
                Context.EmitCilInstruction(ilVar, OpCodes.Volatile);

            var resolvedField = fieldDeclarationVariable.IsValid
                ? fieldDeclarationVariable.VariableName
                : fieldSymbol.FieldResolverExpression(Context);

            var opCode = fieldSymbol.LoadOpCodeForFieldAccess();
            Context.EmitCilInstruction(ilVar, opCode, resolvedField);

            HandlePotentialDelegateInvocationOn(node, fieldSymbol.Type, ilVar);
        }

        protected void ProcessLocalVariable(string ilVar, SimpleNameSyntax localVarSyntax, ILocalSymbol symbol)
        {
            if (HandleLoadAddressOnStorage(ilVar, symbol.Type, localVarSyntax, OpCodes.Ldloca, symbol.Name, VariableMemberKind.LocalVariable))
                return;

            if (InlineArrayProcessor.HandleInlineArrayConversionToSpan(Context, ilVar, symbol.Type, localVarSyntax, OpCodes.Ldloca_S, symbol.Name, VariableMemberKind.LocalVariable))
                return;
            
            var operand = Context.DefinitionVariables.GetVariable(symbol.Name, VariableMemberKind.LocalVariable).VariableName;
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

        private bool HandleLoadAddressOnStorage(string ilVar, ITypeSymbol symbol, CSharpSyntaxNode node, OpCode opCode, string symbolName, VariableMemberKind variableMemberKind, string parentName = null)
        {
            var operand = Context.DefinitionVariables.GetVariable(symbolName, variableMemberKind, parentName).VariableName;
            return HandleLoadAddress(ilVar, symbol, node, opCode, operand);
        }

        protected bool HandleLoadAddress(string ilVar, ITypeSymbol symbol, CSharpSyntaxNode node, OpCode opCode, string operand)
        {
            var parentNode = (CSharpSyntaxNode)node.Parent;
            return HandleCallOnTypeParameter() || HandleCallOnValueType() || HandleRefAssignment() || HandleParameter() || HandleInlineArrayElementAccess();

            bool HandleCallOnValueType()
            {
                if (!symbol.IsValueType)
                    return false;

                // in this case we need to call System.Index.GetOffset(int32) on a value type (System.Index)
                // which requires the address of the value type.
                var isSystemIndexUsedAsIndex = IsSystemIndexUsedAsIndex(symbol, parentNode);
                var usageResult = parentNode.Accept(UsageVisitor.GetInstance(Context));
                if (isSystemIndexUsedAsIndex || parentNode.IsKind(SyntaxKind.AddressOfExpression) || IsPseudoAssignmentToValueType() || node.IsMemberAccessOnElementAccess() || usageResult.Kind == UsageKind.CallTarget)
                {
                    Context.EmitCilInstruction(ilVar, opCode, operand);
                    if (!Context.HasFlag(Constants.ContextFlags.Fixed) && parentNode.IsKind(SyntaxKind.AddressOfExpression))
                        Context.EmitCilInstruction(ilVar, OpCodes.Conv_U);

                    // calls to virtual methods on custom value types needs to be constrained (don't know why, but the generated IL for such scenarios does `constrains`).
                    // the only methods that falls into this category are virtual methods on Object (ToString()/Equals()/GetHashCode())
                    if (usageResult.Target is { IsOverride: true } && usageResult.Target.ContainingType.IsNonPrimitiveValueType(Context))
                        Context.SetFlag(Constants.ContextFlags.MemberReferenceRequiresConstraint, Context.TypeResolver.Resolve(symbol));
                    return true;
                }

                return false;
            }

            bool HandleCallOnTypeParameter()
            {
                if (symbol is not ITypeParameterSymbol typeParameter)
                    return false;

                if (typeParameter.HasReferenceTypeConstraint || typeParameter.IsReferenceType)
                    return false;

                if (parentNode.Accept(UsageVisitor.GetInstance(Context)) != UsageKind.CallTarget)
                    return false;

                Context.EmitCilInstruction(ilVar, opCode, operand);
                Context.SetFlag(Constants.ContextFlags.MemberReferenceRequiresConstraint, Context.TypeResolver.Resolve(symbol));
                return true;
            }

            bool HandleRefAssignment()
            {
                if (!(parentNode is RefExpressionSyntax refExpression))
                    return false;

                var assignedValueSymbol = Context.SemanticModel.GetSymbolInfo(refExpression.Expression);
                if (assignedValueSymbol.Symbol.IsByRef())
                    return false;

                Context.EmitCilInstruction(ilVar, opCode, operand);
                return true;
            }

            bool HandleParameter()
            {
                if (!(parentNode is ArgumentSyntax argument) || !argument.RefOrOutKeyword.IsKind(SyntaxKind.RefKeyword))
                    return false;

                if (Context.SemanticModel.GetSymbolInfo(argument.Expression).Symbol?.IsByRef() == false)
                {
                    Context.EmitCilInstruction(ilVar, opCode, operand);
                    return true;
                }
                return false;
            }

            bool HandleInlineArrayElementAccess()
            {
                if (!node.Parent.IsKind(SyntaxKind.ElementAccessExpression) || !symbol.TryGetAttribute<InlineArrayAttribute>(out var _))
                    return false;
                
                Context.EmitCilInstruction(ilVar, opCode, operand);
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
                Context.EmitCilInstruction(ilVar, type.LdindOpCodeFor());
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

                return method.Parameters[argumentIndex];
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

            //TODO: Find all call sites that adds a Call/Callvirt instruction and make sure
            //      they call X().
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
                var parentName = symbol.TypeParameterKind == TypeParameterKind.Method ? symbol.DeclaringMethod?.Name : symbol.DeclaringType?.Name;
                var found = context.DefinitionVariables.GetVariable(typeParameter.Identifier.Text, VariableMemberKind.TypeParameter, parentName);
                HandleAttributesInMemberDeclaration(context, typeParameter.AttributeLists, found.VariableName);
            }
        }

        private static void HandleAttributesInMemberDeclaration(IVisitorContext context, IEnumerable<AttributeListSyntax> attributeLists, string targetDeclarationVar)
        {
            foreach (var attribute in attributeLists.SelectMany(al => al.Attributes))
            {
                var type = context.SemanticModel.GetSymbolInfo(attribute).Symbol.EnsureNotNull<ISymbol, IMethodSymbol>().ContainingType;

                //TODO: Pass the correct list of type parameters when C# supports generic attributes.
                TypeDeclarationVisitor.EnsureForwardedTypeDefinition(context, type, Array.Empty<TypeParameterSyntax>());

                var attrsExp = type.AttributeKind() switch
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

            string EntryPoint() => attribute?.ArgumentList?.Arguments.FirstOrDefault(arg => arg.NameEquals?.Name.Identifier.Text == "EntryPoint")?.Expression.ToString() ?? "\"\"";

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
                EnsureForwardedMethod(context, attrCtor);

                return attrCtor.MethodResolverExpression(context);
            });

            return attrsExp;
        }

        /*
         * Ensure forward member references are correctly handled, i.e, support for scenario in which a method is being referenced
         * before it has been declared. This can happen for instance in code like:
         *
         * class C
         * {
         *     void Foo() { Bar(); }
         *     void Bar() {}
         * }
         *
         * In this case when the first reference to Bar() is found (in method Foo()) the method itself has not been defined yet
         * so we add a MethodDefinition for it but *no body*. Method body will be processed later, when the method is visited.
         */
        protected static void EnsureForwardedMethod(IVisitorContext context, IMethodSymbol method)
        {
            if (!method.IsDefinedInCurrentAssembly(context))
                return;

            var found = context.DefinitionVariables.GetMethodVariable(method.AsMethodDefinitionVariable());
            if (found.IsValid)
                return;

            string methodDeclarationVar;
            var methodName = method.Name;
            if (method.MethodKind == MethodKind.LocalFunction)
            {
                methodDeclarationVar = context.Naming.SyntheticVariable(method.Name, ElementKind.Method);
                methodName = $"<{method.ContainingSymbol.Name}>g__{method.Name}|0_0";
            }
            else
            {
                methodDeclarationVar = method.MethodKind == MethodKind.Constructor
                    ? context.Naming.Constructor((BaseTypeDeclarationSyntax) method.ContainingType.DeclaringSyntaxReferences.SingleOrDefault()?.GetSyntax(), method.IsStatic)
                    : context.Naming.MethodDeclaration((BaseMethodDeclarationSyntax) method.DeclaringSyntaxReferences.SingleOrDefault()?.GetSyntax());
            }

            var exps = CecilDefinitionsFactory.Method(context, methodDeclarationVar, methodName, "MethodAttributes.Private", method.ReturnType, method.ReturnsByRef, method.GetTypeParameterSyntax());
            context.WriteCecilExpressions(exps);
            
            foreach (var parameter in method.Parameters)
            {
                var parameterExp = CecilDefinitionsFactory.Parameter(parameter, context.TypeResolver.Resolve(parameter.Type));
                var paramVar = context.Naming.Parameter(parameter.Name);
                context.WriteCecilExpression($"var {paramVar} = {parameterExp};");
                context.WriteNewLine();
                context.WriteCecilExpression($"{methodDeclarationVar}.Parameters.Add({paramVar});");
                context.WriteNewLine();
            
                context.DefinitionVariables.RegisterNonMethod(method.ToDisplayString(), parameter.Name, VariableMemberKind.Parameter, paramVar);
            }
            
            context.DefinitionVariables.RegisterMethod(method.AsMethodDefinitionVariable(methodDeclarationVar));
        }

        protected void LogUnsupportedSyntax(SyntaxNode node)
        {
            var lineSpan = node.GetLocation().GetLineSpan();
            AddCecilExpression($"/* Syntax '{node.Kind()}' is not supported in {lineSpan.Path} ({lineSpan.Span.Start.Line + 1},{lineSpan.Span.Start.Character + 1}):\n------\n{node}\n----*/");
        }

        // Methods implementing explicit interfaces, static abstract methods from interfaces and overriden methods with covariant return types
        // needs to explicitly specify which methods they override.
        protected void AddToOverridenMethodsIfAppropriated(string methodVar, IMethodSymbol method)
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
                    if (lastDeclared == null || SymbolEqualityComparer.Default.Equals(lastDeclared, method) || lastDeclared.ContainingType.TypeKind != TypeKind.Interface || method?.IsStatic == false)
                        return;

                    overridenMethod = lastDeclared;
                }
            }

            WriteCecilExpression(Context, $"{methodVar}.Overrides.Add({overridenMethod.MethodResolverExpression(Context)});");
        }
    }
}
