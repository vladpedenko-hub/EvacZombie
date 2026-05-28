using UnityEngine;

public enum UpgradeTier { Tier1 = 1, Tier2_Enhanced = 2, Tier3_Ultimate = 3 }
public enum UpgradeEffectType
{
    // Вертолёт
    Helicopter_SpeedMult,         // T1/T2: verticalSpeed *= (1 + value); T3: флаг heli_instant_land
    Helicopter_CapacityAdd,       // T1/T2: maxCapacity += value; T3: флаг heli_unlimited_capacity
    Helicopter_RadiusMult,        // T1/T2: attractRadius *= (1 + value); T3: флаг heli_global_attract
    Helicopter_LoadTimeAdd,       // loadTime += value
    Helicopter_BoardingReduction, // boardingCooldown *= (1 - value)
    Helicopter_NoPanic,           // флаг: никогда не паникует
    Helicopter_InstantLand,       // флаг: мгновенная посадка
    Helicopter_UnlimitedCapacity, // флаг: безлимитная вместимость
    Helicopter_MegaphoneUlt,      // флаг: все гражданские бегут к вертолёту

    // Снайпер
    Sniper_RangeMult,             // T1/T2: attackRange *= (1 + value); T3: флаг sniper_global_range
    Sniper_DamageMult,            // T1/T2: damage *= (1 + value); T3: флаг sniper_instakill
    Sniper_CooldownReduction,     // cooldownDelay *= (1 - value)
    Sniper_DurationAdd,           // T1: lifespan += value; T2/T3: флаг sniper_permanent
    Sniper_PierceAdd,             // maxPierceTargets += value
    Sniper_Instakill,             // флаг: instakill non-boss
    Sniper_Permanent,             // флаг: lifespan = бесконечен
    Sniper_GlobalRange,           // флаг: бьёт по всей карте
    Sniper_TripleTarget,          // флаг: 3 цели одновременно

    // Бомба
    Bomb_RadiusMult,              // T1/T2: damageRadius *= (1 + value); T3: флаг bomb_mega_radius
    Bomb_DamageMult,              // T1/T2: damage *= (1 + value); T3: флаг bomb_stun
    Bomb_ClusterCount,            // мини-бомбы: добавить value штук
    Bomb_Count,                   // количество бомб за активацию += value
    Bomb_DestroyBuildings,        // флаг: уничтожает здания
    Bomb_Stun,                    // флаг: оглушает выживших зомби
    Bomb_MegaRadius,              // флаг: radius = половина карты

    // Баррикада
    Barricade_HPMult,             // T1/T2: maxHealth *= (1 + value); T3: флаг barricade_indestructible
    Barricade_ReflectDamage,      // T1/T2: % отражения урона; T3: флаг barricade_death_zone
    Barricade_StunDuration,       // длительность оглушения += value
    Barricade_WidthMult,          // NavMeshObstacle scale *= (1 + value)
    Barricade_CountAdd,           // количество баррикад за установку += value
    Barricade_Indestructible,     // флаг: нельзя уничтожить
    Barricade_DeathZone,          // флаг: зона мгновенной смерти
    Barricade_FullWidth,          // флаг: блокирует всю улицу

    // Общие
    General_XPMult,               // множитель входящего XP
    General_NoPanic,              // флаг: герой не паникует 5с после зомби рядом
}

[CreateAssetMenu(fileName = "NewUpgrade", menuName = "ZombieGame/Run Upgrade")]
public class RunUpgradeDefinition : ScriptableObject
{
    [Header("Идентификация")]
    public string upgradeId;
    public CardManager.CardType targetCardType; // CardType.None = General

    [Header("Отображение (Тир 1)")]
    public string displayName;
    [TextArea(2, 4)] public string descriptionTier1;
    public Sprite iconTier1;
    public Color accentColor = Color.white;

    [Header("Отображение (Тир 2 — Enhanced)")]
    public string displayNameEnhanced;
    [TextArea(2, 4)] public string descriptionTier2;
    public Sprite iconTier2;

    [Header("Отображение (Тир 3 — ULTIMATE)")]
    public string displayNameUltimate;
    [TextArea(2, 4)] public string descriptionTier3;
    public Sprite iconTier3;
    public Color ultimateColor = new Color(1f, 0.8f, 0.2f);

    [Header("Эффекты")]
    public UpgradeEffectType effectType;
    public float valueT1;
    public float valueT2;
    public float valueT3;

    // ── Применение эффекта ────────────────────────────────────────────────

    /// <summary>Вызывается из RunSessionData.ApplyUpgrade(). tier = 1, 2 или 3.</summary>
    public void ApplyEffect(int tier, RunSessionData session)
    {
        float val = tier == 1 ? valueT1 : tier == 2 ? valueT2 : valueT3;

        switch (effectType)
        {
            // ── Вертолёт ──────────────────────────────────────────────
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

            // ── Снайпер ──────────────────────────────────────────────
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

            // ── Бомба ────────────────────────────────────────────────
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

            // ── Баррикада ────────────────────────────────────────────
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

            // ── Общие ────────────────────────────────────────────────
            case UpgradeEffectType.General_XPMult:
                session.AddModifier("general_xp_mult", val);
                break;
            case UpgradeEffectType.General_NoPanic:
                session.SetFlag("general_no_panic");
                break;
        }
    }

    // ── Хелперы для UI ────────────────────────────────────────────────────

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
