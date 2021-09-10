﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Naming;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mono.Cecil.Cil;

namespace Cecilifier.Core.AST
{
    internal class ExpressionVisitor : SyntaxWalkerBase
    {
        private static readonly IDictionary<SyntaxKind, Action<IVisitorContext, string, ITypeSymbol, ITypeSymbol>> operatorHandlers =
            new Dictionary<SyntaxKind, Action<IVisitorContext, string, ITypeSymbol, ITypeSymbol>>();
        
        private static readonly IDictionary<string, uint> predefinedTypeSize = new Dictionary<string, uint>();
        private static readonly IDictionary<SpecialType, OpCode> _opCodesForLdElem = new Dictionary<SpecialType, OpCode>()
        {
            [SpecialType.System_Byte] = OpCodes.Ldelem_I1,
            [SpecialType.System_Boolean] = OpCodes.Ldelem_U1,
            [SpecialType.System_Int16] = OpCodes.Ldelem_I2,
            [SpecialType.System_Int32] = OpCodes.Ldelem_I4,
            [SpecialType.System_Int64] = OpCodes.Ldelem_I8,
            [SpecialType.System_Single] = OpCodes.Ldelem_R4,
            [SpecialType.System_Double] = OpCodes.Ldelem_R8,
            [SpecialType.System_Object] = OpCodes.Ldelem_Ref,
        };
        
        private readonly string ilVar;
        private readonly Stack<LinkedListNode<string>> callFixList = new Stack<LinkedListNode<string>>();

        private bool valueTypeNoArgObjCreation;

        static ExpressionVisitor()
        {
            predefinedTypeSize["int"] = sizeof(int);
            predefinedTypeSize["byte"] = sizeof(byte);
            predefinedTypeSize["long"] = sizeof(long);
            
            //TODO: Use AddCilInstruction instead.
            operatorHandlers[SyntaxKind.PlusToken] = (ctx, ilVar, left, right) =>
            {
                if (left.SpecialType == SpecialType.System_String)
                {
                    var concatArgType = right.SpecialType == SpecialType.System_String ? "string" : "object";
                    WriteCecilExpression(ctx,$"{ilVar}.Append({ilVar}.Create({OpCodes.Call.ConstantName()}, assembly.MainModule.Import(typeof(string).GetMethod(\"Concat\", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public, null, new[] {{ typeof({concatArgType}), typeof({concatArgType}) }}, null))));");
                }
                else
                {
                    WriteCecilExpression(ctx, "{0}.Append({0}.Create({1}));", ilVar, OpCodes.Add.ConstantName());
                }
            };

            operatorHandlers[SyntaxKind.SlashToken] = (ctx, ilVar, left, right) => WriteCecilExpression(ctx, $"{ilVar}.Append({ilVar}.Create({OpCodes.Div.ConstantName()}));");
            operatorHandlers[SyntaxKind.GreaterThanToken] = (ctx, ilVar, left, right) => WriteCecilExpression(ctx, $"{ilVar}.Append({ilVar}.Create({CompareOperatorFor(ctx, left, right).ConstantName()}));");
            operatorHandlers[SyntaxKind.EqualsEqualsToken] = (ctx, ilVar, left, right) => WriteCecilExpression(ctx, $"{ilVar}.Append({ilVar}.Create({OpCodes.Ceq.ConstantName()}));");
            operatorHandlers[SyntaxKind.LessThanToken] = (ctx, ilVar, left, right) => WriteCecilExpression(ctx, $"{ilVar}.Append({ilVar}.Create({OpCodes.Clt.ConstantName()}));");
            operatorHandlers[SyntaxKind.MinusToken] = (ctx, ilVar, left, right) => WriteCecilExpression(ctx, $"{ilVar}.Append({ilVar}.Create({OpCodes.Sub.ConstantName()}));");
            operatorHandlers[SyntaxKind.AsteriskToken] = (ctx, ilVar, left, right) => WriteCecilExpression(ctx, $"{ilVar}.Append({ilVar}.Create({OpCodes.Mul.ConstantName()}));");

            // Bitwise Operators
            operatorHandlers[SyntaxKind.AmpersandToken] = (ctx, ilVar, _, _) => WriteCecilExpression(ctx, $"{ilVar}.Append({ilVar}.Create({OpCodes.And.ConstantName()}));");
            operatorHandlers[SyntaxKind.BarToken] = (ctx, ilVar, _, _) => WriteCecilExpression(ctx, $"{ilVar}.Append({ilVar}.Create({OpCodes.Or.ConstantName()}));");
            operatorHandlers[SyntaxKind.CaretToken] = (ctx, ilVar, _, _) => WriteCecilExpression(ctx, $"{ilVar}.Append({ilVar}.Create({OpCodes.Xor.ConstantName()}));");
            operatorHandlers[SyntaxKind.LessThanLessThanToken] = (ctx, ilVar, _, _) => WriteCecilExpression(ctx, $"{ilVar}.Append({ilVar}.Create({OpCodes.Shl.ConstantName()}));");
            operatorHandlers[SyntaxKind.GreaterThanGreaterThanToken] = (ctx, ilVar, _, _) => WriteCecilExpression(ctx, $"{ilVar}.Append({ilVar}.Create({OpCodes.Shr.ConstantName()}));");

            // Logical Operators
            operatorHandlers[SyntaxKind.AmpersandAmpersandToken] = (ctx, ilVar, _, _) => WriteCecilExpression(ctx, $"{ilVar}.Append({ilVar}.Create({OpCodes.And.ConstantName()}));");
            operatorHandlers[SyntaxKind.BarBarToken] = (ctx, ilVar, _, _) => WriteCecilExpression(ctx, $"{ilVar}.Append({ilVar}.Create({OpCodes.Or.ConstantName()}));");
        }

        private static OpCode CompareOperatorFor(IVisitorContext ctx, ITypeSymbol left, ITypeSymbol right)
        {
            switch (left.SpecialType)
            {
            case SpecialType.System_UInt16:
            case SpecialType.System_UInt32:
            case SpecialType.System_UInt64:
            case SpecialType.System_Byte:
                return OpCodes.Cgt_Un;
            
            case SpecialType.None:
                return left.Kind == SymbolKind.PointerType ? OpCodes.Cgt_Un : OpCodes.Cgt;
                
            default:
                return OpCodes.Cgt;
            }
        }

        internal ExpressionVisitor(IVisitorContext ctx, string ilVar) : base(ctx)
        {
            this.ilVar = ilVar;
        }

        internal static bool Visit(IVisitorContext ctx, string ilVar, SyntaxNode node)
        {
            if (node == null)
            {
                return true;
            }

            var ev = new ExpressionVisitor(ctx, ilVar);
            ev.Visit(node);

            return ev.valueTypeNoArgObjCreation;
        }

        public override void VisitReturnStatement(ReturnStatementSyntax node)
        {
            node.Expression.Accept(this);
            InjectRequiredConversions(node.Expression);
        }

        public override void VisitArrowExpressionClause(ArrowExpressionClauseSyntax node)
        {
            node.Expression.Accept(this);
            InjectRequiredConversions(node.Expression);
        }

        public override void VisitStackAllocArrayCreationExpression(StackAllocArrayCreationExpressionSyntax node)
        {
            /*
                // S *s = stackalloc S[n];
                IL_0007: ldarg.1
                IL_0008: conv.u
                IL_0009: sizeof MyStruct
                IL_000f: mul.ovf.un
                IL_0010: localloc
                
                // int *i = stackalloc int[10];
                IL_0001: ldc.i4.s 40
                IL_0003: conv.u
                IL_0004: localloc
             */
            var type = (ArrayTypeSyntax) node.Type;
            var countNode = type.RankSpecifiers[0].Sizes[0];
            if (type.RankSpecifiers.Count == 1 && countNode.IsKind(SyntaxKind.NumericLiteralExpression))
            {
                var sizeLiteral = Int32.Parse(countNode.GetFirstToken().Text) * predefinedTypeSize[type.ElementType.GetText().ToString()];
                
                AddCilInstruction(ilVar, OpCodes.Ldc_I4, sizeLiteral);
                AddCilInstruction(ilVar, OpCodes.Conv_U);
                AddCilInstruction(ilVar, OpCodes.Localloc);
            }
            else
            {
                countNode.Accept(this);
                AddCilInstruction(ilVar, OpCodes.Conv_U);
                AddCilInstruction(ilVar, OpCodes.Sizeof, ResolveType(type.ElementType));
                AddCilInstruction(ilVar, OpCodes.Mul_Ovf_Un);
                AddCilInstruction(ilVar, OpCodes.Localloc);
            }
        }

        public override void VisitArrayCreationExpression(ArrayCreationExpressionSyntax node)
        {
            if (node.Initializer == null)
            {
                node.Type.RankSpecifiers[0].Accept(this);
            }
            else
            {
                AddCilInstruction(ilVar, OpCodes.Ldc_I4, node.Initializer.Expressions.Count);
            }

            var elementTypeInfo = Context.GetTypeInfo(node.Type.ElementType);
            ProcessArrayCreation(elementTypeInfo.Type, node.Initializer);
        }

        public override void VisitImplicitArrayCreationExpression(ImplicitArrayCreationExpressionSyntax node)
        {
            var arrayType = Context.GetTypeInfo(node);
            if (arrayType.Type == null)
            {
                throw new Exception($"Unable to infer array type: {node}");
            }

            AddCilInstruction(ilVar, OpCodes.Ldc_I4, node.Initializer.Expressions.Count);

            var arrayTypeSymbol = (IArrayTypeSymbol) arrayType.Type;
            ProcessArrayCreation(arrayTypeSymbol.ElementType, node.Initializer);
        }

        public override void VisitElementAccessExpression(ElementAccessExpressionSyntax node)
        {
            var expressionInfo = Context.SemanticModel.GetSymbolInfo(node.Expression);
            if (expressionInfo.Symbol == null)
                return;

            node.Expression.Accept(this);
            node.ArgumentList.Accept(this);

            var targetType = expressionInfo.Symbol.Accept(new ElementTypeSymbolResolver());
            if (!node.Parent.IsKind(SyntaxKind.RefExpression) && _opCodesForLdElem.TryGetValue(targetType.SpecialType, out var opCode))
            {
                AddCilInstruction(ilVar, opCode);
            }
            else
            {
                var indexer = targetType.GetMembers().OfType<IPropertySymbol>().FirstOrDefault(p => p.IsIndexer && p.Parameters.Length == node.ArgumentList.Arguments.Count);
                if (indexer != null)
                {
                    EnsurePropertyExists(node, indexer);
                    AddMethodCall(ilVar, indexer.GetMethod);
                }
                else
                {
                    if (node.Parent.IsKind(SyntaxKind.RefExpression))
                        AddCilInstruction(ilVar,  OpCodes.Ldelema, targetType);
                    else
                        AddCilInstruction(ilVar, OpCodes.Ldelem_Ref);
                }
            }
        }
        
        public override void VisitEqualsValueClause(EqualsValueClauseSyntax node)
        {
            base.VisitEqualsValueClause(node);
            InjectRequiredConversions(node.Value);
        }

        public override void VisitAssignmentExpression(AssignmentExpressionSyntax node)
        {
            var leftNodeMae = node.Left as MemberAccessExpressionSyntax;
            CSharpSyntaxNode exp = leftNodeMae?.Name ?? node.Left;
            // check if the left hand side of the assignment is a property (but not indexers) and handle that as a method (set) call.
            var expSymbol = Context.SemanticModel.GetSymbolInfo(exp).Symbol;
            if (expSymbol is IPropertySymbol propertySymbol && !propertySymbol.IsIndexer)
            {
                HandleMethodInvocation(node.Left, node.Right);
                return;
            }

            var visitor = new AssignmentVisitor(Context, ilVar, node);
            
            visitor.InstructionPrecedingValueToLoad = Context.CurrentLine;
            Visit(node.Right);
            if (!valueTypeNoArgObjCreation)
            {
                visitor.Visit(node.Left);
            }
        }

        public override void VisitBinaryExpression(BinaryExpressionSyntax node)
        {
            Visit(node.Left);
            InjectRequiredConversions(node.Left);

            Visit(node.Right);
            InjectRequiredConversions(node.Right);

            var handler = OperatorHandlerFor(node.OperatorToken);
            handler(
                Context,
                ilVar,
                Context.SemanticModel.GetTypeInfo(node.Left).Type,
                Context.SemanticModel.GetTypeInfo(node.Right).Type);
        }

        public override void VisitLiteralExpression(LiteralExpressionSyntax node)
        {
            switch (node.Kind())
            {
                case SyntaxKind.NullLiteralExpression:
                    var nodeType = Context.SemanticModel.GetTypeInfo(node);
                    if (nodeType.ConvertedType?.TypeKind == TypeKind.Pointer)
                    {
                        AddCilInstruction(ilVar, OpCodes.Ldc_I4_0);
                        AddCilInstruction(ilVar, OpCodes.Conv_U);
                    }
                    else
                        AddCilInstruction(ilVar, OpCodes.Ldnull);
                    break;

                case SyntaxKind.StringLiteralExpression:
                    AddCilInstruction(ilVar, OpCodes.Ldstr, node.ToFullString());
                    break;

                case SyntaxKind.CharacterLiteralExpression:
                case SyntaxKind.NumericLiteralExpression:
                    AddLocalVariableAndHandleCallOnValueTypeLiterals(node, GetSpecialType(SpecialType.System_Int32), node.ToString());
                    break;

                case SyntaxKind.TrueLiteralExpression:
                case SyntaxKind.FalseLiteralExpression:
                    AddLocalVariableAndHandleCallOnValueTypeLiterals(node, GetSpecialType(SpecialType.System_Boolean), bool.Parse(node.ToString()) ? 1 : 0);
                    break;

                default:
                    throw new ArgumentException($"Literal ( {node}) of type {node.Kind()} not supported yet.");
            }

            void AddLocalVariableAndHandleCallOnValueTypeLiterals(LiteralExpressionSyntax literalNode, ITypeSymbol expressionType, object literalValue)
            {
                AddCilInstruction(ilVar, LoadOpCodeFor(literalNode), literalValue);
                var localVarParent = (CSharpSyntaxNode) literalNode.Parent;
                Debug.Assert(localVarParent != null);
                
                if (localVarParent.Accept(new UsageVisitor()) == UsageKind.CallTarget) 
                    StoreTopOfStackInLocalVariableAndLoadItsAddress(expressionType);
            }
        }

        public override void VisitDeclarationExpression(DeclarationExpressionSyntax node)
        {
            if (node.Parent is ArgumentSyntax argument && argument.RefKindKeyword.Kind() == SyntaxKind.OutKeyword)
            {
                var localSymbol = (ILocalSymbol) Context.SemanticModel.GetSymbolInfo(node).Symbol;
                var designation = ((SingleVariableDesignationSyntax) node.Designation);
                var resolvedOutArgType = Context.TypeResolver.Resolve(localSymbol.Type);
                
                var outLocalName = AddLocalVariableWithResolvedType(
                    designation.Identifier.Text,
                    Context.DefinitionVariables.GetLastOf(MemberKind.Method),
                    resolvedOutArgType
                );

                AddCilInstruction(ilVar, OpCodes.Ldloca_S, outLocalName);
            }
            
            base.VisitDeclarationExpression(node);
        }

        public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            HandleMethodInvocation(node.Expression, node.ArgumentList);
            StoreTopOfStackInLocalVariableAndLoadItsAddressIfNeeded(node);
        }
        
        public override void VisitConditionalExpression(ConditionalExpressionSyntax node)
        {
            string Emit(OpCode op)
            {
                var instVarName = Context.Naming.Label(op.Name);
                AddCecilExpression(@"var {0} = {1}.Create({2});", instVarName, ilVar, op.ConstantName());
                
                return instVarName;
            }

            var conditionEnd = Emit(OpCodes.Nop);
            var whenFalse = Emit(OpCodes.Nop);

            Visit(node.Condition);
            AddCilInstruction(ilVar, OpCodes.Brfalse_S, whenFalse);

            Visit(node.WhenTrue);
            AddCilInstruction(ilVar, OpCodes.Br_S, conditionEnd);

            AddCecilExpression("{0}.Append({1});", ilVar, whenFalse);
            Visit(node.WhenFalse);

            AddCecilExpression("{0}.Append({1});", ilVar, conditionEnd);
        }
        
        public override void VisitIdentifierName(IdentifierNameSyntax node)
        {
            HandleIdentifier(node);
        }

        public override void VisitGenericName(GenericNameSyntax node)
        {
            HandleIdentifier(node);
        }

        public override void VisitArgument(ArgumentSyntax node)
        {
            // in the case the parent of the argument syntax is an array access
            // *CurrentLine* will represent the instruction that loaded the array
            // reference into the stack.
            //
            // If the argument is a System.Index the actual offset to be used
            // need to be calculated based on the length of the array (due to 
            // Index supporting the concept of *from the end*)
            //
            // In this case InjectRequiredConversions will call the Action
            // passed and we can add the same instruction to load 
            // the array again (the array reference is necessary to 
            // compute it's length)
            var last = Context.CurrentLine;
            
            base.VisitArgument(node);
            InjectRequiredConversions(node.Expression, () =>
            {
                AddCecilExpression(last.Value);
            });
        }

        public override void VisitPrefixUnaryExpression(PrefixUnaryExpressionSyntax node)
        {
            if (node.OperatorToken.Kind() == SyntaxKind.AmpersandToken)
            {
                Visit(node.Operand);
            }
            else if (node.IsKind(SyntaxKind.UnaryMinusExpression))
            {
                Visit(node.Operand);
                InjectRequiredConversions(node.Operand);
                AddCilInstruction(ilVar, OpCodes.Neg);
            }
            else if (node.IsKind(SyntaxKind.PreDecrementExpression))
            {
                ProcessPrefixPostfixOperators(node.Operand, OpCodes.Sub, true);
            }
            else if (node.IsKind(SyntaxKind.PreIncrementExpression))
            {
                ProcessPrefixPostfixOperators(node.Operand, OpCodes.Add, true);
            }
            else if (node.IsKind(SyntaxKind.LogicalNotExpression))
            {
                node.Operand.Accept(this);
                AddCilInstruction(ilVar, OpCodes.Ldc_I4_0);
                AddCilInstruction(ilVar, OpCodes.Ceq);
            }
            else if (node.IsKind(SyntaxKind.BitwiseNotExpression))
            {
                node.Operand.Accept(this);
                AddCilInstruction(ilVar, OpCodes.Not);
            }
            else if (node.IsKind(SyntaxKind.IndexExpression))
            {
                Console.WriteLine();
            }
            else
            {
                LogUnsupportedSyntax(node);
            }
        }

        public override void VisitPostfixUnaryExpression(PostfixUnaryExpressionSyntax node)
        {
            if (node.IsKind(SyntaxKind.PostDecrementExpression))
            {
                ProcessPrefixPostfixOperators(node.Operand, OpCodes.Sub, false);
            }
            else if (node.IsKind(SyntaxKind.PostIncrementExpression))
            {
                ProcessPrefixPostfixOperators(node.Operand, OpCodes.Add, false);
            }
            else
            {
                LogUnsupportedSyntax(node);
            }
        }

        public override void VisitParenthesizedExpression(ParenthesizedExpressionSyntax node)
        {
            base.VisitParenthesizedExpression(node);

            var localVarParent = (CSharpSyntaxNode) node.Parent;
            if (localVarParent.Accept(new UsageVisitor()) != UsageKind.CallTarget)
                return;

            StoreTopOfStackInLocalVariableAndLoadItsAddress(GetSpecialType(SpecialType.System_Int32));
        }

        public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
        {
            //TODO: Refactor to reuse code from VisitIdentifierName....
            var ctorInfo = Context.SemanticModel.GetSymbolInfo(node);

            var ctor = (IMethodSymbol) ctorInfo.Symbol;
            if (TryProcessNoArgsCtorInvocationOnValueType(node, ctor, ctorInfo))
            {
                return;
            }

            EnsureMethodAvailable(Context.Naming.SyntheticVariable(node.Type.ToString(), ElementKind.LocalVariable), ctor, Array.Empty<TypeParameterSyntax>());

            AddCilInstruction(ilVar, OpCodes.Newobj, ctor.MethodResolverExpression(Context));
            PushCall();

            Visit(node.ArgumentList);
            FixCallSite();
            
            StoreTopOfStackInLocalVariableAndLoadItsAddressIfNeeded(node);
        }

        private void ProcessPrefixPostfixOperators(ExpressionSyntax operand, OpCode opCode, bool isPrefix)
        {
            Visit(operand);
            InjectRequiredConversions(operand);

            var assignmentVisitor = new AssignmentVisitor(Context, ilVar);

            var operandInfo = Context.SemanticModel.GetSymbolInfo(operand);
            if (operandInfo.Symbol != null && operandInfo.Symbol.Kind != SymbolKind.Field && operandInfo.Symbol.Kind != SymbolKind.Property) // Fields / Properties requires more complex handling to load the owning reference.
            {
                if (!isPrefix) // For *postfix* operators we duplicate the value *before* applying the operator...
                {
                    AddCilInstruction(ilVar, OpCodes.Dup);
                }

                AddCilInstruction(ilVar, OpCodes.Ldc_I4_1);
                AddCilInstruction(ilVar, opCode);

                if (isPrefix) // For prefix operators we duplicate the value *after* applying the operator...
                {
                    AddCilInstruction(ilVar, OpCodes.Dup);
                }
                
                //assign (top of stack to the operand)
                assignmentVisitor.InstructionPrecedingValueToLoad = Context.CurrentLine;
                operand.Accept(assignmentVisitor);
                return;
            }

            if (isPrefix)
            {
                AddCilInstruction(ilVar, OpCodes.Ldc_I4_1);
                AddCilInstruction(ilVar, opCode);
            }

            var tempLocalName = AddLocalVariableWithResolvedType("tmp", Context.DefinitionVariables.GetLastOf(MemberKind.Method), Context.TypeResolver.Resolve(Context.SemanticModel.GetTypeInfo(operand).Type));
            AddCilInstruction(ilVar, OpCodes.Stloc, tempLocalName);
            AddCilInstruction(ilVar, OpCodes.Ldloc, tempLocalName);
            assignmentVisitor.InstructionPrecedingValueToLoad = Context.CurrentLine;
            AddCilInstruction(ilVar, OpCodes.Ldloc, tempLocalName);
            
            if (!isPrefix)
            {
                AddCilInstruction(ilVar, OpCodes.Ldc_I4_1);
                AddCilInstruction(ilVar, opCode);
            }
            
            // assign (top of stack to the operand)
            operand.Accept(assignmentVisitor);
        }

        private bool TryProcessNoArgsCtorInvocationOnValueType(ObjectCreationExpressionSyntax node, IMethodSymbol methodSymbol, SymbolInfo ctorInfo)
        {
            if (ctorInfo.Symbol.ContainingType.IsReferenceType || methodSymbol.Parameters.Length > 0)
            {
                return false;
            }

            new ValueTypeNoArgCtorInvocationVisitor(Context, ilVar, ctorInfo).Visit(node.Parent);
            return valueTypeNoArgObjCreation = true;
        }

        public override void VisitExpressionStatement(ExpressionStatementSyntax node)
        {
            base.Visit(node.Expression);

            var info = Context.GetTypeInfo(node.Expression);
            if (node.Expression.Kind() != SyntaxKind.SimpleAssignmentExpression && info.Type.SpecialType != SpecialType.System_Void)
            {
                AddCilInstruction(ilVar, OpCodes.Pop);
            }
        }

        public override void VisitThisExpression(ThisExpressionSyntax node)
        {
            AddCilInstruction(ilVar, OpCodes.Ldarg_0);
            base.VisitThisExpression(node);
        }

        public override void VisitRefExpression(RefExpressionSyntax node)
        {
            using (Context.WithFlag("ref-return"))
            {
                node.Expression.Accept(this);
            }
        }

        public override void VisitCastExpression(CastExpressionSyntax node)
        {
            node.Expression.Accept(this);
            var castSource = Context.GetTypeInfo(node.Expression);
            var castTarget = Context.GetTypeInfo(node.Type);
            
            if (castSource.Type == null)
                throw new InvalidOperationException($"Failed to get type information for expression: {node.Expression}");
            
            if (castTarget.Type == null)
                throw new InvalidOperationException($"Failed to get type information for: {node.Type}");

            if (castSource.Type.SpecialType == castTarget.Type.SpecialType && castSource.Type.SpecialType == SpecialType.System_Double)
            {
                /*
                 * Even though a cast from double => double can be view as an identity conversion (from the pov of the developer who wrote it)
                 * we still need to emit a *conv.r8* opcode. * (Fo more details see https://github.com/dotnet/roslyn/discussions/56198)
                 */
                AddCilInstruction(ilVar, OpCodes.Conv_R8);
                return;
            }
            
            if (castSource.Type.SpecialType == castTarget.Type.SpecialType && castSource.Type.SpecialType != SpecialType.None ||
                castSource.Type.SpecialType == SpecialType.System_Byte && castTarget.Type.SpecialType == SpecialType.System_Char ||
                castSource.Type.SpecialType == SpecialType.System_Byte && castTarget.Type.SpecialType == SpecialType.System_Int16 ||
                castSource.Type.SpecialType == SpecialType.System_Byte && castTarget.Type.SpecialType == SpecialType.System_Int32 ||
                castSource.Type.SpecialType == SpecialType.System_Char && castTarget.Type.SpecialType == SpecialType.System_Int32 ||
                castSource.Type.SpecialType == SpecialType.System_Int16 && castTarget.Type.SpecialType == SpecialType.System_Int32)
                return;
            
            var conversion = Context.SemanticModel.ClassifyConversion(node.Expression, castTarget.Type, true);
            if (castTarget.Type.SpecialType != SpecialType.None && conversion.IsNumeric)
            {
                var opcode = castTarget.Type.SpecialType switch
                {
                    SpecialType.System_Int16 => OpCodes.Conv_I2,
                    SpecialType.System_Int32 => OpCodes.Conv_I4,
                    SpecialType.System_Int64 => castSource.Type.SpecialType == SpecialType.System_Byte || castSource.Type.SpecialType == SpecialType.System_Char ? OpCodes.Conv_U8 : OpCodes.Conv_I8,
            
                    SpecialType.System_Single => OpCodes.Conv_R4,
                    SpecialType.System_Double => OpCodes.Conv_R8,
                    SpecialType.System_Char => OpCodes.Conv_U2,
                    SpecialType.System_Byte => OpCodes.Conv_U1,
            
                    _ => throw new Exception($"Cast from {node.Expression} ({castSource.Type}) to {castTarget.Type} is not supported.")
                };
            
                AddCilInstruction(ilVar, opcode);
            }
            else if (conversion.IsExplicit && conversion.IsReference)
            {
                var opcode = castTarget.Type.TypeKind == TypeKind.TypeParameter ? OpCodes.Unbox_Any : OpCodes.Castclass;
                AddCilInstruction(ilVar, opcode, castTarget.Type);
            }
            else if (conversion.IsImplicit && conversion.IsReference && castSource.Type.TypeKind == TypeKind.TypeParameter)
            {
                AddCilInstruction(ilVar, OpCodes.Box, Context.TypeResolver.Resolve(castSource.Type));
            }
        }
        
        public override void VisitRangeExpression(RangeExpressionSyntax node) => LogUnsupportedSyntax(node);
        public override void VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node) => LogUnsupportedSyntax(node);
        public override void VisitAwaitExpression(AwaitExpressionSyntax node) => LogUnsupportedSyntax(node);
        public override void VisitTupleExpression(TupleExpressionSyntax node) => LogUnsupportedSyntax(node);
        public override void VisitInterpolatedStringExpression(InterpolatedStringExpressionSyntax node) => LogUnsupportedSyntax(node);
        public override void VisitIsPatternExpression(IsPatternExpressionSyntax node) => LogUnsupportedSyntax(node);
        public override void VisitThrowExpression(ThrowExpressionSyntax node) => LogUnsupportedSyntax(node);
        public override void VisitSwitchExpression(SwitchExpressionSyntax node) => LogUnsupportedSyntax(node);
        public override void VisitAnonymousObjectCreationExpression(AnonymousObjectCreationExpressionSyntax node) => LogUnsupportedSyntax(node);
        public override void VisitOmittedArraySizeExpression(OmittedArraySizeExpressionSyntax node) => LogUnsupportedSyntax(node);
        public override void VisitImplicitStackAllocArrayCreationExpression(ImplicitStackAllocArrayCreationExpressionSyntax node) => LogUnsupportedSyntax(node);
        public override void VisitMakeRefExpression(MakeRefExpressionSyntax node) => LogUnsupportedSyntax(node);
        public override void VisitRefTypeExpression(RefTypeExpressionSyntax node) => LogUnsupportedSyntax(node);
        public override void VisitRefValueExpression(RefValueExpressionSyntax node) => LogUnsupportedSyntax(node);
        public override void VisitCheckedExpression(CheckedExpressionSyntax node) => LogUnsupportedSyntax(node);
        public override void VisitDefaultExpression(DefaultExpressionSyntax node) => LogUnsupportedSyntax(node);
        public override void VisitTypeOfExpression(TypeOfExpressionSyntax node) => LogUnsupportedSyntax(node);
        public override void VisitSizeOfExpression(SizeOfExpressionSyntax node) => LogUnsupportedSyntax(node);
        public override void VisitInitializerExpression(InitializerExpressionSyntax node) => LogUnsupportedSyntax(node);

        private void StoreTopOfStackInLocalVariableAndLoadItsAddressIfNeeded(ExpressionSyntax node)
        {
            var invocation = (InvocationExpressionSyntax) node.Ancestors().FirstOrDefault(a => a.IsKind(SyntaxKind.InvocationExpression));
            if (invocation == null || invocation.ArgumentList.Arguments.Any(argumentExp => argumentExp.Expression.DescendantNodesAndSelf().Any( exp => exp == node)))
                return;

            var targetOfInvocationType = Context.SemanticModel.GetTypeInfo(node);
            if (targetOfInvocationType.Type?.IsValueType == false)
                return;

            StoreTopOfStackInLocalVariableAndLoadItsAddress(targetOfInvocationType.Type);
        }

        private void StoreTopOfStackInLocalVariableAndLoadItsAddress(ITypeSymbol type)
        {
            var tempLocalName = AddLocalVariableWithResolvedType("tmp", Context.DefinitionVariables.GetLastOf(MemberKind.Method), Context.TypeResolver.Resolve(type));
            AddCilInstruction(ilVar, OpCodes.Stloc, tempLocalName);
            AddCilInstruction(ilVar, OpCodes.Ldloca_S, tempLocalName);
        }
        
        private OpCode LoadOpCodeFor(LiteralExpressionSyntax node)
        {
            var info = Context.SemanticModel.GetTypeInfo(node);
            switch (info.Type.SpecialType)
            {
                case SpecialType.System_Single:
                    return OpCodes.Ldc_R4;

                case SpecialType.System_Double:
                    return OpCodes.Ldc_R8;

                case SpecialType.System_Int16:
                case SpecialType.System_Int32:
                    return OpCodes.Ldc_I4;

                case SpecialType.System_Int64:
                    return OpCodes.Ldc_I8;

                case SpecialType.System_Char:
                    return OpCodes.Ldc_I4;

                case SpecialType.System_Boolean:
                    return OpCodes.Ldc_I4;
            }

            throw new ArgumentException($"Literal type {info.Type} not supported.", nameof(node));
        }
        
        private OpCode StelemOpCodeFor(ITypeSymbol type)
        {
            switch (type.SpecialType)
            {
                case SpecialType.System_Byte: return OpCodes.Stelem_I1;
                case SpecialType.System_Int16: return OpCodes.Stelem_I2;
                case SpecialType.System_Int32: return OpCodes.Stelem_I4;
                case SpecialType.System_Int64: return OpCodes.Stelem_I8;
                case SpecialType.System_Single: return OpCodes.Stelem_R4;
                case SpecialType.System_Double: return OpCodes.Stelem_R8;

                case SpecialType.None: // custom types.
                case SpecialType.System_String:
                case SpecialType.System_Object: return OpCodes.Stelem_Ref;
            }

            throw new Exception($"Element type {type.Name} not supported.");
        }

        /*
         * Support for scenario in which a method is being referenced before it has been declared. This can happen for instance in code like:
         *
         * class C
         * {
         *     void Foo() { Bar(); }
         *     void Bar() {}
         * }
         *
         * In this case when the first reference to Bar() is found (in method Foo()) the method itself has not been defined yet.
         */
        private void EnsureMethodAvailable(string varName, IMethodSymbol method, TypeParameterSyntax[] typeParameters)
        {
            if (!method.IsDefinedInCurrentType(Context))
                return;

            var found = Context.DefinitionVariables.GetMethodVariable(method.AsMethodDefinitionVariable());
            if (found.IsValid)
                return;

            MethodDeclarationVisitor.AddMethodDefinition(Context, varName, method.Name, "MethodAttributes.Private", method.ReturnType, method.ReturnsByRef, typeParameters);
            Context.DefinitionVariables.RegisterMethod(method.ContainingType.Name, method.Name, method.Parameters.Select(p => p.Type.Name).ToArray(), varName);
        }

        private void FixCallSite()
        {
            Context.MoveLineAfter(callFixList.Pop(), Context.CurrentLine);
        }

        private void PushCall()
        {
            callFixList.Push(Context.CurrentLine);
        }

        private void ProcessProperty(SimpleNameSyntax node, IPropertySymbol propertySymbol)
        {
            EnsurePropertyExists(node, propertySymbol);
            
            var parentMae = node.Parent as MemberAccessExpressionSyntax;
            var isAccessOnThisOrObjectCreation = true;
            if (parentMae != null)
            {
                isAccessOnThisOrObjectCreation = parentMae.Expression.IsKind(SyntaxKind.ObjectCreationExpression);
            }

            // if this is an *unqualified* access we need to load *this*
            if (parentMae == null || parentMae.Expression == node)
            {
                AddCilInstruction(ilVar, OpCodes.Ldarg_0);
            }

            var parentExp = node.Parent;
            if (parentExp.Kind() == SyntaxKind.SimpleAssignmentExpression || parentMae != null && parentMae.Name.Identifier == node.Identifier && parentMae.Parent.IsKind(SyntaxKind.SimpleAssignmentExpression))
            {
                AddMethodCall(ilVar, propertySymbol.SetMethod, isAccessOnThisOrObjectCreation);
            }
            else
            {
                if (propertySymbol.ContainingType.SpecialType == SpecialType.System_Array && propertySymbol.Name == "Length")
                {
                    AddCilInstruction(ilVar, OpCodes.Ldlen);
                    AddCilInstruction(ilVar, OpCodes.Conv_I4);
                }
                else if (propertySymbol.ContainingType.SpecialType == SpecialType.System_Array && propertySymbol.Name == "LongLength")
                {
                    AddCilInstruction(ilVar, OpCodes.Ldlen);
                    AddCilInstruction(ilVar, OpCodes.Conv_I8);
                }
                else
                {
                    AddMethodCall(ilVar, propertySymbol.GetMethod, isAccessOnThisOrObjectCreation);
                    StoreTopOfStackInLocalVariableAndLoadItsAddressIfNeeded(node);
                }
            }
        }
        
        private void EnsurePropertyExists(SyntaxNode node, [NotNull] IPropertySymbol propertySymbol)
        {
            var declaringReference = propertySymbol.DeclaringSyntaxReferences.SingleOrDefault();
            if (declaringReference == null)
                return;
            
            var propertyDeclaration = (BasePropertyDeclarationSyntax) declaringReference.GetSyntax();
            if (propertyDeclaration.Span.Start > node.Span.End)
            {
                // this is a forward reference, process it...
                propertyDeclaration.Accept(new PropertyDeclarationVisitor(Context));
            }
        }

        private void ProcessField(SimpleNameSyntax node, IFieldSymbol fieldSymbol)
        {
            var fieldDeclarationVariable = EnsureFieldExists(node, fieldSymbol);
            
            if (!fieldSymbol.IsStatic)
                AddCilInstruction(ilVar, OpCodes.Ldarg_0);
            
            if (HandleLoadAddress(ilVar, fieldSymbol.Type, (CSharpSyntaxNode) node.Parent, OpCodes.Ldflda, fieldSymbol.Name, MemberKind.Field, fieldSymbol.ContainingType.Name))
            {
                return;
            }

            if (fieldSymbol.IsVolatile)
                AddCilInstruction(ilVar, OpCodes.Volatile);

            AddCilInstruction(ilVar, LoadOpCodeForFieldAccess(fieldSymbol), fieldDeclarationVariable.VariableName);
            HandlePotentialDelegateInvocationOn(node, fieldSymbol.Type, ilVar);
        }

        private OpCode LoadOpCodeForFieldAccess(IFieldSymbol fieldSymbol) => fieldSymbol.IsStatic ? OpCodes.Ldsfld : OpCodes.Ldfld;

        private DefinitionVariable EnsureFieldExists(SimpleNameSyntax node, [NotNull] IFieldSymbol fieldSymbol)
        {
            var fieldDeclaration = (FieldDeclarationSyntax) fieldSymbol.DeclaringSyntaxReferences.Single().GetSyntax().Parent.Parent;
            if (fieldDeclaration.Span.Start > node.Span.End)
            {
                // this is a forward reference, process it...
                fieldDeclaration.Accept(new FieldDeclarationVisitor(Context));
            }
            
            var fieldDeclarationVariable = Context.DefinitionVariables.GetVariable(fieldSymbol.Name, MemberKind.Field, fieldSymbol.ContainingType.Name);
            if (!fieldDeclarationVariable.IsValid)
                throw new Exception($"Could not resolve reference to field: {fieldSymbol.Name}");
            
            return fieldDeclarationVariable;
        }
        
        private void ProcessLocalVariable(SimpleNameSyntax localVarSyntax, SymbolInfo varInfo)
        {
            var symbol = (ILocalSymbol) varInfo.Symbol;
            var localVar = (CSharpSyntaxNode) localVarSyntax.Parent;
            if (HandleLoadAddress(ilVar, symbol.Type, localVar, OpCodes.Ldloca, symbol.Name, MemberKind.LocalVariable))
            {
                return;
            }

            AddCilInstruction(ilVar, OpCodes.Ldloc, Context.DefinitionVariables.GetVariable(symbol.Name, MemberKind.LocalVariable).VariableName);
            HandlePotentialDelegateInvocationOn(localVarSyntax, symbol.Type, ilVar);
            HandlePotentialFixedLoad(symbol);
            HandlePotentialRefLoad(localVarSyntax, symbol);
        }

        private void HandlePotentialRefLoad(SimpleNameSyntax localVariableNameSyntax, ILocalSymbol symbol)
        {
            var needsLoadIndirect = false;
            
            var sourceSymbol = Context.SemanticModel.GetSymbolInfo(localVariableNameSyntax).Symbol;
            var sourceIsByRef = sourceSymbol.IsByRef();

            var returnStatement = localVariableNameSyntax.Ancestors().OfType<ReturnStatementSyntax>().SingleOrDefault();
            var argument = localVariableNameSyntax.Ancestors().OfType<ArgumentSyntax>().SingleOrDefault();
            var assigment = localVariableNameSyntax.Ancestors().OfType<BinaryExpressionSyntax>().SingleOrDefault(candidate => candidate.IsKind(SyntaxKind.SimpleAssignmentExpression));

            if (assigment != null && assigment.Left != localVariableNameSyntax)
            {
                var targetIsByRef = Context.SemanticModel.GetSymbolInfo(assigment.Left).Symbol.IsByRef();
                needsLoadIndirect = (assigment.Right == localVariableNameSyntax && sourceIsByRef && !targetIsByRef) // simple assignment like: nonRef = ref;
                                    || sourceIsByRef; // complex assignment like: nonRef = ref + 10;
            }
            else if (argument != null)
            {
                var parameterSymbol = ParameterSymbolFromArgumentSyntax(argument);
                var targetIsByRef = parameterSymbol.IsByRef();

                needsLoadIndirect = (parameterSymbol == sourceSymbol && sourceIsByRef && !targetIsByRef) // simple argument like Foo(ref) where the parameter is not a reference. 
                                    || sourceIsByRef; // complex argument like Foo(ref + 1)
            }
            else if (returnStatement != null)
            {
                var method = returnStatement.Ancestors().OfType<MethodDeclarationSyntax>().Single();
                var methodIsByRef = Context.SemanticModel.GetSymbolInfo(method.ReturnType).Symbol.IsByRef();
                
                needsLoadIndirect = (returnStatement.Expression == localVariableNameSyntax && !methodIsByRef && sourceIsByRef) // simple return like: return ref_var; (method is not by ref)
                                    || sourceIsByRef; // more complex return like: return ref_var + 1; // in this case we can only be accessing the value whence we need to deference the reference.
            }
            
            if (needsLoadIndirect)
                AddCilInstruction(ilVar, LoadIndirectOpCodeFor(symbol.Type.SpecialType));
        }

        private void HandlePotentialFixedLoad(ILocalSymbol symbol)
        {
            if (!symbol.IsFixed)
                return;

            AddCilInstruction(ilVar, OpCodes.Conv_U);
        }

        private void ProcessMethodReference(SimpleNameSyntax node, IMethodSymbol method)
        {
            var invocationParent = node.Ancestors().OfType<InvocationExpressionSyntax>()
                .SingleOrDefault(i => i.Expression == node || i.Expression.ChildNodes().Contains(node));
            
            if (invocationParent != null)
            {
                ProcessMethodCall(node, method);
                return;
            }

            // this is not an invocation. We need to figure out whether this is an assignment, return, etc
            var firstParentNotPartOfName = node.Ancestors().First(a => a.Kind() != SyntaxKind.QualifiedName 
                                                                       && a.Kind() != SyntaxKind.SimpleMemberAccessExpression
                                                                       && a.Kind() != SyntaxKind.EqualsValueClause
                                                                       && a.Kind() != SyntaxKind.VariableDeclarator);

            if (firstParentNotPartOfName is PrefixUnaryExpressionSyntax unaryPrefix && unaryPrefix.IsKind(SyntaxKind.AddressOfExpression))
            {
                AddCilInstruction(ilVar, OpCodes.Ldftn, method.MethodResolverExpression(Context));
                return;
            }
                
            var delegateType = firstParentNotPartOfName switch
            {
                ArgumentSyntax arg => ((IMethodSymbol) Context.SemanticModel.GetSymbolInfo(arg.Parent.Parent).Symbol).Parameters[arg.FirstAncestorOrSelf<ArgumentListSyntax>().Arguments.IndexOf(arg)].Type,
                    
                AssignmentExpressionSyntax assignment => Context.SemanticModel.GetSymbolInfo(assignment.Left).Symbol switch
                {
                    ILocalSymbol local => local.Type,
                    IParameterSymbol param => param.Type,
                    IFieldSymbol field => field.Type,
                    IPropertySymbol prop => prop.Type,
                    _ => throw new NotSupportedException($"Assignment to {assignment.Left} ({assignment.Kind()}) is not supported.")
                },
                    
                VariableDeclarationSyntax variableDeclaration => Context.SemanticModel.GetTypeInfo(variableDeclaration.Type).Type,
                    
                ReturnStatementSyntax returnStatement => returnStatement.FirstAncestorOrSelf<MemberDeclarationSyntax>() switch
                {
                    MethodDeclarationSyntax md => Context.SemanticModel.GetTypeInfo(md.ReturnType).Type,
                    _ => throw new NotSupportedException($"Return is not supported.")
                },
                        
                _ => throw new NotSupportedException($"Referencing method {method} in expression {firstParentNotPartOfName} ({firstParentNotPartOfName.GetType().FullName}) is not supported.")
            };
                
            // we have a reference to a method used to initialize a delegate
            // and need to load the referenced method token and instantiate the delegate. For instance:
            //IL_0002: ldarg.0
            //IL_0002: ldftn string Test::M(int32)
            //IL_0008: newobj instance void class [System.Private.CoreLib]System.Func`2<int32, string>::.ctor(object, native int)

            if (method.IsStatic)
            {
                AddCilInstruction(ilVar, OpCodes.Ldnull);
            }
            else if (!node.Parent.IsKind(SyntaxKind.ThisExpression) && node.Parent == firstParentNotPartOfName)
            {
                AddCilInstruction(ilVar, OpCodes.Ldarg_0);
            }

            AddCilInstruction(ilVar, OpCodes.Ldftn, method.MethodResolverExpression(Context));

            var delegateCtor = delegateType.GetMembers().OfType<IMethodSymbol>().FirstOrDefault(m => m.Name == ".ctor"); 
            AddCilInstruction(ilVar, OpCodes.Newobj, delegateCtor.MethodResolverExpression(Context));
        }
        
        private void ProcessMethodCall(SimpleNameSyntax node, IMethodSymbol method)
        {
            if (!method.IsStatic && method.IsDefinedInCurrentType(Context) && node.Parent.Kind() == SyntaxKind.InvocationExpression)
            {
                AddCilInstruction(ilVar, OpCodes.Ldarg_0);
            }

            //TODO: We need to find the InvocationSyntax that node represents...
            EnsureMethodAvailable(Context.Naming.SyntheticVariable(node.Identifier.Text, ElementKind.Method), method.OverriddenMethod ?? method.OriginalDefinition, Array.Empty<TypeParameterSyntax>());
            var isAccessOnThis = !node.Parent.IsKind(SyntaxKind.SimpleMemberAccessExpression);

            var mae = node.Parent as MemberAccessExpressionSyntax;
            if (mae?.Expression.IsKind(SyntaxKind.ObjectCreationExpression) == true)
            {
                isAccessOnThis = true;
            }

            AddMethodCall(ilVar, method, isAccessOnThis);
        }

        private void InjectRequiredConversions(ExpressionSyntax expression, Action loadArrayIntoStack = null)
        {
            var typeInfo = Context.SemanticModel.GetTypeInfo(expression);
            if (typeInfo.Type == null)
                return;
            
            var conversion = Context.SemanticModel.GetConversion(expression);
            if (conversion.IsImplicit && conversion.IsNumeric)
            {
                Debug.Assert(typeInfo.ConvertedType != null);
                switch (typeInfo.ConvertedType.SpecialType)
                {
                    case SpecialType.System_Single:
                        AddCilInstruction(ilVar, OpCodes.Conv_R4);
                        return;
                    
                    case SpecialType.System_Double:
                        AddCilInstruction(ilVar, OpCodes.Conv_R8);
                        return;

                    case SpecialType.System_Byte:
                        AddCilInstruction(ilVar, OpCodes.Conv_I1);
                        return;
                    
                    case SpecialType.System_Int16:
                        AddCilInstruction(ilVar, OpCodes.Conv_I2);
                        return;
                    
                    case SpecialType.System_Int32:
                        // byte/char are pushed as Int32 by the runtime 
                        if (typeInfo.Type.SpecialType != SpecialType.System_SByte && typeInfo.Type.SpecialType != SpecialType.System_Byte && typeInfo.Type.SpecialType != SpecialType.System_Char)
                            AddCilInstruction(ilVar, OpCodes.Conv_I4);
                        return;
                    
                    case SpecialType.System_Int64:
                        var convOpCode = typeInfo.Type.SpecialType == SpecialType.System_Char || typeInfo.Type.SpecialType == SpecialType.System_Byte ? OpCodes.Conv_U8 : OpCodes.Conv_I8;
                        AddCilInstruction(ilVar, convOpCode);
                        return;

                    default:
                        throw new Exception($"Conversion from {typeInfo.Type} to {typeInfo.ConvertedType}  not implemented.");
                }
            }

            if (conversion.IsImplicit && conversion.IsBoxing)
            {
                AddCilInstruction(ilVar, OpCodes.Box, typeInfo.Type);
            }
            else if (conversion.IsIdentity && typeInfo.Type.Name == "Index" && loadArrayIntoStack != null)
            {
                // We are indexing an array/indexer using System.Index; In this case
                // we need to convert from System.Index to *int* which is done through
                // the method System.Index::GetOffset(int32)
                loadArrayIntoStack();
                AddCilInstruction(ilVar, OpCodes.Ldlen);
                AddCilInstruction(ilVar, OpCodes.Conv_I4);
                AddMethodCall(ilVar, (IMethodSymbol) typeInfo.Type.GetMembers().Single(m => m.Name == "GetOffset"));
            }
        }

        private Action<IVisitorContext, string, ITypeSymbol, ITypeSymbol> OperatorHandlerFor(SyntaxToken operatorToken)
        {
            if (operatorHandlers.ContainsKey(operatorToken.Kind()))
            {
                return operatorHandlers[operatorToken.Kind()];
            }

            throw new Exception(string.Format("Operator {0} not supported yet (expression: {1})", operatorToken.ValueText, operatorToken.Parent));
        }

        /*
	     *            +--> ArgumentList
		 *            |
		 *       /---------\
		 * n.DoIt(10 + x, y);
		 * \----/
		 *	 |
		 *	 +---> Expression: MemberAccessExpression
		 * 
		 * We do not have a natural order to visit the expression and the argument list:
		 * 
		 * - If we visit in the order: arguments, expression (which would be the natural order)
		 *			push 10
		 *			push x
		 *			add
		 *			push y
		 *			push n <-- Should be the first
		 *			Call DoIt(Int32, Int32)
		 * 
		 * - If we visit in the order: expression, arguments
		 *			push n 
		 *			Call DoIt(Int32, Int32) <--+
		 *			push 10                    |
		 *			push x                     |  Should be here
		 *			add                        |
		 *			push y                     |
		 *			         <-----------------+
		 * 
		 * To fix this we visit in the order [exp, args] and move the call operation after visiting the arguments
		 */
        private void HandleMethodInvocation(SyntaxNode target, SyntaxNode args)
        {
            var targetTypeInfo = Context.SemanticModel.GetTypeInfo(target).Type;
            if (targetTypeInfo?.TypeKind == TypeKind.FunctionPointer)
            {
                // if *target* is a function pointer, then we'll emmit a calli, which 
                // expects the stack as : <arg1, arg2, .. argn, function ptr>
                Visit(args);
                Visit(target);
            }
            else
            {
                Visit(target);
                PushCall();

                Visit(args);
                FixCallSite();
            }
        }

        private void HandleIdentifier(SimpleNameSyntax node)
        {
            var member = Context.SemanticModel.GetSymbolInfo(node);
            switch (member.Symbol.Kind)
            {
                case SymbolKind.Method:
                    ProcessMethodReference(node, member.Symbol as IMethodSymbol);
                    break;

                case SymbolKind.Parameter:
                    ProcessParameter(ilVar, node, (IParameterSymbol) member.Symbol);
                    break;

                case SymbolKind.Local:
                    ProcessLocalVariable(node, member);
                    break;

                case SymbolKind.Field:
                    ProcessField(node, member.Symbol as IFieldSymbol);
                    break;

                case SymbolKind.Property:
                    ProcessProperty(node, member.Symbol as IPropertySymbol);
                    break;
            }
        }

        private void ProcessArrayCreation(ITypeSymbol elementType, InitializerExpressionSyntax initializer)
        {
            AddCilInstruction(ilVar, OpCodes.Newarr, Context.TypeResolver.Resolve(elementType));

            var stelemOpCode = StelemOpCodeFor(elementType);
            for (var i = 0; i < initializer?.Expressions.Count; i++)
            {
                AddCilInstruction(ilVar, OpCodes.Dup);
                AddCilInstruction(ilVar, OpCodes.Ldc_I4, i);
                initializer.Expressions[i].Accept(this);

                var itemType = Context.GetTypeInfo(initializer.Expressions[i]);
                if (elementType.IsReferenceType && itemType.Type != null && itemType.Type.IsValueType)
                {
                    AddCilInstruction(ilVar, OpCodes.Box, Context.TypeResolver.Resolve(itemType.Type));
                }

                AddCilInstruction(ilVar, stelemOpCode);
            }
        }
    }

    internal class ElementTypeSymbolResolver : SymbolVisitor<ITypeSymbol>
    {
        public override ITypeSymbol VisitEvent(IEventSymbol symbol)
        {
            return symbol.Type.Accept(this);
        }

        public override ITypeSymbol VisitField(IFieldSymbol symbol)
        {
            return symbol.Type.Accept(this);
        }

        public override ITypeSymbol VisitLocal(ILocalSymbol symbol)
        {
            return symbol.Type.Accept(this);
        }

        public override ITypeSymbol VisitMethod(IMethodSymbol symbol)
        {
            return symbol.ReturnType.Accept(this);
        }

        public override ITypeSymbol VisitProperty(IPropertySymbol symbol)
        {
            return symbol.Type.Accept(this);
        }

        public override ITypeSymbol VisitParameter(IParameterSymbol symbol)
        {
            return symbol.Type.Accept(this);
        }

        public override ITypeSymbol VisitArrayType(IArrayTypeSymbol symbol)
        {
            return symbol.ElementType;
        }

        public override ITypeSymbol VisitNamedType(INamedTypeSymbol symbol)
        {
            return symbol;
        }
    }
}
