using System.Reflection;
using System.Text.Json;
using SPTarkov.Server.Core.Helpers.Server;

namespace Kommie.ArmorTweaks.Server;

public static class ArmorTweaksConfig
{
    public const string ArmorRefurbishment = "ArmorRefurbishment";
    public const string SoftArmorSwap = "SoftArmorSwap";

    private static Dictionary<string, bool>? _modules;

    public static bool IsEnabled(ModHelper modHelper, string module)
    {
        _modules ??= Load(modHelper);
        return !_modules.TryGetValue(module, out var on) || on;
    }

    private static Dictionary<string, bool> Load(ModHelper modHelper)
    {
        var result = new Dictionary<string, bool>();
        try
        {
            var path = Path.Combine(modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly()), "config.json");
            if (!File.Exists(path))
            {
                File.WriteAllText(path,
                    "{\n  \"Modules\": {\n    \"ArmorRefurbishment\": true,\n    \"SoftArmorSwap\": true\n  }\n}\n");
                return result;
            }
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.TryGetProperty("Modules", out var modules))
                foreach (var entry in modules.EnumerateObject())
                    if (entry.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                        result[entry.Name] = entry.Value.GetBoolean();
        }
        catch
        {

        }
        return result;
    }
}
