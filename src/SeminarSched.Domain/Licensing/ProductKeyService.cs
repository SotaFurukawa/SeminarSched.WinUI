using System.Security.Cryptography;
using System.Text;

namespace SeminarSched.Domain.Licensing;

public readonly record struct ProductKeyValidationResult(bool IsValid, bool IsMaster, int? Year)
{
    public static readonly ProductKeyValidationResult Invalid = new(false, false, null);
    public static readonly ProductKeyValidationResult Master = new(true, true, null);

    public static ProductKeyValidationResult ValidForYear(int year) => new(true, false, year);
}

/// <summary>
/// オフライン専用のプロダクトキー検証。ユーザーの明示判断（「仮に使い回されても仕方ない。正式
/// リリース後にこの辺りを詰める」「リポジトリはpublicにした」）により、鍵となる定数
/// <see cref="Secret"/>がソースごと公開される前提での低保証スキームとして意図的に設計している。
/// 詳細は docs/adr/0006-product-key-licensing.md を参照。
/// </summary>
public static class ProductKeyService
{
    private const string Secret = "ShikiWari-ProductKey-v1";
    private const uint ObfuscationMask = 0x5A3C9F17u;
    public const int MasterKeyValue = 0;

    /// <summary>年度は毎年2/1で切り替わる（1月は前年度のまま）。</summary>
    public static int CurrentPeriodYear(DateTimeOffset now) => now.Month >= 2 ? now.Year : now.Year - 1;

    public static string GenerateKey(int value)
    {
        var valueBytes = BitConverter.GetBytes(Obfuscate(unchecked((uint)value)));
        var checksum = ComputeChecksum(valueBytes);
        var all = new byte[6];
        valueBytes.CopyTo(all, 0);
        checksum.CopyTo(all, 4);

        var hex = Convert.ToHexString(all);
        return string.Join('-', [hex[..4], hex[4..8], hex[8..12]]);
    }

    public static bool TryParseKey(string? rawInput, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(rawInput))
        {
            return false;
        }

        var hex = new string(rawInput.Where(char.IsAsciiHexDigit).ToArray()).ToUpperInvariant();
        if (hex.Length != 12)
        {
            return false;
        }

        byte[] all;
        try
        {
            all = Convert.FromHexString(hex);
        }
        catch (FormatException)
        {
            return false;
        }

        var valueBytes = all[..4];
        var checksum = all[4..6];
        if (!checksum.AsSpan().SequenceEqual(ComputeChecksum(valueBytes)))
        {
            return false;
        }

        value = unchecked((int)Deobfuscate(BitConverter.ToUInt32(valueBytes)));
        return true;
    }

    public static ProductKeyValidationResult Validate(string? rawInput, DateTimeOffset now)
    {
        if (!TryParseKey(rawInput, out var value))
        {
            return ProductKeyValidationResult.Invalid;
        }

        if (value == MasterKeyValue)
        {
            return ProductKeyValidationResult.Master;
        }

        return value == CurrentPeriodYear(now)
            ? ProductKeyValidationResult.ValidForYear(value)
            : ProductKeyValidationResult.Invalid;
    }

    private static uint Obfuscate(uint value) => value ^ ObfuscationMask;

    private static uint Deobfuscate(uint value) => value ^ ObfuscationMask;

    private static byte[] ComputeChecksum(byte[] valueBytes)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Secret));
        return hmac.ComputeHash(valueBytes)[..2];
    }
}
