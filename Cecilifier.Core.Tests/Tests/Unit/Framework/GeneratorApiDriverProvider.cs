using System.Collections;
using Cecilifier.ApiDriver.MonoCecil;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit.Framework;

class GeneratorApiDriverProvider : IEnumerable
{
    public IEnumerator GetEnumerator()
    {
        yield return new TestFixtureData(new MonoCecilGeneratorDriver()).SetArgDisplayNames("MonoCecil");
    }
}
