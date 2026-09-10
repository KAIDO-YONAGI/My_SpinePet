using System.Text.Json;
using SpinePet.Services;

JsonSerializerOptions jsonOptions = new() { WriteIndented = true };
CharacterCatalogBattleImportService importer = new();
if (args.Length >= 3 &&
    string.Equals(args[0], "--audit", StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine(JsonSerializer.Serialize(
        importer.Audit(args[1], args[2..]),
        jsonOptions));
    return 0;
}

if (args.Length >= 4)
{
    Console.WriteLine(JsonSerializer.Serialize(
        importer.Import(args[0], args[1], args[2..]),
        jsonOptions));
    return 0;
}

Console.Error.WriteLine(
    "Usage: BattleCatalogImporter --audit " +
    "<resources/Characters> <resource-directory> [<resource-directory> ...]");
Console.Error.WriteLine(
    "   or: BattleCatalogImporter " +
    "<resources/Characters> <SpinePet/res> " +
    "<resource-directory> [<resource-directory> ...]");
Console.Error.WriteLine(
    "The resource directory list is required; full-catalog import is disabled.");
Console.Error.WriteLine(
    "See IMPORT.md (sections 1.2 and 3.1) for the audit/import workflow and " +
    "the expected JSON fields.");
return 2;
