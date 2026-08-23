using System.Text.Json;
using SpinePet.Services;

CharacterCatalogBattleImportService importer = new();
JsonSerializerOptions jsonOptions = new() { WriteIndented = true };
if (args.Length == 2 &&
    string.Equals(args[0], "--audit", StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine(JsonSerializer.Serialize(
        importer.Audit(args[1]),
        jsonOptions));
    return 0;
}

if (args.Length == 2)
{
    Console.WriteLine(JsonSerializer.Serialize(
        importer.Import(args[0], args[1]),
        jsonOptions));
    return 0;
}

Console.Error.WriteLine(
    "Usage: BattleCatalogImporter [--audit] " +
    "<resources/Characters> [SpinePet/res]");
return 2;
