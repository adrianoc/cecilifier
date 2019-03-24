My change log.... :)

##fixes
	- *call* instruction emitted with invalid target in some scenarios
	- calls to overloaded methods/ctors always resolved to the first member processed.
	- take operands of call/calli/callvirt into account when comparing assemblies.

##features
	- Support access of generic static/instance methods.
