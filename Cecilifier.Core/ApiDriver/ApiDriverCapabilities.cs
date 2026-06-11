using System;

namespace Cecilifier.Core.ApiDriver;

[Flags]
public enum ApiDriverCapabilities
{
    None = 0x0,
    RequiresForwardReferences = 1 << 0x1,
    
    // ApiDriver is required to process ParameterSyntax; some ApiDrivers may process those nodes implicitly
    RequiresExplicitParameterSyntaxHandling = 1 << 0x2, 
}
