using BepInEx;
using BepInEx.Configuration;

namespace Kommie.ArmorTweaks
{

    [BepInPlugin("kommie.armortweaks", "Armor Tweaks", "1.0.0")]
    public class ArmorTweaksPlugin : BaseUnityPlugin
    {
        private void Awake()
        {
            ConfigEntry<bool> stacking = Config.Bind("Modules", "ArmorStacking", true,
                "Armor Stacking: stacked armor layers (body armor + armored rig, plates + soft armor).");
            ConfigEntry<bool> swap = Config.Bind("Modules", "SoftArmorSwap", true,
                "Soft Armor Swap: removable, swappable soft armor and helmet inserts.");
            ConfigEntry<bool> refurb = Config.Bind("Modules", "ArmorRefurbishment", true,
                "Armor Refurbishment: drag a refurbishment kit onto gear to restore its inserts. " +
                "The server mod has its own switch for this module (see its config.json).");

            kommie.armorstacking.ArmorStackingPlugin.ExtraAbsorption = Config.Bind("ArmorStacking", "ExtraAbsorption", 0f,
                new ConfigDescription(
                    "Extra penetration power each defeated layer absorbs on top of the game's own carry factor (CF). " +
                    "0 = pure vanilla CF. 100 = strongest. factor = CF * (1 - value/100 * R / (R + pen)), where R is the " +
                    "layer's current resistance (armor class + durability) and pen is what that layer was rolled against.",
                    new AcceptableValueRange<float>(0f, 100f)));
            kommie.armorstacking.ArmorStackingPlugin.CarryToShot = Config.Bind("ArmorStacking", "CarryToShot", true,
                "Keep the reduced penetration on the shot after the armor loop. This also lowers the continuing " +
                "bullet's pen and damage (vanilla scales both by pen). Turn off to only change block odds.");
            kommie.armorstacking.ArmorStackingPlugin.DebugLog = Config.Bind("ArmorStacking", "DebugLog", false,
                "Log every armor roll: layer kind, carrier, pen used, stack factor and result.");

            if (stacking.Value) kommie.armorstacking.ArmorStackingPlugin.Init(Logger);
            else Logger.LogInfo("Armor Stacking is switched off in the config.");

            if (swap.Value) Kommie.SoftArmorSwap.SoftArmorSwapPlugin.Init(Logger);
            else Logger.LogInfo("Soft Armor Swap is switched off in the config.");

            if (refurb.Value) Kommie.ArmorRefurbishment.Client.ArmorRefurbishmentPlugin.Init(Logger);
            else Logger.LogInfo("Armor Refurbishment is switched off in the config.");
        }
    }
}
