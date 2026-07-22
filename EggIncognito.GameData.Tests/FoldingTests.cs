namespace EggIncognito.GameData.Tests;

public sealed class FoldingTests {
    [Fact]
    public void Add_sums_contributions_onto_seed() => Assert.Equal(17, Folding.Fold(CombineMode.Add, 0, [2, 5, 10]));

    [Fact]
    public void Mul_multiplies_contributions() => Assert.Equal(1000, Folding.Fold(CombineMode.Mul, 1, [10, 100]));

    [Fact]
    public void MulPlusOne_multiplies_one_plus_each() => Assert.Equal(1.1 * 1.2, Folding.Fold(CombineMode.MulPlusOne, 1, [0.1, 0.2]), 10);

    [Fact]
    public void Beacons_stack_additively_not_multiplicatively() {
        var beacons = Folding.Fold(CombineMode.Add, 1, [2 - 1, 5 - 1]);
        Assert.Equal(6, beacons);
    }
}
