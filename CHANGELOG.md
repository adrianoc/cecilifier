## [Unreleased]
- bumped cecilifier version to 1.70.0

## [Unreleased]
- bumped cecilifier version to 1.60.0
- fixed NRE when handling explicit delegate instantiation (#190)
- fixed TypeAttributes for static top level/inner types (#191)
- change code to copy .runtimeconfig.json file when running the cecilified code (#189)
- do not load implicit 'this' for static property access (#187)

## 15/Sept/2022

## Changed

- bumped cecilifier version to 1.50.0
- updated testing section of README.md

## Added

- support for covariant properties (#179)
- support for covariant return methods (#179)
- support for using statement (#177)
- support for modulus operator (%)

## Fixed

- static automatic properties being handled as instance ones (#183)
- forwarded field references generating invalid code (#182)
- forwarded attribute usage (#180)
- handling of attributes in type parameters (#181)

## 15/May/2022

## Changed

- bumped cecilifier version to 1.40.0
- updated Monaco editor to 0.32.1
- avoids redundant assignment to type's base type property when not necessary
- fully qualifies type names when resolving external types (#174)
- specifies correct type (text) when sending back json contents through the websocket.
- optimizes CecilifierExtensions.PascalCase() method to minimize gc allocations
- moves delegate method attributes constants to Constants.CommonCecilConstants
- removes some code duplication by reusing AddDelegateMethod() instead of more low-level ones
- gets rid of some allocations by resorting to ReadOnlySpan and StringBuilder
- gets rid of some allocations by moving UsageVisitor to a singleton

## Added

- comment to instruction that loads pre-computed span size
- basic support for adding inline comments when emitting CIL instructions
- support for stackalloc in nested expressions
- support for specifying assemblies to be referenced when cecilifying code snippets.


## Fixed

- generated code for property/method access on type parameter (#170)
- inner delegate declaration being ignored (#156)
- static fields/properties not being initialized if class do not have a static constructor (#167)
- locals declared in lambda expressions (#176)
- locals declared in property/event accessors (#175)

## 20/March/2022

## Changed

- bumped cecilifier version to 1.30.0
- new releases notification mechanism
- Updated projects to compile to C# 10 targeting .Net 6.0
- Initialize MethodDefinition instances with final return type whenever possible
  to minimize noise.
- Changed approach to avoid generate code referencing 'System.Private.CoreLib.dll'
- Improved assembly comparison by checking field references in instruction operands.  
- Improved support for System.Range (#140)
- Improved handling of System.Indexer variables and IndexExpressions used with arrays/Spans<T> (related to #143, #136)
- Improved stackalloc handling (#134, #135)

## Added

- Bug reporter
- Script to make it easier to update version information in Cecilifier projects.
- Added support for showing latest issues fixed on staging
- Added support for local functions (#124)
- Added support for operators !=, >= and <= (#79)
- Added support for typeof operator (#89)

## Fixed

- Generic methods references uses wrong type parameter (#91)
- Fixed issue with async method calls relying on .Result
- Fixed handling of statement contents used in comments
- Fixed nested enum declaration being ignored (issue #116)
- Fixed handling of delegate equality comparison (issue #113)
- Fixed potential name clash due to usage of simple names instead of fully qualified names in variable registration (#126)
- Fixed exception being thrown if code have access to fields defined in referenced assemblies (#128)
- Fixed bug causing open generic types to be handled as closed types in some scenarios
- Fixed WebSocket connection closing prematurely by lowering ping interval to meet timings in nginx.
- Fixed incorrect Ldlen instruction when retrieving the length of a Span<T> (instead calls Span<>.get_Length) (#143)
- Fixed exception if stackalloc for non primitives is used with non constant dimension (#135)
- Fixed exception upon assignment to method/property returning by ref (#136)
- Fixed field assignment in member access expressions (#130)
- Fixed Span<T>.Slice() not being called when indexer is passed a System.Range.(#140)
- Fixed assignment to System.Index missing op_Implicit(Int32) call (#139)
- Fixed field value being passed instead of its address (#142)
- Fixed missing Ldind and Ldflda/Ldsflda in some scenarios (#141 & #142)

## 28/Oct/2021

## Changed

- bumped cecilifier version to 1.22.0

## Added

- added support for const fields
- added support for DllImportAttribute / extern methods
- added support for simple string interpolation
- added support for nameof expression (issue #95)
- added support for operator overloading
- added support for user conversion operators
- added coverage for a couple of box scenarios

## Fixed

- fixed issue of setting static state by constructor when overriding formatting options
- skip balanced stloc0/ldloc0 when comparing assemblies (test infrastructure)
- fixed exception being thrown upon local variable being passed as an argument to a method call that is, itself, 
  used as an argument to another method call (issue #110)
  
## 09/Oct/2021

## Changed

- bumped cecilifier version to 1.21.0

## Added

- Mapping from `snippet` <-> `cecilified code` allowing users to more easily correlate which parts of the `snippet` code corresponds to which
  parts of the `cecilified code` and vice-versa.

## Fixed

- d4c7cea do not pretend all field references are accessing 'this' (issue #104)

## 02/Oct/2021

## Changed

- bumped cecilifier version to 1.20.0 (I had messed up with versioning last time setting it to 1.2.0 instead of 1.20.0)
- cleanup previously messed up merge conflicts markers in this file :)
- Replaced most ILProcessor.Create()/Append() calls with ILProcessor.Emit() to 
  reduce noise and make cecilified code easier to read.
  
## Added

- support for non-capturing lambdas converted to Func<>/Action<> (issue #101)
- support for cast operator (issue #88)
- added PR template
- added CODEOWNERS file

## Fixed

- fixed release notes mechanism
- initialization of doubles crashing (issue #97)

## 29/Aug/2021

## Changed

- bump cecilifier version to 1.2.0
- improved assembly comparison to check name of type parameters (testing)
- improved access modifier handling
- removed some code duplication
- updated Microsoft.CodeAnalysis.CSharp from version 3.10.0-3.final to 4.0.0-2.final
- updated Mono.Cecil from version 0.11.3 to 0.11.4

## Added

- support for bitwise (|, &, ^) / logical operators (&&, ||)  
- introduced ITypeResolver.Bcl property to represent the code to resolve common BCL types.

## Fixed

- fixed gist not working due to use of async
- fixed multiple issues with generic/inner type instantiation (issues #84 and #85)
- fixed missing box instruction on some ValueType instantiation  (#86)
- improved handling of inner types of generic outer types (issue #76)
- fixed code to emit ldsfld opcode when accessing static fields (issue #83)
- fixed implicit private static field declaration
- fixed crash upon static field assignment (issue #82)
- fixed name formatting not taking prefix/casing into account for some element kinds (#90)

## 04/July/2021

## Changed

- bump cecilifier version to 1.13.0
- fixed a couple of ref parameter issues
- updated Microsoft.CodeAnalysis.CSharp to version 3.10.0-3.final
- updated Mono.Cecil to version 0.11.3
- updated Newtonsoft.Json to version 13.0.1
- changed release notes UI.

## Added

- support for *out variables*
- support for *ref return*
- support for *local ref*
- code coverage

## 13/June/2021

## Changed

- bump cecilifier version to 1.12.0
- Redesigned site
- Added *standard csharp code* instead of blank content.
- Replaced [CodeMirror](https://github.com/codemirror/CodeMirror) with [Monaco editor](https://github.com/microsoft/monaco-editor).
- switched to net5.0
- fix error reporting on discord channel
- fix volatile field handling
- do not quote (') zipped project file.
- improved support for generics in general
- updated Mono.Cecil & Microsoft.CodeAnalysis.CSharp nuget libs to latest
- fixed generated code for delegate initialization / calling

## Added

- basic support for Range/Index
- support for switch statements
- set assembly entry point with Main() method from first class one is defined
- array/indexers support

## 04/July/2020

## Changed

- better comments on each generated code section
- report errors on discord channel

## Added

- a lot of other stuff that has not been reported since March/2019 :(
- Support for for statements

## 24/March/2019

## Changed

- fixed type resolution crashing in some scenarios
- wrong opcode being generated for some types

## Added

- Support for instantiating single dimensional arrays 

## 24/March/2019

### Changed

- *call* instruction emitted with invalid target in some scenarios
- fixed calls to overloaded methods/ctors always being resolved to the first member processed.
- take operands of various instructions (eg, call/calli/callvirt) into account when comparing assemblies.

### Added

- Support access of generic static/instance methods.