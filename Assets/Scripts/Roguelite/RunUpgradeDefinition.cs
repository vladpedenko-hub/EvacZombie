using UnityEngine;

public enum UpgradeTier { Tier1 = 1, Tier2_Enhanced = 2, Tier3_Ultimate = 3 }
public enum UpgradeEffectType
{
    // Helicopter
    Helicopter_SpeedMult,         // T1/T2: verticalSpeed *= (1 + value); T3: flag heli_instant_land
    Helicopter_CapacityAdd,       // T1/T2: maxCapacity += value; T3: flag heli_unlimited_capacity
    Helicopter_RadiusMult,        // T1/T2: attractRadius *= (1 + value); T3: flag heli_global_attract
    Helicopter_LoadTimeAdd,       // loadTime += value
    Helicopter_BoardingReduction, // boardingCooldown *= (1 - value)
    Helicopter_NoPanic,           // flag: never panics
    Helicopter_InstantLand,       // flag: instant landing
    Helicopter_UnlimitedCapacity, // flag: unlimited capacity
    Helicopter_MegaphoneUlt,      // flag: all civilians run to the helicopter

    // Sniper
    Sniper_RangeMult,             // T1/T2: attackRange *= (1 + value); T3: flag sniper_global_range
    Sniper_DamageMult,            // T1/T2: damage *= (1 + value); T3: flag sniper_instakill
    Sniper_CooldownReduction,     // cooldownDelay *= (1 - value)
    Sniper_DurationAdd,           // T1: lifespan += value; T2/T3: flag sniper_permanent
    Sniper_PierceAdd,             // maxPierceTargets += value
    Sniper_Instakill,             // flag: instakill non-boss
    Sniper_Permanent,             // flag: lifespan = infinite
    Sniper_GlobalRange,           // flag: hits across the entire map
    Sniper_TripleTarget,          // flag: 3 targets simultaneously

    // Bomb
    Bomb_RadiusMult,              // T1/T2: damageRadius *= (1 + value); T3: flag bomb_mega_radius
    Bomb_DamageMult,              // T1/T2: damage *= (1 + value); T3: flag bomb_stun
    Bomb_ClusterCount,            // mini-bombs: add value units
    Bomb_Count,                   // number of bombs per activation += value
    Bomb_DestroyBuildings,        // flag: destroys buildings
    Bomb_Stun,                    // flag: stuns surviving zombies
    Bomb_MegaRadius,              // flag: radius = half the map

    // Barricade
    Barricade_HPMult,             // T1/T2: maxHealth *= (1 + value); T3: flag barricade_indestructible
    Barricade_ReflectDamage,      // T1/T2: % damage reflected; T3: flag barricade_death_zone
    Barricade_StunDuration,       // stun duration += value
    Barricade_WidthMult,          // NavMeshObstacle scale *= (1 + value)
    Barricade_CountAdd,           // number of barricades per placement += value
    Barricade_Indestructible,     // flag: cannot be destroyed
    Barricade_DeathZone,          // flag: instant kill zone
    Barricade_FullWidth,          // flag: blocks the entire street

    // General
    General_XPMult,               // incoming XP multiplier
    General_NoPanic,              // flag: hero does not panic for 5s after zombie nearby
}

[CreateAssetMenu(fileName = "NewUpgrade", menuName = "ZombieGame/Run Upgrade")]
public class RunUpgradeDefinition : ScriptableObject
{
    [Header("Identification")]
    public string upgradeId;
    public CardManager.CardType targetCardType; // CardType.None = General

    [Header("Display (Tier 1)")]
    public string displayName;
    [TextArea(2, 4)] public string descriptionTier1;
    public Sprite iconTier1;
    public Color accentColor = Color.white;

    [Header("Display (Tier 2 — Enhanced)")]
    public string displayNameEnhanced;
    [TextArea(2, 4)] public string descriptionTier2;
    public Sprite iconTier2;

    [Header("Display (Tier 3 — ULTIMATE)")]
    public string displayNameUltimate;
    [TextArea(2, 4)] public string descriptionTier3;
    public Sprite iconTier3;
    public Color ultimateColor = new Color(1f, 0.8f, 0.2f);

    [Header("Effects")]
    public UpgradeEffectType effectType;
    public float valueT1;
    public float valueT2;
    public float valueT3;

    // ── Effect application ────────────────────────────────────────────────

    /// <summary>Called from RunSessionData.ApplyUpgrade(). tier = 1, 2, or 3.</summary>
    public void ApplyEffect(int tier, RunSessionData session)
    {
        float val = tier == 1 ? valueT1 : tier == 2 ? valueT2 : valueT3;

        switch (effectType)
        {
            // ── Helicopter ──────────────────────────────────────────────
            case UpgradeEffectType.Helicopter_SpeedMult:
                if (tier == 3) session.SetFlag("heli_instant_land");
                else session.AddModifier("heli_speed_mult", val);
                break;
            case UpgradeEffectType.Helicopter_CapacityAdd:
                if (tier == 3) session.SetFlag("heli_unlimited_capacity");
                else session.AddModifier("heli_capacity_add", val);
                break;
            case UpgradeEffectType.Helicopter_RadiusMult:
                if (tier == 3) session.SetFlag("heli_global_attract");
                else session.AddModifier("heli_radius_mult", val);
                break;
            case UpgradeEffectType.Helicopter_LoadTimeAdd:
                session.AddModifier("heli_loadtime_add", val);
                break;
            case UpgradeEffectType.Helicopter_BoardingReduction:
                session.AddModifier("heli_boarding_reduction", val);
                break;
            case UpgradeEffectType.Helicopter_NoPanic:
                session.SetFlag("heli_no_panic");
                break;
            case UpgradeEffectType.Helicopter_InstantLand:
                session.SetFlag("heli_instant_land");
                break;
            case UpgradeEffectType.Helicopter_UnlimitedCapacity:
                session.SetFlag("heli_unlimited_capacity");
                break;
            case UpgradeEffectType.Helicopter_MegaphoneUlt:
                session.SetFlag("heli_global_attract");
                break;

            // ── Sniper ──────────────────────────────────────────────
            case UpgradeEffectType.Sniper_RangeMult:
                if (tier == 3) session.SetFlag("sniper_global_range");
                else session.AddModifier("sniper_range_mult", val);
                break;
            case UpgradeEffectType.Sniper_DamageMult:
                if (tier == 3) session.SetFlag("sniper_instakill");
                else session.AddModifier("sniper_damage_mult", val);
                break;
            case UpgradeEffectType.Sniper_CooldownReduction:
                session.AddModifier("sniper_cooldown_red", val);
                break;
            case UpgradeEffectType.Sniper_DurationAdd:
                if (tier >= 2) session.SetFlag("sniper_permanent");
                else session.AddModifier("sniper_duration_add", val);
                break;
            case UpgradeEffectType.Sniper_PierceAdd:
                session.AddModifier("sniper_pierce_add", val);
                break;
            case UpgradeEffectType.Sniper_Instakill:
                session.SetFlag("sniper_instakill");
                break;
            case UpgradeEffectType.Sniper_Permanent:
                session.SetFlag("sniper_permanent");
                break;
            case UpgradeEffectType.Sniper_GlobalRange:
                session.SetFlag("sniper_global_range");
                break;
            case UpgradeEffectType.Sniper_TripleTarget:
                session.SetFlag("sniper_triple_target");
                break;

            // ── Bomb ────────────────────────────────────────────────
            case UpgradeEffectType.Bomb_RadiusMult:
                if (tier == 3) session.SetFlag("bomb_mega_radius");
                else session.AddModifier("bomb_radius_mult", val);
                break;
            case UpgradeEffectType.Bomb_DamageMult:
                if (tier == 3) session.SetFlag("bomb_stun");
                else session.AddModifier("bomb_damage_mult", val);
                break;
            case UpgradeEffectType.Bomb_ClusterCount:
                session.AddModifier("bomb_cluster_count", val);
                break;
            case UpgradeEffectType.Bomb_Count:
                session.AddModifier("bomb_count_add", val);
                break;
            case UpgradeEffectType.Bomb_DestroyBuildings:
                session.SetFlag("bomb_destroy_buildings");
                break;
            case UpgradeEffectType.Bomb_Stun:
                session.SetFlag("bomb_stun");
                break;
            case UpgradeEffectType.Bomb_MegaRadius:
                session.SetFlag("bomb_mega_radius");
                break;

            // ── Barricade ────────────────────────────────────────────
            case UpgradeEffectType.Barricade_HPMult:
                if (tier == 3) session.SetFlag("barricade_indestructible");
                else session.AddModifier("barricade_hp_mult", val);
                break;
            case UpgradeEffectType.Barricade_ReflectDamage:
                if (tier == 3) session.SetFlag("barricade_death_zone");
                else session.AddModifier("barricade_reflect_pct", val);
                break;
            case UpgradeEffectType.Barricade_StunDuration:
                session.AddModifier("barricade_stun_dur", val);
                break;
            case UpgradeEffectType.Barricade_WidthMult:
                session.AddModifier("barricade_width_mult", val);
                break;
            case UpgradeEffectType.Barricade_CountAdd:
                session.AddModifier("barricade_count_add", val);
                break;
            case UpgradeEffectType.Barricade_Indestructible:
                session.SetFlag("barricade_indestructible");
                break;
            case UpgradeEffectType.Barricade_DeathZone:
                session.SetFlag("barricade_death_zone");
                break;
            case UpgradeEffectType.Barricade_FullWidth:
                session.SetFlag("barricade_full_width");
                break;

            // ── General ────────────────────────────────────────────────
            case UpgradeEffectType.General_XPMult:
                session.AddModifier("general_xp_mult", val);
                break;
            case UpgradeEffectType.General_NoPanic:
                session.SetFlag("general_no_panic");
                break;
        }
    }

    // ── UI Helpers ────────────────────────────────────────────────────

    public string GetDisplayName(int tier) => tier switch
    {
        1 => displayName,
        2 => displayNameEnhanced,
        3 => displayNameUltimate,
        _ => displayName
    };

    public string GetDescription(int tier) => tier switch
    {
        1 => descriptionTier1,
        2 => descriptionTier2,
        3 => descriptionTier3,
        _ => descriptionTier1
    };

    public Sprite GetIcon(int tier) => tier switch
    {
        1 => iconTier1 != null ? iconTier1 : iconTier2,
        2 => iconTier2 != null ? iconTier2 : iconTier1,
        3 => iconTier3 != null ? iconTier3 : iconTier1,
        _ => iconTier1
    };

    public Color GetAccentColor(int tier) => tier == 3 ? ultimateColor : accentColor;
}
