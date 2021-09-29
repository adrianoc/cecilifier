## unreleased 

- bumped cecilifier version to 1.20.0 (I had messed up with versioning last time setting it to 1.2.0 instead of 1.20.0)
- cleanup previously messed up merge conflicts markers in this file :)

## Changed

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