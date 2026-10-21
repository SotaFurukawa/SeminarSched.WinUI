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

    /// <summary>
    /// ユーザー要望「このプロダクトキーはできればいくつも出せるような仕様にしてほしい」への対応。
    /// 同じ年度・マスターでも、呼び出すたびに異なる（校舎ごとに配っても見分けが付く）キーを
    /// 生成できるよう、検証には関与しない2バイトの乱数「salt」をチェックサムの対象へ加えた
    /// 新形式（8バイト＝16進32桁＝4グループ）にした。saltを明示的に指定しない限り、
    /// 暗号学的乱数で毎回自動生成するため、同じ年度に対して無制限に異なるキーを発行できる。
    /// 既存配布済みの旧形式（6バイト＝16進24桁＝3グループ、saltなし）は<see cref="TryParseKey"/>
    /// 側で従来通り検証できるようにし、互換性を保っている（過去に発行したキーが引き続き使える）。
    /// </summary>
    public static string GenerateKey(int value, ushort? salt = null)
    {
        var resolvedSalt = salt ?? (ushort)RandomNumberGenerator.GetInt32(ushort.MaxValue + 1);
        var valueBytes = BitConverter.GetBytes(Obfuscate(unchecked((uint)value)));
        var saltBytes = BitConverter.GetBytes(resolvedSalt);
        var checksum = ComputeChecksum([.. valueBytes, .. saltBytes]);
        var all = new byte[8];
        valueBytes.CopyTo(all, 0);
        saltBytes.CopyTo(all, 4);
        checksum.CopyTo(all, 6);

        var hex = Convert.ToHexString(all);
        return string.Join('-', [hex[..4], hex[4..8], hex[8..12], hex[12..16]]);
    }

    public static bool TryParseKey(string? rawInput, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(rawInput))
        {
            return false;
        }

        var hex = new string(rawInput.Where(char.IsAsciiHexDigit).ToArray()).ToUpperInvariant();
        // 16進16桁（8バイト、salt付きの新形式）と16進12桁（6バイト、saltの無い旧形式。既に配布
        // 済みのキーはこちら）の両方を受け付ける。それ以外の長さは常に不正。
        if (hex.Length is not (16 or 12))
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
        var checksum = hex.Length == 16 ? all[6..8] : all[4..6];
        var checksumTarget = hex.Length == 16 ? all[..6] : valueBytes;
        if (!checksum.AsSpan().SequenceEqual(ComputeChecksum(checksumTarget)))
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
