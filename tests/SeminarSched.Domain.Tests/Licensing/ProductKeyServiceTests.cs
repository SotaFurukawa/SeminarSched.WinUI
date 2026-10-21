using SeminarSched.Domain.Licensing;

namespace SeminarSched.Domain.Tests.Licensing;

public sealed class ProductKeyServiceTests
{
    [Theory]
    [InlineData(2026, 1, 31, 2025)]
    [InlineData(2026, 2, 1, 2026)]
    [InlineData(2026, 10, 2, 2026)]
    [InlineData(2027, 1, 31, 2026)]
    public void CurrentPeriodYear_RollsOverOnFebruaryFirst(int year, int month, int day, int expectedPeriodYear)
    {
        var now = new DateTimeOffset(year, month, day, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(expectedPeriodYear, ProductKeyService.CurrentPeriodYear(now));
    }

    [Fact]
    public void GenerateKey_ThenValidate_IsValidForMatchingPeriodYear()
    {
        var now = new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero);
        var key = ProductKeyService.GenerateKey(2026);

        var result = ProductKeyService.Validate(key, now);

        Assert.True(result.IsValid);
        Assert.False(result.IsMaster);
        Assert.Equal(2026, result.Year);
    }

    [Fact]
    public void Validate_RejectsKeyGeneratedForADifferentPeriodYear()
    {
        var now = new DateTimeOffset(2027, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var key = ProductKeyService.GenerateKey(2026);

        var result = ProductKeyService.Validate(key, now);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_MasterKey_IsAlwaysValidRegardlessOfDate()
    {
        var masterKey = ProductKeyService.GenerateKey(ProductKeyService.MasterKeyValue);

        var past = ProductKeyService.Validate(masterKey, new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var future = ProductKeyService.Validate(masterKey, new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.True(past.IsValid);
        Assert.True(past.IsMaster);
        Assert.True(future.IsValid);
        Assert.True(future.IsMaster);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-key")]
    [InlineData("AAAA-AAAA-AAAA")]
    public void Validate_RejectsMalformedInput(string input)
    {
        var result = ProductKeyService.Validate(input, DateTimeOffset.Now);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_RejectsTamperedKey()
    {
        var now = new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero);
        var key = ProductKeyService.GenerateKey(2026);
        var tampered = string.Concat("9", key.AsSpan(1));

        var result = ProductKeyService.Validate(tampered, now);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void TryParseKey_IgnoresHyphensAndCase()
    {
        var key = ProductKeyService.GenerateKey(2026);
        var reformatted = key.Replace("-", "").ToLowerInvariant();

        Assert.True(ProductKeyService.TryParseKey(reformatted, out var value));
        Assert.Equal(2026, value);
    }

    // ユーザー要望「このプロダクトキーはできればいくつも出せるような仕様にしてほしい」への対応。
    // 同じ年度でも、saltを変えれば異なる文字列のキーを発行できること、かつどちらも同じ年度として
    // 正しく検証できることを確認する。
    [Fact]
    public void GenerateKey_WithDifferentSalts_ProducesDistinctKeysThatBothValidateForSameYear()
    {
        var now = new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero);
        var first = ProductKeyService.GenerateKey(2026, salt: 1);
        var second = ProductKeyService.GenerateKey(2026, salt: 2);

        Assert.NotEqual(first, second);
        Assert.True(ProductKeyService.Validate(first, now).IsValid);
        Assert.True(ProductKeyService.Validate(second, now).IsValid);
        Assert.Equal(2026, ProductKeyService.Validate(first, now).Year);
        Assert.Equal(2026, ProductKeyService.Validate(second, now).Year);
    }

    // 互換性の確認。saltの仕組みを追加する前（本セッション中に実際に配布目的で生成した2026年版
    // キー）が、仕様変更後も引き続き有効であること（既に配布済みのキーを失効させない）を確認する。
    [Fact]
    public void Validate_AcceptsPreviouslyIssuedLegacyFormatKey()
    {
        var now = new DateTimeOffset(2026, 10, 21, 0, 0, 0, TimeSpan.Zero);

        var result = ProductKeyService.Validate("FD98-3C5A-E685", now);

        Assert.True(result.IsValid);
        Assert.False(result.IsMaster);
        Assert.Equal(2026, result.Year);
    }
}
