using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Cecilifier.Core.Extensions;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;

namespace Cecilifier.Core.Tests.Framework.AssemblyDiff
{
    internal class AssemblyComparer
    {
        private static readonly Dictionary<Code, Func<Instruction, Instruction, bool, (bool, int)>> _instructionValidator =
            new()
            {
                {Code.Ret, OperandObliviousValidator},
                {Code.Nop, OperandObliviousValidator},

                {Code.Ldarg_0, LoadArgumentShortCutValidator},
                {Code.Ldarg_1, LoadArgumentShortCutValidator},
                {Code.Ldarg_2, LoadArgumentShortCutValidator},
                {Code.Ldarg_3, LoadArgumentShortCutValidator},

                {Code.Ldloc_0, LoadLocalShortCutValidator},
                {Code.Ldloc_1, LoadLocalShortCutValidator},
                {Code.Ldloc_2, LoadLocalShortCutValidator},
                {Code.Ldloc_3, LoadLocalShortCutValidator},

                {Code.Stloc_0, StoreLocalShortCutValidator},
                {Code.Stloc_1, StoreLocalShortCutValidator},
                {Code.Stloc_2, StoreLocalShortCutValidator},
                {Code.Stloc_3, StoreLocalShortCutValidator},

                {Code.Call, ValidateCalls},
                {Code.Calli, ValidateCallSite},
                {Code.Callvirt, ValidateCalls},
                {Code.Newobj, ValidateCalls},

                {Code.Ldftn, ValidateCalls},
                {Code.Ldloca, ValidateLocalVariableIndex},
                {Code.Ldloca_S, ValidateLocalVariableIndex},
                {Code.Ldloc, ValidateLocalVariableIndex},
                {Code.Ldloc_S, ValidateLocalVariableIndex},
                {Code.Stloc, ValidateLocalVariableIndex},
                {Code.Stloc_S, ValidateLocalVariableIndex},

                {Code.Ldc_I4_M1, ValidateLoadMinusOne},

                {Code.Castclass, ValidateTypeReference},
                {Code.Box, ValidateTypeReference},
                {Code.Isinst, ValidateTypeReference},
                {Code.Newarr, ValidateTypeReference},

                {Code.Ldflda, ValidateField},
                {Code.Ldfld, ValidateField},
                {Code.Stfld, ValidateField},

                {Code.Stind_Ref, OperandObliviousValidator},
                {Code.Stind_I2, OperandObliviousValidator},
                {Code.Stind_I4, OperandObliviousValidator},
                {Code.Stind_I8, OperandObliviousValidator}
            };

        private readonly AssemblyDefinition first;
        private readonly AssemblyDefinition second;

        public AssemblyComparer(string pathToFirst, string pathToSecond)
        {
            first = AssemblyDefinition.ReadAssembly(pathToFirst);
            second = AssemblyDefinition.ReadAssembly(pathToSecond);
        }

        public string First => first.MainModule.FileName;

        public string Second => second.MainModule.FileName;

        public bool Compare(IAssemblyDiffVisitor visitor, Func<Instruction, Instruction, bool?> instructionComparer)
        {
            if (first.Modules.Count != second.Modules.Count)
            {
                if (!visitor.VisitModules(first, second))
                {
                    return false;
                }
            }

            // We don't handle multi-module assemblies. It seems they are very rare in practice
            var sourceModule = first.MainModule;
            var targetModule = second.MainModule;

            var ret = true;
            var firstTypes = new HashSet<TypeDefinition>(sourceModule.Types);

            foreach (var sourceType in firstTypes.SelectMany(t => t.NestedTypes).Concat(firstTypes).Where(t => t.FullName != "<Module>"))
            {
                var targetType = targetModule.Types.SelectMany(ct => ct.NestedTypes).Concat(targetModule.Types).SingleOrDefault(t => t.FullName == sourceType.FullName);

                var typeVisitor = visitor.VisitType(sourceType);
                if (typeVisitor == null)
                {
                    continue;
                }

                if (targetType == null)
                {
                    if (!typeVisitor.VisitMissing(sourceType, targetModule))
                    {
                        return false;
                    }

                    continue;
                }

                if (sourceType.IsNested != targetType.IsNested)
                {
                    throw new Exception("Types differ: " + sourceType.Name + " / " + targetType.Name + " : " + sourceType.IsNested);
                }

                ret = ret && CheckTypeCustomAttributes(typeVisitor, sourceType, targetType);
                ret = ret && CheckTypeAttributes(typeVisitor, sourceType, targetType);
                ret = ret && CheckTypeGenericInformation(typeVisitor, sourceType, targetType);
                ret = ret && CheckTypeMembers(typeVisitor, sourceType, targetType, instructionComparer);
                ret = ret && CheckTypeInheritance(typeVisitor, sourceType, targetType);
                ret = ret && CheckImplementedInterfaces(typeVisitor, sourceType, targetType);
            }

            return ret;
        }

        private bool CheckTypeGenericInformation(ITypeDiffVisitor typeVisitor, TypeDefinition source, TypeDefinition target)
        {
            return typeVisitor.VisitGenerics(source, target);
        }

        private bool CheckTypeCustomAttributes(ITypeDiffVisitor typeVisitor, TypeDefinition sourceType, TypeDefinition targetType)
        {
            return typeVisitor.VisitCustomAttributes(sourceType, targetType);
        }

        private static bool CheckImplementedInterfaces(ITypeDiffVisitor typeVisitor, TypeDefinition sourceType, TypeDefinition targetType)
        {
            if (sourceType.Interfaces.Count != targetType.Interfaces.Count)
            {
                return false;
            }

            return sourceType.Interfaces.Except(targetType.Interfaces, new InterfaceComparer()).Any() == false;
        }

        private static bool CheckTypeInheritance(ITypeDiffVisitor typeVisitor, TypeDefinition source, TypeDefinition target)
        {
            if (target.BaseType == null && source.BaseType == null)
            {
                return true;
            }

            if (target.BaseType != null && source.BaseType.FullName == target.BaseType.FullName)
            {
                return true;
            }

            return typeVisitor.VisitBaseType(source.BaseType.Resolve(), target);
        }


        private static bool CheckTypeMembers(ITypeDiffVisitor typeVisitor, TypeDefinition source, TypeDefinition target, Func<Instruction, Instruction, bool?> instructionComparer)
        {
            return CheckFields(typeVisitor, source, target)
                   && CheckMethods(typeVisitor, source, target, instructionComparer)
                   && CheckEvents(typeVisitor, source, target)
                   && CheckProperties(typeVisitor, source, target);
        }

        private static bool CheckProperties(ITypeDiffVisitor typeVisitor, TypeDefinition source, TypeDefinition target)
        {
            foreach (var sourceProperty in source.Properties)
            {
                var propertyVisitor = typeVisitor.VisitMember(sourceProperty);
                var targetProperty = propertyVisitor.VisitProperty(sourceProperty, target);

                if (targetProperty == null)
                    return false;

                var ret = propertyVisitor.VisitType(sourceProperty, targetProperty)
                          && propertyVisitor.VisitAttributes(sourceProperty, targetProperty)
                          && propertyVisitor.VisitAccessors(sourceProperty, targetProperty)
                          && propertyVisitor.VisitCustomAttributes(sourceProperty, targetProperty);

                if (!ret)
                    return false;
            }

            return true;
        }

        private static bool CheckEvents(ITypeDiffVisitor typeVisitor, TypeDefinition source, TypeDefinition target)
        {
            foreach (var sourceEvent in source.Events)
            {
                var eventVisitor = typeVisitor.VisitMember(sourceEvent);
                var targetEvent = eventVisitor.VisitEvent(sourceEvent, target);

                if (targetEvent == null)
                    return false;

                var ret = eventVisitor.VisitType(sourceEvent, targetEvent)
                          && eventVisitor.VisitAttributes(sourceEvent, targetEvent)
                          && eventVisitor.VisitAccessors(sourceEvent, targetEvent)
                          && eventVisitor.VisitCustomAttributes(sourceEvent, targetEvent);

                if (!ret)
                    return false;
            }

            return true;
        }

        private static bool CheckMethods(ITypeDiffVisitor typeVisitor, TypeDefinition source, TypeDefinition target, Func<Instruction, Instruction, bool?> instructionComparer)
        {
            var list = target.Methods.GroupBy(m => m.FullName).ToList();

            var duplication = list.Where(g => g.Count() > 1);
            if (duplication.Any())
            {
                var duplicated = duplication.First().ElementAt(0);
                typeVisitor.VisitMember(duplicated).VisitDuplication(duplicated);

                return false;
            }

            var ret = true;
            var targetMethods = target.Methods.ToDictionary(m => m.FullName);
            foreach (var sourceMethod in source.Methods)
            {
                var memberVisitor = typeVisitor.VisitMember(sourceMethod);
                if (memberVisitor == null)
                {
                    continue;
                }

                if (!CheckTypeMember(memberVisitor, sourceMethod, target, targetMethods))
                {
                    ret = false;
                    continue;
                }

                var targetMethod = targetMethods[sourceMethod.FullName];
                if (sourceMethod.ReturnType.FullName != targetMethod.ReturnType.FullName)
                {
                    if (!memberVisitor.VisitReturnType(sourceMethod, targetMethod))
                    {
                        return false;
                    }
                }

                ret &= memberVisitor.VisitMemberProperties(sourceMethod, targetMethod);
                if (sourceMethod.Attributes != targetMethod.Attributes)
                {
                    if (!memberVisitor.VisitAttributes(sourceMethod, targetMethod))
                    {
                        return false;
                    }
                }

                if (!memberVisitor.VisitCustomAttributes(sourceMethod, targetMethod))
                    return false;

                if (!memberVisitor.VisitGenerics(sourceMethod, targetMethod))
                {
                    ret = false;
                }

                if (!CheckMethodBody(memberVisitor, sourceMethod, targetMethod, instructionComparer))
                {
                    ret = false;
                }
            }

            return ret;
        }


        private static bool CheckMethodBody(IMethodDiffVisitor visitor, MethodDefinition source, MethodDefinition target, Func<Instruction, Instruction, bool?> instructionComparer)
        {
            if (source.HasBody != target.HasBody)
            {
                return visitor.VisitBody(source, target);
            }

            if (source.Body == null)
            {
                return true;
            }

            if (source.Body.Variables.Except(target.Body.Variables, VariableDefinitionComparer.Instance).Any())
            {
                visitor.VisitLocalVariables(source, target);
                return false;
            }

            var modulesToIgnore = new HashSet<IMetadataScope> { source.DeclaringType.Scope, target.DeclaringType.Scope };
            var targetInstructions = SkipIrrelevantInstructions(target.Body.Instructions).GetEnumerator();
            int skipCount = 0;
            foreach (var instruction in SkipIrrelevantInstructions(source.Body.Instructions))
            {
                while (skipCount-- > 0 && !targetInstructions.MoveNext())
                {
                }

                if (!targetInstructions.MoveNext())
                {
                    return visitor.VisitBody(source, target, instruction);
                }

                if (!EqualOrEquivalent(instruction, targetInstructions.Current, instructionComparer, source.IsStatic, modulesToIgnore, out skipCount))
                {
                    return visitor.VisitBody(source, target, instruction);
                }
            }

            if (targetInstructions.MoveNext())
            {
                return visitor.VisitBody(source, target, targetInstructions.Current);
            }

            return true;
        }

        private static IEnumerable<Instruction> SkipIrrelevantInstructions(Collection<Instruction> instructions)
        {
            var instructionFilter = LenientInstructionComparer.Instantiate();
            return instructions
                .Where(instructionFilter.IgnoreNops)
                .Where(instructionFilter.IgnoreNonRequiredLocalVariableAssignments);
        }

        private static bool EqualOrEquivalent(Instruction instruction, Instruction current, Func<Instruction, Instruction, bool?> instructionComparer, bool isStatic, HashSet<IMetadataScope> scopesToIgnore,
            out int skipCount)
        {
            skipCount = 0;
            instruction = LenientInstructionComparer.SkipNonRelevantInstructions(instruction);
            current = LenientInstructionComparer.SkipNonRelevantInstructions(current);

            if (instruction?.OpCode == current?.OpCode)
                return ValidateOperands(instruction, current, instructionComparer, scopesToIgnore);

            if (_instructionValidator.TryGetValue(instruction.OpCode.Code, out var validator))
            {
                var (ret, c) = validator(instruction, current, isStatic);
                skipCount = c;

                return ret;
            }

            Debug.Assert(current != null);

            switch (instruction.OpCode.Code)
            {
                case Code.Stloc_0:
                case Code.Stloc_1:
                case Code.Stloc_2:
                case Code.Stloc_3:
                    throw new Exception();

                case Code.Ldarga_S:
                    return current.OpCode.Code == Code.Ldarga;
                case Code.Ldarg_S:
                    return current.OpCode.Code == Code.Ldarg;

                case Code.Ldloca_S:
                    return current.OpCode.Code == Code.Ldloca;

                case Code.Ldc_I4_S:
                    return current.OpCode.Code == Code.Ldc_I4 && Convert.ToInt32(instruction.Operand) == Convert.ToInt32(current.Operand);

                case Code.Ldc_I4_0:
                case Code.Ldc_I4_1:
                case Code.Ldc_I4_2:
                case Code.Ldc_I4_3:
                case Code.Ldc_I4_4:
                case Code.Ldc_I4_5:
                case Code.Ldc_I4_6:
                case Code.Ldc_I4_7:
                case Code.Ldc_I4_8:
                    return current.OpCode.Code == Code.Ldc_I4 && (instruction.OpCode.Code - Code.Ldc_I4_0) == Convert.ToInt32(current.Operand);

                case Code.Ldc_I4_M1:
                    return current.OpCode.Code == Code.Ldc_I4 && (int) current.Operand == -1 && current.Next.OpCode == OpCodes.Neg;

                case Code.Ldc_R8:
                    return AreEquivalentLoads(current, Code.Conv_R8, ref skipCount);

                case Code.Ldc_R4:
                    return AreEquivalentLoads(current, Code.Conv_R4, ref skipCount);

                case Code.Leave:
                case Code.Leave_S:
                case Code.Br:
                case Code.Brfalse:
                case Code.Brfalse_S:
                case Code.Brtrue:
                case Code.Brtrue_S:
                    return (current.OpCode.FlowControl == FlowControl.Branch || current.OpCode.FlowControl == FlowControl.Cond_Branch) && EqualOrEquivalent((Instruction) instruction.Operand, (Instruction) current.Operand, instructionComparer, isStatic, scopesToIgnore, out skipCount);

                case Code.Pop:
                    if (current.Previous == null || instruction.Previous == null)
                    {
                        return false;
                    }

                    return current.OpCode.Code == Code.Stloc && current.Previous.OpCode.IsCallOrNewObj() && instruction.Previous.OpCode.IsCallOrNewObj() &&
                           LenientInstructionComparer.HasNoLocalVariableLoads(instruction.Next, instruction.Operand);
            }

            if (instruction?.OpCode != current?.OpCode)
            {
                Console.WriteLine($"No specific validation for {instruction.OpCode} operands!");
                return false;
            }

            return true;
        }

        private static bool ValidateOperands(Instruction instruction, Instruction current, Func<Instruction, Instruction, bool?> instructionComparer, HashSet<IMetadataScope> scopesToIgnore)
        {
            if (instructionComparer != null)
            {
                var result = instructionComparer(instruction, current);
                if (result.HasValue)
                    return result.Value;
            }
            
            if (TryValidateFieldReference(instruction, current, scopesToIgnore, out var validationResult))
                return validationResult;
            
            if (TryValidateMethodReference(instruction, current, scopesToIgnore, out validationResult))
                return validationResult;
            
            if (TryValidateTypeReference(instruction, current, scopesToIgnore, out validationResult))
                return validationResult;
            
            return true;
        }

        private static bool TryValidateTypeReference(Instruction instruction, Instruction current, HashSet<IMetadataScope> scopesToIgnore, out bool validationResult)
        {
            validationResult = false;
            if (instruction.Operand is not TypeReference trInstruction || current.Operand is not TypeReference trCurrent)
                return false;

            if (scopesToIgnore.Contains(trInstruction.Scope) || scopesToIgnore.Contains(trCurrent.Scope))
            {
                validationResult = true;
            }
            else
            {
                validationResult = trInstruction.FullName == trCurrent.FullName;
            }

            return true;
        }

        private static bool TryValidateMethodReference(Instruction instruction, Instruction current, HashSet<IMetadataScope> scopesToIgnore, out bool validationResult)
        {
            validationResult = false;
            if (instruction.Operand is not MethodReference mrInstruction || current.Operand is not MethodReference mrCurrent)
                return false;

            if (scopesToIgnore.Contains(mrInstruction.DeclaringType.Scope) || scopesToIgnore.Contains(mrCurrent.DeclaringType.Scope))
            {
                validationResult = true;
            }
            else
            {
                validationResult = MethodReferenceComparer.Instance.Compare(mrInstruction, mrCurrent) == 0;
            }
            return true;
        }

        private static bool TryValidateFieldReference(Instruction instruction, Instruction current, HashSet<IMetadataScope> scopesToIgnore, out bool validationResult)
        {
            validationResult = false;
            if (instruction.Operand is not FieldReference frInstruction || current.Operand is not FieldReference frCurrent)
                return false;
            
            if (frInstruction.Name != frCurrent.Name || frInstruction.DeclaringType.FullName != frCurrent.DeclaringType.FullName)
            {
                validationResult = false;
            }
            else if (scopesToIgnore.Contains(frInstruction.DeclaringType.Scope) || scopesToIgnore.Contains(frCurrent.DeclaringType.Scope))
            {
                validationResult = frInstruction.FieldType.FullName == frCurrent.FieldType.FullName;
            }
            else
            {
                validationResult = frInstruction.DeclaringType.Scope.Name == frCurrent.DeclaringType.Scope.Name ||
                                   frInstruction.DeclaringType.Scope.Name == "System.Private.CoreLib" 
                                            && (frCurrent.DeclaringType.Scope.Name == "System.Runtime" || frCurrent.DeclaringType.Scope.Name == "mscorlib"); 
            }
            return true;
        }

        private static bool AreEquivalentLoads(Instruction current, Code convInstruction, ref int skipCount)
        {
            /* This code is here to workaround Cecilify not optimizing numeric constant loads to its final types. For instance, 'double d = 1' will generate:
             *      ldc.i4 1
             *      conv.r8
             * instead of
             *      ldc.r8 1
             */
            var areEquivalentLoads = (current.OpCode.Code == Code.Ldc_I4 || current.OpCode.Code == Code.Ldc_I8 || current.OpCode.Code == Code.Ldc_R4) && (current.Next?.OpCode.Code == convInstruction);
            if (areEquivalentLoads)
                skipCount = 1;

            return areEquivalentLoads;
        }

        private static int VarIndex(object operand)
        {
            return ((VariableDefinition) operand).Index;
        }

        private static bool CheckFields(ITypeDiffVisitor typeVisitor, TypeDefinition source, TypeDefinition target)
        {
            var targetFields = target.Fields.ToDictionary(f => f.FullName);
            foreach (var sourceMember in source.Fields)
            {
                var memberVisitor = typeVisitor.VisitMember(sourceMember);
                if (memberVisitor == null)
                {
                    continue;
                }

                if (!CheckTypeMember(memberVisitor, sourceMember, target, targetFields))
                {
                    continue;
                }

                var targetField = targetFields[sourceMember.FullName];
                if (sourceMember.FieldType.FullName != targetField.FieldType.FullName)
                {
                    if (!memberVisitor.VisitFieldType(sourceMember, targetField))
                    {
                        return false;
                    }
                }

                if (sourceMember.Attributes != targetField.Attributes)
                {
                    if (!memberVisitor.VisitAttributes(sourceMember, targetField))
                    {
                        return false;
                    }
                }

                if (sourceMember.HasConstant != targetField.HasConstant)
                {
                    memberVisitor.VisitConstant(sourceMember, targetField);
                    return false;
                }

                if (sourceMember.HasConstant && sourceMember.Constant.ToString() != targetField.Constant.ToString())
                {
                    memberVisitor.VisitConstant(sourceMember, targetField);
                    return false;
                }

                if (!memberVisitor.VisitCustomAttributes(sourceMember, targetField))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool CheckTypeMember<T>(IMemberDiffVisitor memberVisitor, T sourceMember, TypeDefinition target, IDictionary<string, T> targetMembers) where T : IMemberDefinition
        {
            if (!targetMembers.ContainsKey(sourceMember.FullName))
            {
                return memberVisitor.VisitMissing(sourceMember, target);
            }

            var targetMember = targetMembers[sourceMember.FullName];
            if (sourceMember.FullName != targetMember.FullName && !memberVisitor.VisitName(sourceMember, targetMember))
            {
                return false;
            }

            if (sourceMember.DeclaringType.FullName != targetMember.DeclaringType.FullName && !memberVisitor.VisitDeclaringType(sourceMember, target))
            {
                return false;
            }
            
            return true;
        }

        private static bool CheckTypeAttributes(ITypeDiffVisitor typeVisitor, TypeDefinition source, TypeDefinition target)
        {
            return source.Attributes == target.Attributes || typeVisitor.VisitAttributes(source, target);
        }

        private static (bool, int) OperandObliviousValidator(Instruction lhs, Instruction rhs, bool isDefiningMethodStatic)
        {
            return (lhs.OpCode == rhs.OpCode, 0);
        }

        private static (bool, int) LoadArgumentShortCutValidator(Instruction lhs, Instruction rhs, bool isDefiningMethodStatic)
        {
            if (lhs.OpCode == rhs.OpCode)
                return (true, 0);

            var paramIndexOffset = isDefiningMethodStatic ? 0 : 1;
            var ret = lhs.OpCode.Code switch
            {
                Code.Ldarg_0 => rhs.OpCode.Code == Code.Ldarg && (((ParameterDefinition) rhs.Operand).Index + paramIndexOffset) == 0,
                Code.Ldarg_1 => rhs.OpCode.Code == Code.Ldarg && (((ParameterDefinition) rhs.Operand).Index + paramIndexOffset) == 1,
                Code.Ldarg_2 => rhs.OpCode.Code == Code.Ldarg && (((ParameterDefinition) rhs.Operand).Index + paramIndexOffset) == 2,
                Code.Ldarg_3 => rhs.OpCode.Code == Code.Ldarg && (((ParameterDefinition) rhs.Operand).Index + paramIndexOffset) == 3,
                _ => throw new NotSupportedException()
            };

            return (ret, 0);
        }

        private static (bool, int) LoadLocalShortCutValidator(Instruction lhs, Instruction rhs, bool isDefiningMethodStatic)
        {
            if (lhs.OpCode == rhs.OpCode)
                return (true, 0);

            if (rhs.OpCode.Code != Code.Ldloc)
                return (false, 0);

            var ret = lhs.OpCode.Code switch
            {
                Code.Ldloc_0 => VarIndex(rhs.Operand) == 0,
                Code.Ldloc_1 => VarIndex(rhs.Operand) == 1,
                Code.Ldloc_2 => VarIndex(rhs.Operand) == 2,
                Code.Ldloc_3 => VarIndex(rhs.Operand) == 3,

                _ => throw new NotSupportedException()
            };

            return (ret, 0);
        }

        private static (bool, int) StoreLocalShortCutValidator(Instruction lhs, Instruction rhs, bool isDefiningMethodStatic)
        {
            if (lhs.OpCode == rhs.OpCode)
                return (true, 0);

            if (rhs.OpCode.Code != Code.Stloc)
                return (false, 0);

            var ret = lhs.OpCode.Code switch
            {
                Code.Stloc_0 => VarIndex(rhs.Operand) == 0,
                Code.Stloc_1 => VarIndex(rhs.Operand) == 1,
                Code.Stloc_2 => VarIndex(rhs.Operand) == 2,
                Code.Stloc_3 => VarIndex(rhs.Operand) == 3,

                _ => throw new NotSupportedException()
            };

            return (ret, 0);
        }

        private static (bool, int) ValidateCalls(Instruction lhs, Instruction rhs, bool isDefiningMethodStatic)
        {
            var m1 = (MethodReference) lhs.Operand;
            var m2 = (MethodReference) rhs.Operand;

            var ret = MethodReferenceComparer.Instance.Compare(m1, m2) == 0;
            return (ret, 0);
        }

        private static (bool, int) ValidateCallSite(Instruction lhs, Instruction rhs, bool isDefiningMethodStatic)
        {
            var leftCallSite = (CallSite) lhs.Operand;
            var rightCallSite = (CallSite) rhs.Operand;

            var ret = MethodSignatureComparer.Instance.Compare(leftCallSite, rightCallSite) == 0;
            return (ret, 0);
        }

        private static (bool, int) ValidateLocalVariableIndex(Instruction lhs, Instruction rhs, bool isDefiningMethodStatic)
        {
            var varDefLhs = (VariableDefinition) lhs.Operand;
            var varDefRhs = (VariableDefinition) rhs.Operand;

            var ret = varDefLhs.VariableType.FullName == varDefRhs.VariableType.FullName &&
                      varDefLhs.IsPinned == varDefRhs.IsPinned;

            return (ret, 0);
        }

        private static (bool, int) ValidateLoadMinusOne(Instruction lhs, Instruction rhs, bool isDefiningMethodStatic)
        {
            var ret = rhs.OpCode == OpCodes.Ldc_I4 && (int) rhs.Operand == 1 && rhs?.Next?.OpCode == OpCodes.Neg;
            return (ret, 1);
        }

        private static bool ValidateTypeReference(TypeReference typeLhs, TypeReference typeRhs)
        {
            var typeNameMatches = typeLhs.FullName == typeRhs.FullName;
            if (!typeNameMatches)
            {
                return false;
            }

            switch (typeLhs)
            {
                case ArrayType lhsAt:
                    var rhsAt = typeRhs as ArrayType;
                    return ValidateTypeReference(lhsAt.ElementType, rhsAt?.ElementType);

                case GenericInstanceType _:
                    var rhsGen = typeRhs as GenericInstanceType;
                    return rhsGen != null;
            }

            return true;
        }

        private static (bool, int) ValidateTypeReference(Instruction lhs, Instruction rhs, bool isDefiningMethodStatic)
        {
            var typeLhs = (TypeReference) lhs.Operand;
            var typeRhs = (TypeReference) rhs.Operand;

            return (ValidateTypeReference(typeLhs, typeRhs), 0);
        }

        private static (bool, int) ValidateField(Instruction lhs, Instruction rhs, bool isDefiningMethodStatic)
        {
            if (lhs.OpCode != rhs.OpCode)
                return (false, 0);

            var fieldLhs = (FieldReference) lhs.Operand;
            var fieldRhs = (FieldReference) rhs.Operand;

            var ret = fieldLhs.FieldType.FullName == fieldRhs.FieldType.FullName && fieldLhs.FullName == fieldRhs.FullName;
            return (ret, 0);
        }
    }

    internal struct LenientInstructionComparer
    {
        public static LenientInstructionComparer Instantiate()
        {
            var instance = new LenientInstructionComparer();
            instance.toBeIgnored = new HashSet<Instruction>();

            return instance;
        }

        public bool IgnoreNops(Instruction i)
        {
            return i.OpCode != OpCodes.Nop;
        }

        public bool IgnoreNonRequiredLocalVariableAssignments(Instruction inst)
        {
            if (toBeIgnored.Remove(inst))
            {
                return false;
            }

            if (!IsStLoc(inst.OpCode) || inst.Next == null)
            {
                return true;
            }

            var current = SkipNops(inst.Next);
            if (IsLdLoc(current.OpCode) && current.Operand == inst.Operand)
            {
                toBeIgnored.Add(current);
                return false;
            }

            toBeIgnored.Clear();

            return true;
        }

        private static bool IsLdLoc(OpCode opCode)
        {
            return opCode == OpCodes.Ldloc ||
                   opCode == OpCodes.Ldloc_0 ||
                   opCode == OpCodes.Ldloc_1 ||
                   opCode == OpCodes.Ldloc_2 ||
                   opCode == OpCodes.Ldloc_3 ||
                   opCode == OpCodes.Ldloc_S;
        }

        private static bool IsStLoc(OpCode opCode)
        {
            return opCode == OpCodes.Stloc ||
                   opCode == OpCodes.Stloc_0 ||
                   opCode == OpCodes.Stloc_1 ||
                   opCode == OpCodes.Stloc_2 ||
                   opCode == OpCodes.Stloc_3 ||
                   opCode == OpCodes.Stloc_S;
        }

        public static bool HasNoLocalVariableLoads(Instruction instruction, object operand)
        {
            while (instruction != null && (instruction.OpCode != OpCodes.Ldloc || instruction.Operand != operand))
            {
                instruction = instruction.Next;
            }

            return instruction == null;
        }

        public static Instruction SkipNonRelevantInstructions(Instruction instruction)
        {
            instruction = SkipNops(instruction);
            if (!IsStLoc(instruction.OpCode) || instruction.Next == null)
            {
                return instruction;
            }

            var nextInstruction = SkipNops(instruction.Next);
            if (IsLdLoc(nextInstruction.OpCode) && nextInstruction.Operand == instruction.Operand)
            {
                return SkipNops(nextInstruction.Next);
            }

            return instruction;
        }

        private static Instruction SkipNops(Instruction instruction)
        {
            while (instruction != null && instruction.OpCode == OpCodes.Nop)
                instruction = instruction.Next;

            return instruction;
        }

        private HashSet<Instruction> toBeIgnored;
    }
}
