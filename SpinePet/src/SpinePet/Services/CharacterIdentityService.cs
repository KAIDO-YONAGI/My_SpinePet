using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using SpinePet.Models;

namespace SpinePet.Services;

public sealed class CharacterIdentityService
{
    private const string CharacterNamesResourceName =
        "SpinePet.Data.CharacterNames.json";

    private static readonly Regex ResourceNamePattern = new(
        @"^c(?<character>\d+)(?:_(?<skin>[^_]+))?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly Dictionary<string, string> _characterNames;

    public CharacterIdentityService()
        : this(LoadCharacterNames())
    {
    }

    internal CharacterIdentityService(
        IReadOnlyDictionary<string, string> characterNames)
    {
        _characterNames = new Dictionary<string, string>(
            characterNames,
            StringComparer.OrdinalIgnoreCase);
    }

    public CharacterIdentity Resolve(
        string skeletonPath,
        string? fallbackName = null)
    {
        string resourceStem = Path.GetFileNameWithoutExtension(skeletonPath);
        Match match = ResourceNamePattern.Match(resourceStem);
        if (!match.Success)
        {
            return new CharacterIdentity(
                resourceStem,
                string.Empty,
                string.Empty,
                GetFallbackName(resourceStem, fallbackName, string.Empty));
        }

        string characterCode = match.Groups["character"].Value;
        string skinCode = match.Groups["skin"].Value;
        string resourceName = match.Value;
        string displayName = TryGetCharacterName(
            characterCode,
            out string mappedName)
                ? mappedName
                : GetFallbackName(resourceStem, fallbackName, skinCode);

        return new CharacterIdentity(
            resourceName,
            characterCode,
            skinCode,
            displayName);
    }

    public bool TryGetCharacterName(
        string characterCode,
        out string displayName)
    {
        if (!string.IsNullOrWhiteSpace(characterCode) &&
            _characterNames.TryGetValue(
                characterCode.Trim(),
                out string? mappedName) &&
            !string.IsNullOrWhiteSpace(mappedName))
        {
            displayName = mappedName;
            return true;
        }

        displayName = string.Empty;
        return false;
    }

    private static string GetFallbackName(
        string resourceStem,
        string? fallbackName,
        string skinCode)
    {
        string candidate = string.IsNullOrWhiteSpace(fallbackName)
            ? resourceStem
            : fallbackName.Trim();

        if (!string.IsNullOrWhiteSpace(skinCode))
        {
            string skinSuffix = $"_{skinCode}";
            if (candidate.EndsWith(
                skinSuffix,
                StringComparison.OrdinalIgnoreCase))
            {
                candidate = candidate[..^skinSuffix.Length].TrimEnd();
            }
        }

        return ResourceNamePattern.IsMatch(candidate) ||
            string.IsNullOrWhiteSpace(candidate)
            ? resourceStem
            : candidate;
    }

    private static Dictionary<string, string> LoadCharacterNames()
    {
        Assembly assembly = typeof(CharacterIdentityService).Assembly;
        using Stream stream =
            assembly.GetManifestResourceStream(CharacterNamesResourceName) ??
            throw new InvalidOperationException(
                $"Missing embedded resource '{CharacterNamesResourceName}'.");

        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ??
            new Dictionary<string, string>();
    }
}
