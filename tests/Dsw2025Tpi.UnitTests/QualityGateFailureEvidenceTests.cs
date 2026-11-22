using Xunit;

namespace Dsw2025Tpi.UnitTests;

public class QualityGateFailureEvidenceTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void QualityGateEvidence_FailsIntentionally()
    {
        Assert.Fail("Fallo intencional - evidencia TP4");
    }
}
