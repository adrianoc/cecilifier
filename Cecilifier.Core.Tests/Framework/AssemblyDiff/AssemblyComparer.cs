using System;
using System.Collections.Generic;
using System.Linq;
using Cecilifier.Core.Extensions;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;

namespace Cecilifier.Core.Tests.Framework.AssemblyDiff
{
    internal class AssemblyComparer
    {
        private static readonly Dictionary<Code, Func<Instruction, Instruction, bool>> _instructionValidator =
            new Dictionary<Code, Func<Instruction, Instruction, bool>>
            {
                {Code.Ret, OperandObliviousValidator},
                {Code.Nop, OperandObliviousValidator},
                {Code.Pop, OperandObliviousValidator},
                {Code.Ldarg_0, OperandObliviousValidator},
                {Code.Ldarg_1, OperandObliviousValidator},
                {Code.Ldarg_2, OperandObliviousValidator},
                {Code.Ldarg_3, OperandObliviousValidator},

                {Code.Call, ValidateCalls},
                {Code.Calli, ValidateCalls},
                {Code.Callvirt, ValidateCalls},
                {Code.Newobj, ValidateCalls},

                {Code.Ldftn, ValidateCalls},
                {Code.Ldloca, ValidateLocalVariableIndex},
                {Code.Ldloca_S, ValidateLocalVariableIndex},
                {Code.Ldloc, ValidateLocalVariableIndex},
                {Code.Ldloc_S, ValidateLocalVariableIndex},
                {Code.Stloc, ValidateLocalVariableIndex},
                {Code.Stloc_S, ValidateLocalVariableIndex},

                {Code.Castclass, ValidateTypeReference},
                {Code.Box, ValidateTypeReference},
                {Code.Isinst, ValidateTypeReference},
                {Code.Newarr, ValidateTypeReference},

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

        public bool Compare(IAssemblyDiffVisitor visitor)
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
            ISet<TypeDefinition> firstTypes = new HashSet<TypeDefinition>(sourceModule.Types);

            foreach (var sourceType in firstTypes.SelectMany(t => t.NestedTypes).Concat(firstTypes).Where(t => t.FullName != "<Module>"))
            {
                var targetType = targetModule.Types.SelectMany(ct => ct.NestedTypes).Concat(targetModule.Types).SingleOrDefault(t => t.FullName == sourceType.FullName);

                var typeVisitor = visitor.VisitType(sourceType);
                if (typeVisitor == null)
                {
                    continue;
                }

                if (targetType == default(TypeDefinition))
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
                //ret = ret && CheckTypeGenericInformation(typeVisitor, sourceType, targetType);
                ret = ret && CheckTypeMembers(typeVisitor, sourceType, targetType);
                ret = ret && CheckTypeInheritance(typeVisitor, sourceType, targetType);
                ret = ret && CheckImplementedInterfaces(typeVisitor, sourceType, targetType);
            }

            //TODO: Check missing classes
            return ret;
        }

        private bool CheckTypeCustomAttributes(ITypeDiffVisitor typeVisitor, TypeDefinition sourceType, TypeDefinition targetType)
        {
            if (sourceType.HasCustomAttributes != targetType.HasCustomAttributes)
            {
                if (!typeVisitor.VisitCustomAttributes(sourceType, targetType))
                {
                    return false;
                }
            }

            if (!sourceType.HasCustomAttributes)
            {
                return true;
            }

            foreach (var customAttribute in sourceType.CustomAttributes)
            {
                var found = targetType.CustomAttributes.Any(candidate => CustomAttributeMatches(candidate, customAttribute));
                if (!found)
                {
                    return false;
                }
            }

            return true;
        }

        private bool CustomAttributeMatches(CustomAttribute lhs, CustomAttribute rhs)
        {
            if (lhs.Constructor.ToString() != rhs.Constructor.ToString())
            {
                return false;
            }

            if (lhs.HasConstructorArguments != rhs.HasConstructorArguments)
            {
                return false;
            }

            if (lhs.HasConstructorArguments)
            {
                if (!lhs.ConstructorArguments.SequenceEqual(rhs.ConstructorArguments, CustomAttributeComparer.Instance))
                {
                    return false;
                }
            }

            if (lhs.HasProperties != rhs.HasProperties)
            {
                return false;
            }

            if (lhs.HasProperties)
            {
                if (!lhs.Properties.SequenceEqual(rhs.Properties, CustomAttributeNamedArgumentComparer.Instance))
                {
                    return false;
                }
            }

            if (lhs.HasFields != rhs.HasFields)
            {
                return false;
            }

            if (lhs.HasFields)
            {
                if (!lhs.Fields.SequenceEqual(rhs.Fields, CustomAttributeNamedArgumentComparer.Instance))
                {
                    return false;
                }
            }

            return true;
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


        private static bool CheckTypeMembers(ITypeDiffVisitor typeVisitor, TypeDefinition source, TypeDefinition target)
        {
            if (!CheckFields(typeVisitor, source, target))
            {
                return false;
            }

            if (!CheckMethods(typeVisitor, source, target))
            {
                return false;
            }

            //foreach (var sourceEvent in source.Events)
            //{
            //}

            //foreach (var sourcePropertie in source.Properties)
            //{
            //}

            return true;
        }

        private static bool CheckMethods(ITypeDiffVisitor typeVisitor, TypeDefinition source, TypeDefinition target)
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

                if (sourceMethod.Attributes != targetMethod.Attributes)
                {
                    if (!memberVisitor.VisitAttributes(sourceMethod, targetMethod))
                    {
                        return false;
                    }
                }

                if (!memberVisitor.VisitGenerics(sourceMethod, targetMethod))
                {
                    ret = false;
                }

                if (!CheckMethodBody(memberVisitor, sourceMethod, targetMethod))
                {
                    ret = false;
                }
            }

            return ret;
        }


        private static bool CheckMethodBody(IMethodDiffVisitor visitor, MethodDefinition source, MethodDefinition target)
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

            var targetInstructions = SkipIrrelevantInstructions(target.Body.Instructions).GetEnumerator();
            foreach (var instruction in SkipIrrelevantInstructions(source.Body.Instructions))
            {
                if (!targetInstructions.MoveNext())
                {
                    return visitor.VisitBody(source, target, instruction);
                }

                if (!EqualOrEquivalent(instruction, targetInstructions.Current))
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

        private static bool EqualOrEquivalent(Instruction instruction, Instruction current)
        {
            while (instruction != null && instruction.OpCode == OpCodes.Nop)
            {
                instruction = instruction.Next;
            }

            while (current != null && current.OpCode == OpCodes.Nop)
            {
                current = current.Next;
            }

            if (instruction?.OpCode == current?.OpCode)
            {
                if (_instructionValidator.TryGetValue(instruction.OpCode.Code, out var validator))
                {
                    return validator(instruction, current);
                }

                Console.WriteLine($"No specific validation for {instruction.OpCode} operands!");
                return true;
            }

            switch (instruction.OpCode.Code)
            {
                case Code.Ldarg_0: return current.OpCode.Code == Code.Ldarg && (int) current.Operand == 0;
                case Code.Ldarg_1: return current.OpCode.Code == Code.Ldarg && (int) current.Operand == 1;
                case Code.Ldarg_2: return current.OpCode.Code == Code.Ldarg && (int) current.Operand == 2;
                case Code.Ldarg_3: return current.OpCode.Code == Code.Ldarg && (int) current.Operand == 3;

                case Code.Ldloc_0: return current.OpCode.Code == Code.Ldloc && VarIndex(current.Operand) == 0;
                case Code.Ldloc_1: return current.OpCode.Code == Code.Ldloc && VarIndex(current.Operand) == 1;
                case Code.Ldloc_2: return current.OpCode.Code == Code.Ldloc && VarIndex(current.Operand) == 2;
                case Code.Ldloc_3: return current.OpCode.Code == Code.Ldloc && VarIndex(current.Operand) == 3;

                case Code.Stloc_0: return current.OpCode.Code == Code.Stloc && VarIndex(current.Operand) == 0;
                case Code.Stloc_1: return current.OpCode.Code == Code.Stloc && VarIndex(current.Operand) == 1;
                case Code.Stloc_2: return current.OpCode.Code == Code.Stloc && VarIndex(current.Operand) == 2;
                case Code.Stloc_3: return current.OpCode.Code == Code.Stloc && VarIndex(current.Operand) == 3;

                case Code.Ldarga_S: return current.OpCode.Code == Code.Ldarga;
                case Code.Ldarg_S: return current.OpCode.Code == Code.Ldarg;

                case Code.Ldc_I4_S:
                case Code.Ldc_I4_0:
                case Code.Ldc_I4_1:
                case Code.Ldc_I4_2:
                case Code.Ldc_I4_3:
                case Code.Ldc_I4_4:
                case Code.Ldc_I4_5:
                case Code.Ldc_I4_6:
                case Code.Ldc_I4_7:
                case Code.Ldc_I4_8: return current.OpCode.Code == Code.Ldc_I4;

                case Code.Leave:
                case Code.Leave_S:
                case Code.Br:
                case Code.Brfalse:
                case Code.Brfalse_S:
                case Code.Brtrue:
                case Code.Brtrue_S: return current.OpCode.FlowControl == FlowControl.Branch && EqualOrEquivalent((Instruction) instruction.Operand, (Instruction) current.Operand);

                case Code.Pop:
                    if (current.Previous == null || instruction.Previous == null)
                    {
                        return false;
                    }

                    return current.OpCode.Code == Code.Stloc && current.Previous.OpCode.IsCall() && instruction.Previous.OpCode.IsCall() &&
                           LenientInstructionComparer.HasNoLocalVariableLoads(instruction.Next, instruction.Operand);
            }

            return false;
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
            }

            return true;
        }


        private static bool CheckTypeMember<T>(IMemberDiffVisitor memberVisitor, IMemberDefinition sourceMember, TypeDefinition target, IDictionary<string, T> targetMembers) where T : IMemberDefinition
        {
            if (!targetMembers.ContainsKey(sourceMember.FullName))
            {
                return memberVisitor.VisitMissing(sourceMember, target);
            }

            var targetMember = targetMembers[sourceMember.FullName];
            if (sourceMember.FullName != targetMember.FullName)
            {
                if (!memberVisitor.VisitName(sourceMember, targetMember))
                {
                    return false;
                }
            }

            if (sourceMember.DeclaringType.FullName != targetMember.DeclaringType.FullName)
            {
                if (!memberVisitor.VisitDeclaringType(sourceMember, target))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool CheckTypeAttributes(ITypeDiffVisitor typeVisitor, TypeDefinition source, TypeDefinition target)
        {
            return source.Attributes == target.Attributes || typeVisitor.VisitAttributes(source, target);
        }


        private static bool OperandObliviousValidator(Instruction lhs, Instruction rhs)
        {
            return true;
        }

        private static bool ValidateCalls(Instruction lhs, Instruction rhs)
        {
            var m1 = (MethodReference) lhs.Operand;
            var m2 = (MethodReference) rhs.Operand;

            return MethodReferenceComparer.Instance.Compare(m1, m2) == 0;
        }

        private static bool ValidateLocalVariableIndex(Instruction lhs, Instruction rhs)
        {
            var varDefLhs = (VariableDefinition) lhs.Operand;
            var varDefRhs = (VariableDefinition) rhs.Operand;

            return varDefLhs.VariableType.FullName == varDefRhs.VariableType.FullName &&
                   varDefLhs.IsPinned == varDefRhs.IsPinned;
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

        private static bool ValidateTypeReference(Instruction lhs, Instruction rhs)
        {
            var typeLhs = (TypeReference) lhs.Operand;
            var typeRhs = (TypeReference) rhs.Operand;

            return ValidateTypeReference(typeLhs, typeRhs);
        }

        private static bool ValidateField(Instruction lhs, Instruction rhs)
        {
            var fieldLhs = (FieldReference) lhs.Operand;
            var fieldRhs = (FieldReference) rhs.Operand;

            return fieldLhs.FieldType.FullName == fieldRhs.FieldType.FullName;
        }
    }

    internal class CustomAttributeNamedArgumentComparer : IEqualityComparer<CustomAttributeNamedArgument>
    {
        static CustomAttributeNamedArgumentComparer()
        {
            Instance = new CustomAttributeNamedArgumentComparer();
        }

        public static IEqualityComparer<CustomAttributeNamedArgument> Instance { get; }

        public bool Equals(CustomAttributeNamedArgument x, CustomAttributeNamedArgument y)
        {
            if (x.Name != y.Name)
            {
                return false;
            }

            return CustomAttributeComparer.Instance.Equals(x.Argument, y.Argument);
        }

        public int GetHashCode(CustomAttributeNamedArgument obj)
        {
            return 0;
        }
    }

    internal class CustomAttributeComparer : IEqualityComparer<CustomAttributeArgument>
    {
        static CustomAttributeComparer()
        {
            Instance = new CustomAttributeComparer();
        }

        public static IEqualityComparer<CustomAttributeArgument> Instance { get; }

        public bool Equals(CustomAttributeArgument x, CustomAttributeArgument y)
        {
            if (x.Type.ToString() != y.Type.ToString())
            {
                return false;
            }

            if (x.Value != null && y.Value == null)
            {
                return false;
            }

            if (x.Value == null && y.Value != null)
            {
                return false;
            }

            return x.Value != null
                ? x.Value.ToString() == y.Value.ToString()
                : true;
        }

        public int GetHashCode(CustomAttributeArgument obj)
        {
            return 0;
        }
    }

    internal class InterfaceComparer : IEqualityComparer<InterfaceImplementation>
    {
        public bool Equals(InterfaceImplementation x, InterfaceImplementation y)
        {
            return x.InterfaceType.FullName == y.InterfaceType.FullName;
        }

        public int GetHashCode(InterfaceImplementation obj)
        {
            return obj.InterfaceType.Name.GetHashCode();
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

            if (inst.Next == null)
            {
                return true;
            }

            if (inst.OpCode != OpCodes.Stloc || inst.Next.OpCode != OpCodes.Ldloc || inst.Operand != inst.Next.Operand)
            {
                return true;
            }

            // We have an *stloc X* followed by an *ldloc X* so lets check if we have any other reference to the same
            // local var.
            var ignoredInstructions = new HashSet<Instruction>();
            ignoredInstructions
                .Add(inst.Next); // if no other load from *X* is found we need to ignore current instruction (stloc X) and also the next one (ldloc X)

            var current = inst.Next.Next;
            while (current != null)
            {
                // found some other instruction accessing *X*
                if (current.Operand == inst.Operand)
                {
                    if (current.OpCode == OpCodes.Stloc)
                    {
                        ignoredInstructions.Add(current.Next);
                    }
                    else
                    {
                        // it is not a stloc so the instruction is important and we should use it in the comparison...
                        return true;
                    }
                }

                current = current.Next;
            }

            toBeIgnored = ignoredInstructions;
            return false;
        }

        public static bool HasNoLocalVariableLoads(Instruction instruction, object operand)
        {
            while (instruction != null && (instruction.OpCode != OpCodes.Ldloc || instruction.Operand != operand))
            {
                instruction = instruction.Next;
            }

            return instruction == null;
        }

        private HashSet<Instruction> toBeIgnored;
    }
}
