using UnityEngine;
using UnityEditor;
using System.IO;

/// <summary>
/// Создаёт все RunUpgradeDefinition .asset файлы для MVP-апгрейдов.
/// Запусти через меню: ZombieGame / Create MVP Upgrade Assets
/// </summary>
public static class UpgradeAssetCreator
{
    private const string OutputPath = "Assets/Resources/Upgrades";

    [MenuItem("ZombieGame/Create MVP Upgrade Assets")]
    public static void CreateAllUpgrades()
    {
        if (!Directory.Exists(OutputPath))
            Directory.CreateDirectory(OutputPath);

        // ── ВЕРТОЛЁТ ──────────────────────────────────────────────────────

        Create("upgrade_heli_speed",
            id: "helicopter_quicklanding",
            cardType: CardManager.CardType.Helicopter,
            effect: UpgradeEffectType.Helicopter_SpeedMult,
            name1: "Быстрая Посадка",        name2: "Форсаж",                   name3: "VTOL-Протокол",
            desc1: "+35% скорость снижения",
            desc2: "Скорость x2.5, первые 3 сек. после посадки — зомби не вызывают панику",
            desc3: "Вертолёт снижается мгновенно",
            v1: 0.35f, v2: 1.5f, v3: 0f,
            accent: new Color(0.3f, 0.7f, 1f));

        Create("upgrade_heli_capacity",
            id: "helicopter_capacity",
            cardType: CardManager.CardType.Helicopter,
            effect: UpgradeEffectType.Helicopter_CapacityAdd,
            name1: "Доп. Места",             name2: "Тяжёлый Транспорт",         name3: "Массовая Эвакуация",
            desc1: "+2 места",
            desc2: "+6 мест суммарно",
            desc3: "Вместимость безлимитна — все гражданские в зоне",
            v1: 2f, v2: 4f, v3: 0f,
            accent: new Color(0.4f, 0.9f, 0.4f));

        Create("upgrade_heli_attract",
            id: "helicopter_megaphone",
            cardType: CardManager.CardType.Helicopter,
            effect: UpgradeEffectType.Helicopter_RadiusMult,
            name1: "Мегафон",               name2: "Экстренное Вещание",         name3: "Городская Тревога",
            desc1: "+40% радиус привлечения",
            desc2: "Радиус x2, гражданские бегут быстрее",
            desc3: "ВСЕ гражданские на карте идут к вертолёту",
            v1: 0.40f, v2: 1.0f, v3: 0f,
            accent: new Color(1f, 0.85f, 0.3f));

        // ── СНАЙПЕР ───────────────────────────────────────────────────────

        Create("upgrade_sniper_range",
            id: "sniper_range",
            cardType: CardManager.CardType.Sniper,
            effect: UpgradeEffectType.Sniper_RangeMult,
            name1: "Удлинённый Ствол",      name2: "Дальнобойная Платформа",     name3: "Всевидящее Око",
            desc1: "+35% дальность стрельбы",
            desc2: "+65% дальность стрельбы",
            desc3: "Снайпер бьёт по всей карте",
            v1: 0.35f, v2: 0.65f, v3: 0f,
            accent: new Color(0.7f, 0.5f, 0.2f));

        Create("upgrade_sniper_damage",
            id: "sniper_damage",
            cardType: CardManager.CardType.Sniper,
            effect: UpgradeEffectType.Sniper_DamageMult,
            name1: "Бронебойные Патроны",   name2: "Экзекутор",                  name3: "Шторм Свинца",
            desc1: "+50% урон",
            desc2: "+100% урон",
            desc3: "Мгновенно убивает не-боссов",
            v1: 0.5f, v2: 1.0f, v3: 0f,
            accent: new Color(0.9f, 0.3f, 0.2f));

        Create("upgrade_sniper_duration",
            id: "sniper_duration",
            cardType: CardManager.CardType.Sniper,
            effect: UpgradeEffectType.Sniper_DurationAdd,
            name1: "Долгая Вахта",          name2: "Оверватч",                   name3: "Вечный Страж",
            desc1: "+8 сек. длительность",
            desc2: "Снайпер не исчезает",
            desc3: "Снайпер не исчезает",
            v1: 8f, v2: 0f, v3: 0f,
            accent: new Color(0.5f, 0.5f, 0.8f));

        // ── БОМБА ─────────────────────────────────────────────────────────

        Create("upgrade_bomb_radius",
            id: "bomb_radius",
            cardType: CardManager.CardType.Bomb,
            effect: UpgradeEffectType.Bomb_RadiusMult,
            name1: "Увеличенная Боеголовка",name2: "Тактическое Ядро",           name3: "Термобарика",
            desc1: "+40% радиус взрыва",
            desc2: "+200% радиус взрыва",
            desc3: "Радиус взрыва — половина карты",
            v1: 0.4f, v2: 2.0f, v3: 0f,
            accent: new Color(1f, 0.5f, 0.1f));

        Create("upgrade_bomb_damage",
            id: "bomb_damage",
            cardType: CardManager.CardType.Bomb,
            effect: UpgradeEffectType.Bomb_DamageMult,
            name1: "Ударная Волна",         name2: "Избыточная Сила",            name3: "ЭМИ-Взрыв",
            desc1: "+100% урон",
            desc2: "+900% урон",
            desc3: "Оглушает выживших зомби на 5 сек.",
            v1: 1.0f, v2: 9.0f, v3: 0f,
            accent: new Color(0.9f, 0.2f, 0.1f));

        Create("upgrade_bomb_cluster",
            id: "bomb_cluster",
            cardType: CardManager.CardType.Bomb,
            effect: UpgradeEffectType.Bomb_ClusterCount,
            name1: "Кассетная Начинка",     name2: "Ковровая Бомбардировка",     name3: "Роевой Удар",
            desc1: "+3 мини-взрыва вокруг точки",
            desc2: "+5 мини-взрывов вокруг точки",
            desc3: "+12 мини-взрывов вокруг точки",
            v1: 3f, v2: 5f, v3: 12f,
            accent: new Color(1f, 0.7f, 0.0f));

        // ── БАРРИКАДА ─────────────────────────────────────────────────────

        Create("upgrade_barricade_hp",
            id: "barricade_hp",
            cardType: CardManager.CardType.Barricade,
            effect: UpgradeEffectType.Barricade_HPMult,
            name1: "Армированный Бетон",    name2: "Крепостная Стена",           name3: "Несокрушимый Бункер",
            desc1: "+50% HP баррикады",
            desc2: "+300% HP баррикады",
            desc3: "Баррикада неразрушима",
            v1: 0.5f, v2: 3.0f, v3: 0f,
            accent: new Color(0.6f, 0.4f, 0.2f));

        Create("upgrade_barricade_spike",
            id: "barricade_spike",
            cardType: CardManager.CardType.Barricade,
            effect: UpgradeEffectType.Barricade_ReflectDamage,
            name1: "Колючая Проволока",     name2: "Бритвенный Провод",          name3: "Зона Смерти",
            desc1: "25% отражение урона атакующим зомби",
            desc2: "75% отражение урона атакующим зомби",
            desc3: "Зомби рядом с баррикадой мгновенно погибают",
            v1: 0.25f, v2: 0.75f, v3: 0f,
            accent: new Color(0.8f, 0.1f, 0.1f));

        Create("upgrade_barricade_count",
            id: "barricade_count",
            cardType: CardManager.CardType.Barricade,
            effect: UpgradeEffectType.Barricade_CountAdd,
            name1: "Быстрое Развёртывание", name2: "Минное Поле",                name3: "Периметр",
            desc1: "+1 баррикада за установку",
            desc2: "+2 баррикады за установку",
            desc3: "+3 баррикады за установку",
            v1: 1f, v2: 1f, v3: 1f,
            accent: new Color(0.5f, 0.7f, 0.3f));

        // ── ОБЩИЕ ─────────────────────────────────────────────────────────

        Create("upgrade_general_xp",
            id: "general_xp",
            cardType: CardManager.CardType.None,
            effect: UpgradeEffectType.General_XPMult,
            name1: "Ускоренное Обучение",   name2: "Ускоренное Обучение II",     name3: "Ускоренное Обучение III",
            desc1: "+25% к получаемому XP",
            desc2: "+60% к получаемому XP",
            desc3: "+100% к получаемому XP",
            v1: 0.25f, v2: 0.35f, v3: 0.40f,
            accent: new Color(0.4f, 0.8f, 1f));

        Create("upgrade_general_nopanic",
            id: "general_nopanic",
            cardType: CardManager.CardType.None,
            effect: UpgradeEffectType.General_NoPanic,
            name1: "Ветеранский Инстинкт",  name2: "Ветеранский Инстинкт II",    name3: "Ветеранский Инстинкт III",
            desc1: "Герой не уходит из-за паники от зомби",
            desc2: "Герой не уходит из-за паники от зомби",
            desc3: "Герой не уходит из-за паники от зомби",
            v1: 0f, v2: 0f, v3: 0f,
            accent: new Color(0.7f, 0.7f, 0.9f));

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[UpgradeAssetCreator] Создано {AssetDatabase.FindAssets("t:RunUpgradeDefinition", new[] { OutputPath }).Length} апгрейдов в {OutputPath}");
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

        // Не перезаписываем существующие
        RunUpgradeDefinition existing = AssetDatabase.LoadAssetAtPath<RunUpgradeDefinition>(assetPath);
        if (existing != null)
        {
            Debug.Log($"[UpgradeAssetCreator] Пропускаем {fileName} — уже существует");
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
        Debug.Log($"[UpgradeAssetCreator] Создан {fileName}.asset");
    }
}
