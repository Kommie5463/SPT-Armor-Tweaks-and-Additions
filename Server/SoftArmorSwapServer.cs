using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Utils.Json;

namespace Kommie.ArmorTweaks.Server;

[Injectable(TypePriority = OnLoadOrder.TraderRegistration + 1)]
public class SoftArmorSwapServer(
    ISptLogger<SoftArmorSwapServer> logger,
    TemplateTable templates,
    LocaleTable locales,
    ModHelper modHelper) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        if (!ArmorTweaksConfig.IsEnabled(modHelper, ArmorTweaksConfig.SoftArmorSwap))
        {
            logger.Info("Armor Tweaks: Soft Armor Swap is switched off in config.json");
            return Task.CompletedTask;
        }

        var inserts = templates.Items.Values
            .Where(t => t.Parent == BaseClasses.BUILT_IN_INSERTS && t.Properties?.ArmorClass is > 0)
            .ToList();
        NameInserts(inserts);
        logger.Info($"Armor Tweaks (Soft Armor Swap): {inserts.Count} inserts named");
        return Task.CompletedTask;
    }

    private void NameInserts(List<TemplateItem> inserts)
    {
        var texts = inserts.ToDictionary(i => i.Id.ToString(), Describe);
        foreach (var language in locales.Languages.Keys)
        {
            if (!locales.Global.TryGetValue(language, out var lazy)) continue;
            lazy.AddTransformer(dictionary =>
            {
                foreach (var (id, text) in texts)
                {
                    if (string.IsNullOrWhiteSpace(dictionary.GetValueOrDefault(id + " Name")))
                        dictionary[id + " Name"] = text.Name;
                    if (string.IsNullOrWhiteSpace(dictionary.GetValueOrDefault(id + " ShortName")))
                        dictionary[id + " ShortName"] = text.Short;
                    if (string.IsNullOrWhiteSpace(dictionary.GetValueOrDefault(id + " Description")))
                        dictionary[id + " Description"] = text.Description;
                }
                return dictionary;
            });
        }
    }

    private static (string Name, string Short, string Description) Describe(TemplateItem insert)
    {
        var props = insert.Properties!;
        var labels = new List<string>();
        var helmet = false;
        var colliders = props.ArmorColliders?.ToHashSet() ?? new HashSet<string>();
        var plates = props.ArmorPlateColliders?.ToHashSet() ?? new HashSet<string>();

        void Pair(string up, string low, string both, string upLabel, string lowLabel)
        {
            var u = colliders.Contains(up);
            var l = colliders.Contains(low);
            if (u && l) labels.Add(both);
            else if (u) labels.Add(upLabel);
            else if (l) labels.Add(lowLabel);
        }

        Pair("RibcageUp", "RibcageLow", "Chest", "Upper chest", "Lower chest");
        Pair("SpineTop", "SpineDown", "Back", "Upper back", "Lower back");
        if (colliders.Contains("LeftSideChestUp") || colliders.Contains("LeftSideChestDown")) labels.Add("Left side");
        if (colliders.Contains("RightSideChestUp") || colliders.Contains("RightSideChestDown")) labels.Add("Right side");

        var named = new (string Collider, string Label, bool Helmet)[]
        {
            ("ParietalHead", "crown", true), ("BackHead", "nape", true), ("Ears", "ear", true),
            ("Jaw", "jaw", true), ("Eyes", "face", true),
            ("NeckFront", "Throat", false), ("NeckBack", "Neck", false),
            ("Pelvis", "Front groin", false), ("PelvisBack", "Back groin", false),
            ("LeftUpperArm", "Left shoulder", false), ("RightUpperArm", "Right shoulder", false)
        };
        var handled = new HashSet<string>
        {
            "RibcageUp", "RibcageLow", "SpineTop", "SpineDown", "LeftSideChestUp", "LeftSideChestDown",
            "RightSideChestUp", "RightSideChestDown", "HeadCommon"
        };
        foreach (var (collider, label, isHelmet) in named)
        {
            handled.Add(collider);
            if (!colliders.Contains(collider)) continue;
            labels.Add(label);
            if (isHelmet) helmet = true;
        }
        foreach (var other in colliders.Where(c => !handled.Contains(c)))
            labels.Add(System.Text.RegularExpressions.Regex.Replace(other, "(?<=[a-z0-9])(?=[A-Z])", " "));

        if (labels.Count == 0)
        {
            if (plates.Any(p => p.EndsWith("_chest"))) labels.Add("Front plate area");
            if (plates.Any(p => p.EndsWith("_back"))) labels.Add("Back plate area");
            if (plates.Any(p => p.Contains("_side_left"))) labels.Add("Left side");
            if (plates.Any(p => p.Contains("_side_right"))) labels.Add("Right side");
        }

        var zone = labels.Count == 0 ? "" : labels.Count > 3 ? "Wide coverage" : string.Join(" + ", labels);
        var cls = props.ArmorClass ?? 0;
        var material = props.ArmorMaterial?.ToString();
        var detail = material == null ? $"class {cls}" : $"class {cls}, {material}";

        string name, shortName;
        if (helmet)
        {
            var area = zone.ToLowerInvariant();
            name = $"Helmet {area} insert ({detail})";
            shortName = $"Helmet {area} C{cls}";
        }
        else if (zone.Length > 0)
        {
            name = $"{zone} soft armor insert ({detail})";
            shortName = $"{zone} C{cls}";
        }
        else
        {
            name = $"Soft armor insert ({detail})";
            shortName = $"Insert C{cls}";
        }

        var type = string.IsNullOrEmpty(props.ArmorType) ? "" : $" {props.ArmorType.ToLowerInvariant()} armor,";
        var description = $"A removable {(helmet ? "helmet" : "soft armor")} insert for the {(zone.Length > 0 ? zone.ToLowerInvariant() : "protected")} area.{type} {detail}.";
        return (name, shortName, description);
    }
}
