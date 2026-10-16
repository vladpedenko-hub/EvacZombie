using UnityEngine;
using System.Collections.Generic;

// feature/sandbox-level: every dev-sandbox-specific number lives here, in one asset, separate
// from CrowdTuning (which stays the single source of truth for normal-level crowd AI tuning,
// used on real levels too). This asset lives under Resources/ so SandboxButtonInjector can load
// it in a build without any AssetDatabase/Editor-only API.
[CreateAssetMenu(fileName = "SandboxConfig", menuName = "Game/Sandbox Config")]
public class SandboxConfig : ScriptableObject
{
	[Header("Level")]
	[Tooltip("The dev-only sandbox LevelData. Must have isSandboxLevel = true and must not be in any RegionConfig.levels list.")]
	public LevelData sandboxLevel;

	[Header("Cards — all unlocked at max level, sandbox only")]
	public List<CardData> allCards = new List<CardData>();

	[Header("Dev Panel — Timescale presets")]
	public float[] timescalePresets = { 0.5f, 1f, 2f, 4f };

	[Header("Dev Panel — Infinite Supply")]
	[Tooltip("Supply is topped back up to this value whenever it drops below it, while the infiniteSupply toggle is on.")]
	public int infiniteSupplyAmount = 999;

	[Header("Dev Panel — Spawn On Tap")]
	[Tooltip("Ground plane Y used to convert a screen tap into a world spawn position.")]
	public float spawnOnTapGroundY = 0f;

	[Header("Dev Panel — Layout")]
	public Vector2 panelAnchoredPosition = new Vector2(16f, -16f);
	public float panelWidth = 440f;
}
