using UnityEngine;
using UnityEditor;
using System.IO;

/// <summary>
/// Creates all RunUpgradeDefinition .asset files for MVP upgrades.
/// Run via menu: ZombieGame / Create MVP Upgrade Assets
/// </summary>
public static class UpgradeAssetCreator
{
    private const string OutputPath = "Assets/Resources/Upgrades";

    [MenuItem("ZombieGame/Create MVP Upgrade Assets")]
    public static void CreateAllUpgrades()
    {
        if (!Directory.Exists(OutputPath))
            Directory.CreateDirectory(OutputPath);

        // ── HELICOPTER ──────────────────────────────────────────────────────

        Create("upgrade_heli_speed",
            id: "helicopter_quicklanding",
            cardType: CardManager.CardType.Helicopter,
            effect: UpgradeEffectType.Helicopter_SpeedMult,
            name1: "Quick Landing",          name2: "Afterburner",              name3: "VTOL Protocol",
            desc1: "+35% descent speed",
            desc2: "Speed x2.5, zombies don't trigger panic for 3 sec after landing",
            desc3: "Helicopter lands instantly",
            v1: 0.35f, v2: 1.5f, v3: 0f,
            accent: new Color(0.3f, 0.7f, 1f));

        Create("upgrade_heli_capacity",
            id: "helicopter_capacity",
            cardType: CardManager.CardType.Helicopter,
            effect: UpgradeEffectType.Helicopter_CapacityAdd,
            name1: "Extra Seats",            name2: "Heavy Transport",           name3: "Mass Evacuation",
            desc1: "+2 capacity",
            desc2: "+6 capacity total",
            desc3: "Unlimited capacity — all civilians in range",
            v1: 2f, v2: 4f, v3: 0f,
            accent: new Color(0.4f, 0.9f, 0.4f));

        Create("upgrade_heli_attract",
            id: "helicopter_megaphone",
            cardType: CardManager.CardType.Helicopter,
            effect: UpgradeEffectType.Helicopter_RadiusMult,
            name1: "Megaphone",             name2: "Emergency Broadcast",       name3: "City Alert",
            desc1: "+40% attract radius",
            desc2: "Radius x2, civilians run faster",
            desc3: "ALL civilians on the map move to the helicopter",
            v1: 0.40f, v2: 1.0f, v3: 0f,
            accent: new Color(1f, 0.85f, 0.3f));

        // ── SNIPER ───────────────────────────────────────────────────────

        Create("upgrade_sniper_range",
            id: "sniper_range",
            cardType: CardManager.CardType.Sniper,
            effect: UpgradeEffectType.Sniper_RangeMult,
            name1: "Extended Barrel",       name2: "Long-Range Platform",       name3: "All-Seeing Eye",
            desc1: "+35% shooting range",
            desc2: "+65% shooting range",
            desc3: "Sniper hits across the entire map",
            v1: 0.35f, v2: 0.65f, v3: 0f,
            accent: new Color(0.7f, 0.5f, 0.2f));

        Create("upgrade_sniper_damage",
            id: "sniper_damage",
            cardType: CardManager.CardType.Sniper,
            effect: UpgradeEffectType.Sniper_DamageMult,
            name1: "Armor-Piercing Rounds", name2: "Executioner",               name3: "Lead Storm",
            desc1: "+50% damage",
            desc2: "+100% damage",
            desc3: "Instantly kills non-bosses",
            v1: 0.5f, v2: 1.0f, v3: 0f,
            accent: new Color(0.9f, 0.3f, 0.2f));

        Create("upgrade_sniper_duration",
            id: "sniper_duration",
            cardType: CardManager.CardType.Sniper,
            effect: UpgradeEffectType.Sniper_DurationAdd,
            name1: "Long Watch",            name2: "Overwatch",                 name3: "Eternal Sentinel",
            desc1: "+8 sec duration",
            desc2: "Sniper never disappears",
            desc3: "Sniper never disappears",
            v1: 8f, v2: 0f, v3: 0f,
            accent: new Color(0.5f, 0.5f, 0.8f));

        // ── BOMB ─────────────────────────────────────────────────────────

        Create("upgrade_bomb_radius",
            id: "bomb_radius",
            cardType: CardManager.CardType.Bomb,
            effect: UpgradeEffectType.Bomb_RadiusMult,
            name1: "Enlarged Warhead",      name2: "Tactical Core",             name3: "Thermobaric",
            desc1: "+40% blast radius",
            desc2: "+200% blast radius",
            desc3: "Blast radius covers half the map",
            v1: 0.4f, v2: 2.0f, v3: 0f,
            accent: new Color(1f, 0.5f, 0.1f));

        Create("upgrade_bomb_damage",
            id: "bomb_damage",
            cardType: CardManager.CardType.Bomb,
            effect: UpgradeEffectType.Bomb_DamageMult,
            name1: "Shockwave",             name2: "Overkill",                  name3: "EMP Blast",
            desc1: "+100% damage",
            desc2: "+900% damage",
            desc3: "Stuns surviving zombies for 5 sec.",
            v1: 1.0f, v2: 9.0f, v3: 0f,
            accent: new Color(0.9f, 0.2f, 0.1f));

        Create("upgrade_bomb_cluster",
            id: "bomb_cluster",
            cardType: CardManager.CardType.Bomb,
            effect: UpgradeEffectType.Bomb_ClusterCount,
            name1: "Cluster Payload",       name2: "Carpet Bombing",            name3: "Swarm Strike",
            desc1: "+3 mini-explosions around the point",
            desc2: "+5 mini-explosions around the point",
            desc3: "+12 mini-explosions around the point",
            v1: 3f, v2: 5f, v3: 12f,
            accent: new Color(1f, 0.7f, 0.0f));

        // ── BARRICADE ─────────────────────────────────────────────────────

        Create("upgrade_barricade_hp",
            id: "barricade_hp",
            cardType: CardManager.CardType.Barricade,
            effect: UpgradeEffectType.Barricade_HPMult,
            name1: "Reinforced Concrete",   name2: "Fortress Wall",             name3: "Indestructible Bunker",
            desc1: "+50% barricade HP",
            desc2: "+300% barricade HP",
            desc3: "Barricade is indestructible",
            v1: 0.5f, v2: 3.0f, v3: 0f,
            accent: new Color(0.6f, 0.4f, 0.2f));

        Create("upgrade_barricade_spike",
            id: "barricade_spike",
            cardType: CardManager.CardType.Barricade,
            effect: UpgradeEffectType.Barricade_ReflectDamage,
            name1: "Barbed Wire",           name2: "Razor Wire",                name3: "Death Zone",
            desc1: "25% damage reflected to attacking zombies",
            desc2: "75% damage reflected to attacking zombies",
            desc3: "Zombies near the barricade are instantly killed",
            v1: 0.25f, v2: 0.75f, v3: 0f,
            accent: new Color(0.8f, 0.1f, 0.1f));

        Create("upgrade_barricade_count",
            id: "barricade_count",
            cardType: CardManager.CardType.Barricade,
            effect: UpgradeEffectType.Barricade_CountAdd,
            name1: "Rapid Deployment",      name2: "Minefield",                 name3: "Perimeter",
            desc1: "+1 barricade per placement",
            desc2: "+2 barricades per placement",
            desc3: "+3 barricades per placement",
            v1: 1f, v2: 1f, v3: 1f,
            accent: new Color(0.5f, 0.7f, 0.3f));

        // ── GENERAL ─────────────────────────────────────────────────────────

        Create("upgrade_general_xp",
            id: "general_xp",
            cardType: CardManager.CardType.None,
            effect: UpgradeEffectType.General_XPMult,
            name1: "Accelerated Learning",  name2: "Accelerated Learning II",   name3: "Accelerated Learning III",
            desc1: "+25% XP gained",
            desc2: "+60% XP gained",
            desc3: "+100% XP gained",
            v1: 0.25f, v2: 0.35f, v3: 0.40f,
            accent: new Color(0.4f, 0.8f, 1f));

        Create("upgrade_general_nopanic",
            id: "general_nopanic",
            cardType: CardManager.CardType.None,
            effect: UpgradeEffectType.General_NoPanic,
            name1: "Veteran Instinct",      name2: "Veteran Instinct II",       name3: "Veteran Instinct III",
            desc1: "Hero does not flee due to zombie panic",
            desc2: "Hero does not flee due to zombie panic",
            desc3: "Hero does not flee due to zombie panic",
            v1: 0f, v2: 0f, v3: 0f,
            accent: new Color(0.7f, 0.7f, 0.9f));

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[UpgradeAssetCreator] Created {AssetDatabase.FindAssets("t:RunUpgradeDefinition", new[] { OutputPath }).Length} upgrades in {OutputPath}");
    }

    private static void Create(
        string fileName,
        string id,
        CardManager.CardType cardType,
        UpgradeEffectType effect,
        string name1, string name2, string name3,
        string desc1, string desc2, string desc3,
        float v1, float v2, float v3,
        Color accent)
    {
        string assetPath = $"{OutputPath}/{fileName}.asset";

        // Do not overwrite existing assets
        RunUpgradeDefinition existing = AssetDatabase.LoadAssetAtPath<RunUpgradeDefinition>(assetPath);
        if (existing != null)
        {
            Debug.Log($"[UpgradeAssetCreator] Skipping {fileName} — already exists");
            return;
        }

        var asset = ScriptableObject.CreateInstance<RunUpgradeDefinition>();
        asset.upgradeId          = id;
        asset.targetCardType     = cardType;
        asset.effectType         = effect;
        asset.displayName        = name1;
        asset.displayNameEnhanced = name2;
        asset.displayNameUltimate = name3;
        asset.descriptionTier1   = desc1;
        asset.descriptionTier2   = desc2;
        asset.descriptionTier3   = desc3;
        asset.valueT1            = v1;
        asset.valueT2            = v2;
        asset.valueT3            = v3;
        asset.accentColor        = accent;
        asset.ultimateColor      = new Color(1f, 0.8f, 0.2f);

        AssetDatabase.CreateAsset(asset, assetPath);
        Debug.Log($"[UpgradeAssetCreator] Created {fileName}.asset");
    }
}
