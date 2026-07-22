using Svc = EggIncognito.Services;

namespace EggIncognito.Tests;

public class ClassifyAutoWriteTests {
    private static Svc.AutoWriteVerdict Classify(int b, int s) => Svc.ExtractorConfig.ClassifyAutoWrite(b, s);

    [Fact]
    public void NonExactWinner_Rejected() =>
        Assert.Equal(Svc.AutoWriteVerdict.Reject, Classify(999, 50));

    [Fact]
    public void SoleExact_Written() =>
        Assert.Equal(Svc.AutoWriteVerdict.Write, Classify(1053, 3));

    [Fact]
    public void ExactWithFieldLead_Written() {
        Assert.Equal(Svc.AutoWriteVerdict.Write, Classify(1053, 1003));
        Assert.Equal(Svc.AutoWriteVerdict.Write, Classify(1018, 1005));
    }

    [Fact]
    public void ExactTie_Flagged() =>
        Assert.Equal(Svc.AutoWriteVerdict.Flag, Classify(1010, 1010));
}
