## Unreleased

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
