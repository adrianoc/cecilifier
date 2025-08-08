namespace Cecilifier.Core.Tests.Tests.Unit.Framework;

public class MultipleILGeneratorApiDriverTest 
{
    protected MultipleILGeneratorApiDriverTest(IILGeneratorApiDriver apiDriver) => ApiDriver = apiDriver;
    protected IILGeneratorApiDriver ApiDriver { get; set; }
}
