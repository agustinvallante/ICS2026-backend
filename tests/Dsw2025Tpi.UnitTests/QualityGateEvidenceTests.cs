using Xunit;

namespace Dsw2025Tpi.UnitTests;

public class QualityGateEvidenceTests
{
    [Fact]
    public void EvidenceTest_PassesOnlyWhenFailureIsNotRequested()
    {
        var failTest = Environment.GetEnvironmentVariable("TP4_QA_FORCE_FAILURE");

        Assert.False(
            string.Equals(failTest, "true", StringComparison.OrdinalIgnoreCase),
            "Fallo intencional para verificar el bloqueo del deploy de prueba.");
    }
}
