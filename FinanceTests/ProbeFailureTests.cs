namespace FinanceTests;

/// <summary>Probe for Hestia T40: one test that fails on purpose. Never merge.</summary>
public class ProbeFailureTests
{
    [Fact]
    public void ProbeFails() => Assert.Equal(1, 2);
}
