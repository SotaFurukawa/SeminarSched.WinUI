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

        Assert.Equal([60, 180, 600, 1800, 3600], ordered.Select(item => (int)item.MaximumDuration.TotalSeconds));
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
