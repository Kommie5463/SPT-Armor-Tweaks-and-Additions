using SPTarkov.Server.Core.Models.Spt.Mod;

namespace Kommie.ArmorTweaks.Server;

public record ModMetadata : IModMetadata
{
    public string ModGuid { get; init; } = "kommie.armortweaks";
    public string Name { get; init; } = "Armor Tweaks";
    public string Author { get; init; } = "Kommie";
    public List<string>? Contributors { get; init; }
    public SemanticVersioning.Version Version { get; init; } = new("1.0.0");
    public SemanticVersioning.Range SptVersion { get; init; } = new("~4.1.0");
    public bool HasPrepatcher { get; init; }
    public List<string>? Incompatibilities { get; init; }
    public Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public string? Url { get; init; }
    public string License { get; init; } = "MIT";
}
