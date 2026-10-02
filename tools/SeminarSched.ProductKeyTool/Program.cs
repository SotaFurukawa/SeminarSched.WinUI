using SeminarSched.Domain.Licensing;

if (args.Length != 1)
{
    Console.WriteLine("使い方: dotnet run --project tools/SeminarSched.ProductKeyTool -- <年度 | master>");
    Console.WriteLine("例:   dotnet run --project tools/SeminarSched.ProductKeyTool -- 2027");
    Console.WriteLine("例:   dotnet run --project tools/SeminarSched.ProductKeyTool -- master");
    return 1;
}

int value;
if (string.Equals(args[0], "master", StringComparison.OrdinalIgnoreCase))
{
    value = ProductKeyService.MasterKeyValue;
}
else if (!int.TryParse(args[0], out value))
{
    Console.WriteLine($"'{args[0]}' は年度（整数）または 'master' のいずれでもありません。");
    return 1;
}

var key = ProductKeyService.GenerateKey(value);
var label = value == ProductKeyService.MasterKeyValue ? "マスターキー（完全版・無期限）" : $"{value}年版（2/1〜翌1/31まで有効）";
Console.WriteLine($"{label}: {key}");
return 0;
