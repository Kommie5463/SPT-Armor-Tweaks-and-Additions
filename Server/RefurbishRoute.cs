using System.Text.Json.Serialization;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Utils;

namespace Kommie.ArmorTweaks.Server;

public record RefurbishRequest : IRequestData
{
    [JsonPropertyName("kitId")] public MongoId KitId { get; init; }
    [JsonPropertyName("targetId")] public MongoId TargetId { get; init; }
}

public class PieceResult
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("durability")] public double Durability { get; set; }
    [JsonPropertyName("maxDurability")] public double MaxDurability { get; set; }
}

public class RefurbishResult
{
    [JsonPropertyName("ok")] public bool Ok { get; set; }
    [JsonPropertyName("message")] public string Message { get; set; } = "";
    [JsonPropertyName("pieces")] public List<PieceResult> Pieces { get; set; } = new();
    [JsonPropertyName("kitResource")] public double KitResource { get; set; }
}

[Injectable]
public class RefurbishRoute : StaticRouter
{
    public const string Url = "/armorrefurbishment/apply";

    public RefurbishRoute(JsonUtil json, RefurbishService service, HttpResponseUtil responses) : base(json, new[]
    {
        new RouteAction<RefurbishRequest>(Url, (url, request, sessionId, info, ct) =>
            ValueTask.FromResult(responses.GetBody(service.Apply(sessionId, request))))
    }) { }
}

[Injectable]
public class RefurbishService(TemplateTable templates, ProfileHelper profiles, RepairConfig repairConfig)
{
    private const double Epsilon = 0.01;

    public RefurbishResult Apply(MongoId sessionId, RefurbishRequest request)
    {
        var result = new RefurbishResult();
        var pmc = profiles.GetPmcProfile(sessionId);
        var items = pmc?.Inventory?.Items;
        var db = templates.Items;
        if (items == null) return Fail(result, "No inventory found");

        var kit = items.FirstOrDefault(i => i.Id == request.KitId);
        var target = items.FirstOrDefault(i => i.Id == request.TargetId);
        if (kit == null || target == null) return Fail(result, "Kit or target not found");
        var helmetKit = kit.Template == ArmorRefurbishmentMod.HelmetKitId;
        if (!helmetKit && kit.Template != ArmorRefurbishmentMod.SoftKitId) return Fail(result, "Not a refurbishment kit");

        var maxUses = (double)(db[kit.Template].Properties?.MaxRepairResource ?? ArmorRefurbishmentMod.KitUses);
        kit.Upd ??= new Upd();
        kit.Upd.RepairKit ??= new UpdRepairKit { Resource = maxUses };
        var uses = kit.Upd.RepairKit.Resource ?? maxUses;
        if (uses < 1) return Fail(result, "The kit is used up");

        if (IsHelmetGear(items, db, target) != helmetKit)
            return Fail(result, helmetKit ? "The helmet kit only works on helmets" : "The soft armor kit does not work on helmets");

        foreach (var piece in Candidates(items, db, target))
        {
            if (uses < 1) break;
            var restored = Refurbish(piece, db[piece.Template]);
            if (restored <= 0) continue;
            var skill = db[piece.Template].Properties?.ArmorType == "Heavy" ? SkillTypes.HeavyVests : SkillTypes.LightVests;
            profiles.AddSkillPointsToPlayer(pmc!, skill, restored * repairConfig.ArmorKitSkillPointGainPerRepairPointMultiplier, true);
            result.Pieces.Add(new PieceResult
            {
                Id = piece.Id.ToString(),
                Durability = piece.Upd!.Repairable!.Durability!.Value,
                MaxDurability = piece.Upd.Repairable.MaxDurability!.Value
            });
            uses -= 1;
        }

        if (result.Pieces.Count == 0)
            return Fail(result, "Nothing to refurbish: inserts must be repaired to their maximum first");

        kit.Upd.RepairKit.Resource = uses;
        result.Ok = true;
        result.KitResource = uses;
        result.Message = $"Refurbished {result.Pieces.Count} piece(s), {uses} use(s) left";
        return result;
    }

    private static RefurbishResult Fail(RefurbishResult r, string message)
    {
        r.Ok = false;
        r.Message = message;
        return r;
    }

    private static IEnumerable<Item> Candidates(List<Item> items, Dictionary<MongoId, TemplateItem> db, Item target)
    {
        if (IsInsert(db, target.Template)) { yield return target; yield break; }
        if (db.TryGetValue(target.Template, out var shell) && shell.Properties?.ArmorClass is > 0)
            yield return target;

        var queue = new Queue<string>();
        queue.Enqueue(target.Id.ToString());
        while (queue.Count > 0)
        {
            var parent = queue.Dequeue();
            foreach (var child in items.Where(i => i.ParentId == parent))
            {
                if (IsInsert(db, child.Template)) yield return child;
                else queue.Enqueue(child.Id.ToString());
            }
        }
    }

    private static bool IsInsert(Dictionary<MongoId, TemplateItem> db, MongoId tpl) =>
        db.TryGetValue(tpl, out var t) && t.Parent == BaseClasses.BUILT_IN_INSERTS && t.Properties?.ArmorClass is > 0;

    private static bool IsHelmetGear(List<Item> items, Dictionary<MongoId, TemplateItem> db, Item piece)
    {
        var current = piece;
        for (var i = 0; i < 8 && current != null; i++)
        {
            if (db.TryGetValue(current.Template, out var t) && t.Parent == BaseClasses.HEADWEAR) return true;
            var parentId = current.ParentId;
            current = items.FirstOrDefault(x => x.Id.ToString() == parentId);
        }
        return false;
    }

    private static double Refurbish(Item piece, TemplateItem template)
    {
        var factory = template.Properties?.MaxDurability;
        var rep = piece.Upd?.Repairable;
        if (factory == null || rep?.MaxDurability == null || rep.Durability == null) return 0;
        if (rep.MaxDurability >= factory - Epsilon) return 0;
        if (rep.Durability < rep.MaxDurability - Epsilon) return 0;
        var restored = factory.Value - rep.MaxDurability.Value;
        rep.MaxDurability = factory;
        rep.Durability = factory;
        return restored;
    }
}
