using SeminarSched.Optimization.Profiles;

namespace SeminarSched.Optimization.Tests;

public sealed class OptimizationProfileCatalogTests
{
    [Fact]
    public void Catalog_ContainsFiveSnappedLevels_WithStandardDefault()
    {
        Assert.Equal(5, OptimizationProfileCatalog.All.Count);
        Assert.Equal(OptimizationQualityLevel.Standard, OptimizationProfileCatalog.DefaultLevel);
        Assert.Equal(
            [1, 2, 3, 4, 5],
            OptimizationProfileCatalog.All.Select(profile => (int)profile.Level).Order().ToArray());
    }

    [Fact]
    public void Profiles_IncreaseMaximumDurationAndStrategyBreadth()
    {
        var ordered = OptimizationProfileCatalog.All.OrderBy(profile => profile.Level).ToArray();

        // Fast/Fasterは実データ検証（生徒57名・必要回数計458件）で、複数戦略への均等分割後に
        // 1戦略あたりの持ち時間が単一戦略時代のcold-start所要時間（約21〜31秒）を下回り、
        // 全戦略が失敗する退行を確認したため、総時間を底上げしてある（OptimizationProfileCatalog参照）。
        Assert.Equal([120, 240, 600, 1800, 3600], ordered.Select(item => (int)item.MaximumDuration.TotalSeconds));
        Assert.True(ordered.Zip(ordered.Skip(1)).All(pair => pair.First.StrategyCount <= pair.Second.StrategyCount));
    }

    [Fact]
    public void HighProfiles_UseTournamentAndRepair()
    {
        var high = OptimizationProfileCatalog.Get(OptimizationQualityLevel.High);
        var highest = OptimizationProfileCatalog.Get(OptimizationQualityLevel.Highest);

        Assert.True(high.UsesTournament);
        Assert.True(high.UsesHints);
        Assert.True(high.UsesNeighborhoodRepair);
        Assert.False(high.UsesFinalPolishing);
        Assert.True(highest.UsesFinalPolishing);
        Assert.Equal(TimeSpan.FromMinutes(10), highest.StagnationTimeout);
    }

    [Fact]
    public void EveryProfile_AllocatesEntireBudget()
    {
        foreach (var profile in OptimizationProfileCatalog.All)
        {
            Assert.Equal(1.0, profile.Stages.Sum(stage => stage.BudgetShare), precision: 4);
        }
    }
}
