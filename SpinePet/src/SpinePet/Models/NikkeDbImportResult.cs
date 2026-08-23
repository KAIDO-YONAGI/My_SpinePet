namespace SpinePet.Models;

public sealed record NikkeDbImportResult(
    string ResourceId,
    string DestinationDirectory,
    CharacterResourceFiles Standing,
    bool BattleImported);
