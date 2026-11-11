using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Commerce;
using SPTarkov.Server.Core.Services.Modding.Custom;

namespace Kommie.ArmorTweaks.Server;

[Injectable(TypePriority = OnLoadOrder.TraderRegistration + 1)]
public class ArmorRefurbishmentMod(
    ISptLogger<ArmorRefurbishmentMod> logger,
    TemplateTable templates,
    TradersTable traders,
    CustomItemService customItems,
    ModHelper modHelper) : IOnLoad
{

    public const int KitUses = 5;
    private const double SoftKitPrice = 80000;
    private const double HelmetKitPrice = 95000;
    private const int SellerLoyalty = 2;

    private static readonly MongoId[] NoWindowTargets = { BaseClasses.STASH };

    public static readonly MongoId SoftKitId = new("6723a1c4e8b2d90a7c5f1e01");
    public static readonly MongoId HelmetKitId = new("6723a1c4e8b2d90a7c5f1e02");

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        if (!ArmorTweaksConfig.IsEnabled(modHelper, ArmorTweaksConfig.ArmorRefurbishment))
        {
            logger.Info("Armor Tweaks: Armor Refurbishment is switched off in config.json, no kits were added");
            return Task.CompletedTask;
        }

        var donor = templates.Items.Values
            .Where(t => t.Parent == BaseClasses.REPAIR_KITS && t.Properties?.TargetItemFilter != null)
            .FirstOrDefault(t => t.Properties!.TargetItemFilter!.Contains(BaseClasses.ARMOR)
                                 || t.Properties.TargetItemFilter!.Contains(BaseClasses.VEST));
        if (donor == null)
        {
            logger.Warning("ArmorRefurbishment: no armor repair kit found to clone, nothing was added");
            return Task.CompletedTask;
        }

        var ok = true;
        ok &= CreateKit(donor, SoftKitId, "ArmorRefurbishment_SoftKit", "Soft armor refurbishment kit", "Soft refurb kit", SoftKitPrice,
            "Restores soft armor inserts that are repaired to their maximum back to factory condition. " +
            $"{KitUses} uses; one use per insert.",
            NoWindowTargets);
        ok &= CreateKit(donor, HelmetKitId, "ArmorRefurbishment_HelmetKit", "Helmet refurbishment kit", "Helmet refurb kit", HelmetKitPrice,
            "Restores helmet inserts that are repaired to their maximum back to factory condition. " +
            $"{KitUses} uses; one use per insert.",
            NoWindowTargets);
        if (!ok) return Task.CompletedTask;

        var listed = 0;
        listed += Sell(SoftKitId, SoftKitPrice);
        listed += Sell(HelmetKitId, HelmetKitPrice);

        logger.Info($"ArmorRefurbishment: 2 kits added ({listed} trader offers)");
        return Task.CompletedTask;
    }

    private bool CreateKit(TemplateItem donor, MongoId id, string name, string fullName, string shortName, double price,
        string description, MongoId[] targets)
    {
        var handbookParent = templates.Handbook.Items.FirstOrDefault(h => h.Id == donor.Id)?.ParentId;
        if (handbookParent == null) logger.Warning("ArmorRefurbishment: donor kit has no handbook entry, the kits get no handbook price");
        var result = customItems.CreateItemFromClone(new NewItemFromCloneDetails
        {
            ItemTplToClone = donor.Id,
            NewId = id,
            ParentId = BaseClasses.REPAIR_KITS,
            NewItemName = name,
            FleaPriceRoubles = price,
            HandbookPriceRoubles = price,
            HandbookParentId = handbookParent,
            AddToHandbook = handbookParent != null,
            AddToFleaPriceDb = true,
            OverrideProperties = new TemplateItemProperties
            {
                MaxRepairResource = KitUses,
                CanSellOnRagfair = true,
                TargetItemFilter = targets
            },
            Locales = new Dictionary<string, LocaleDetails>
            {
                ["en"] = new LocaleDetails { Name = fullName, ShortName = shortName, Description = description }
            }
        });
        if (!result.Success)
            logger.Error($"ArmorRefurbishment: could not create {name}: {string.Join("; ", result.Errors ?? new List<string>())}");
        return result.Success;
    }

    private int Sell(MongoId kitId, double price)
    {
        var assort = traders.GetTrader(Traders.RAGMAN)?.Assort;
        if (assort?.Items == null || assort.BarterScheme == null || assort.LoyalLevelItems == null) return 0;
        var itemId = new MongoId();
        assort.Items.Add(new Item
        {
            Id = itemId,
            Template = kitId,
            ParentId = "hideout",
            SlotId = "hideout",
            Upd = new Upd { StackObjectsCount = 3, UnlimitedCount = false, RepairKit = new UpdRepairKit { Resource = KitUses } }
        });
        assort.BarterScheme[itemId] = new List<List<BarterScheme>>
        {
            new() { new BarterScheme { Template = Money.ROUBLES, Count = price } }
        };
        assort.LoyalLevelItems[itemId] = SellerLoyalty;
        return 1;
    }
}
