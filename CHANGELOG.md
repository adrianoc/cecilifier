## unreleased 

## Changed

## Added

- support for bitwise (|, &, ^) / logical operators (&&, ||)

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
<<<<<<< HEAD
- Support for for statements
=======
- Support for for statements	
>>>>>>> 70ebeb1 (updates Cecil & Microsoft.CodeAnalysis.CSharp versions)

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

<<<<<<< HEAD
- Support access of generic static/instance methods.
=======
- Support access of generic static/instance methods.
>>>>>>> 70ebeb1 (updates Cecil & Microsoft.CodeAnalysis.CSharp versions)
