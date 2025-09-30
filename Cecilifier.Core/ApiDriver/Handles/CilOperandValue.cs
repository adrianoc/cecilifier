using Microsoft.CodeAnalysis;

namespace Cecilifier.Core.ApiDriver.Handles;

public record struct CilOperandValue(ITypeSymbol Type, object Value);
