## unreleased

## Changed
	- fix error reporting on discord channel
	- fix volatile field handling
    - do not quote (') zipped project file.
    
## Added
    - basic support for Range/Index
    - set assembly entry point with Main() method from first class one is defined

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
