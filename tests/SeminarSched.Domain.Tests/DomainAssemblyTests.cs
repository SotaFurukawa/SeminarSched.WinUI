using SeminarSched.Domain;

namespace SeminarSched.Domain.Tests;

public sealed class DomainAssemblyTests
{
    [Fact]
    public void Reference_ExposesDomainAssemblyWithoutUiDependency()
    {
        var references = DomainAssembly.Reference.GetReferencedAssemblies();

        Assert.Equal("SeminarSched.Domain", DomainAssembly.Reference.GetName().Name);
        Assert.DoesNotContain(references, item => item.Name?.Contains("WinUI", StringComparison.OrdinalIgnoreCase) == true);
        Assert.DoesNotContain(references, item => item.Name?.Contains("Infrastructure", StringComparison.OrdinalIgnoreCase) == true);
    }
}
