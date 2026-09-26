using SeminarSched.Optimization.Diagnostics;

namespace SeminarSched.Optimization.Tests;

public sealed class HardwareBenchmarkTests
{
    // ユーザー要望（checkpoint108）「品質プロファイルのリバランス...スコアの閾値を設定することで、
    // スコアを上回っていたら多めの負荷、下回っていたらあまり負荷はかけないようにする...こちらも
    // 5段階くらい用意しておいてください」を検証する。Classifyは実際にCP-SATを動かさない純粋な
    // 分類ロジックのため、境界値を含め高速に検証できる。
    [Theory]
    [InlineData(0.1, HardwareTier.VeryHigh)]
    [InlineData(0.75, HardwareTier.VeryHigh)]
    [InlineData(0.76, HardwareTier.High)]
    [InlineData(1.5, HardwareTier.High)]
    [InlineData(1.51, HardwareTier.Standard)]
    [InlineData(3.0, HardwareTier.Standard)]
    [InlineData(3.01, HardwareTier.Low)]
    [InlineData(6.0, HardwareTier.Low)]
    [InlineData(6.01, HardwareTier.VeryLow)]
    [InlineData(19.9, HardwareTier.VeryLow)]
    public void Classify_ReachedOptimal_UsesElapsedTimeThresholds(double elapsedSeconds, HardwareTier expected)
    {
        var result = HardwareBenchmark.Classify(TimeSpan.FromSeconds(elapsedSeconds), reachedOptimal: true);

        Assert.Equal(expected, result.Tier);
        Assert.True(result.ReachedOptimal);
    }

    // 制限時間内にOptimal（Feasible含む）へ一切到達できなかった場合は、経過時間の値に関わらず
    // 最も非力なtierとして扱う（＝それだけ非力、または問題が解けなかったとみなす）。
    [Fact]
    public void Classify_DidNotReachOptimal_IsAlwaysVeryLow()
    {
        var result = HardwareBenchmark.Classify(TimeSpan.FromSeconds(0.1), reachedOptimal: false);

        Assert.Equal(HardwareTier.VeryLow, result.Tier);
        Assert.False(result.ReachedOptimal);
    }

    // 固定・決定的な合成問題を実際にCP-SATで解かせ、この開発機（16論理プロセッサ）では数秒以内に
    // Optimalへ到達することを確認する回帰テスト（実測ではおよそ1秒前後）。TimeLimit（20秒）よりは
    // 十分速く終わるはずで、極端な低速化や無限ループへの回帰を検知できる。
    [Fact]
    public async Task RunAsync_SolvesTheSyntheticProblemWithinTheTimeLimit()
    {
        var result = await HardwareBenchmark.RunAsync();

        Assert.True(result.ReachedOptimal, $"Expected the synthetic benchmark problem to reach Optimal within {HardwareBenchmark.TimeLimit}, but it did not (elapsed={result.Elapsed}).");
        Assert.True(result.Elapsed < HardwareBenchmark.TimeLimit);
    }
}
