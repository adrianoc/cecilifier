# System.Reflection.Metadata Notes

## `MethodBodyStreamEncoder.AddMethodBody()` *hasDynamicStackAllocation* parameter

This parameter needs to be `true` for methods with `localloc` (C# `stackalloc`) instructions.

## `MetadataBuilder.AddMethodDefinition()` *parameterList* parameter

The documentation states:

> If the method declares parameters in the Params table, set this to the handle
> of the first one. Otherwise, set this to the handle of the first parameter declared
> by the next method definition. If no parameters are declared in the module, System.Reflection.Metadata.Ecma335.MetadataTokens.ParameterHandle(1).

If we need to abide to this it may become very complex to define the value to be used:

1. We would need to process the `next` method definition (at least process its parameters to create `ParameterHandle`s)
2. What if `next` method has no parameters? apply the same logic, i.e. use the 1st parameter of the `next.next` method if it does have parameters ? (a: I think so)

That said, if method has *no parameters* it seems that passing `System.Reflection.Metadata.Ecma335.MetadataTokens.ParameterHandle(1)` does work (tested in a simple scenario), i.e. it seems we do not need to abide to that rule.

Need testing with more complex scenarios:

1. Previous method definition has no parameters
1. Next method definition has no parameters
1. Next method definition has parameters
1. Others ?

## `MetadataBuilder.AddTypeDefinition()` *fieldList* and *methodList* parameters

Similar to above.

## `MetadataBuilder.AddMethodSemantics()`

Use this method to associate property getter/setter, event add/remove (others?) methods to its entity.


# Potential challenges

The main challenge is due the fact that everything assumes it targets Mono.Cecil

1. IVisitorContext.EmitCilInstruction()
1. Type resolution (MakeGenericType() and friends, ResolvePredefinedType(), etc for instance)
1. Member definition creation should be generalized (i.e. `CecilDefinitionsFactory` need to be abstracted somehow)
1. Assumptions around `ILProcessor` when adding instructions
1. `CecilifierExtensions.AsCecilApplication()` : boiler plate code needs to be abstracted somehow
    1. Probably this will impact front end code as well (for instance, starting line of mapped - C# <-> cecilified - code)
1. `Cecilifier.Core.Extensions.MethodExtensions` has plenty of Mono.Cecil specifics
1. Method call handling (reordering of generated code/instructions in general)
1. `PrivateCorlibFixerMixin` : the code used to fix references to PrivateCorlib
    1. Is this required at all with SRM ?
1. Differences in the API design may require different code paths in Cecilifier

# Design Options

## Expose an IILGeneratorApiDriver instance through IVisitorContext

This would be one step in the direction of decoupling IVisitorContext <-> Mono.Cecil

1. Any code that depends on EmitCilInstruction() would mostly be unchanged
1. EmitCilInstruction() would simply delegate to the `apiDriver`


### Problems

1. How to abstract `opcodes` type ? This type appears in multiple places in which the code needs to represent an IL opcode (for instance in `EmitCilInstruction()`).
    1. `CecilifierContext.EmitCilInstruction()` simply writes the text representation to the generated code
    1. Mono.Cecil uses `Mono.Cecil.Cil.OpCodes` class
    1. System.Reflection.Metadata uses `System.Reflection.Metadata.ILOpCode` enumeration


1. Investigate `System.Reflection.Metadata` 
1. Investigate potential custom configurations for SRM.