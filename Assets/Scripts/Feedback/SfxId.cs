/// <summary>
/// Every sound effect the game can play.
/// To replace a placeholder with a real sound: drop an audio file named exactly like the enum value
/// into Assets/Resources/Audio/Sfx/  (e.g. Assets/Resources/Audio/Sfx/SniperShot.ogg). No code or scene changes needed.
/// Music: Assets/Resources/Audio/Music/menu.ogg and gameplay.ogg.
/// </summary>
public enum SfxId
{
	None = 0,

	// --- UI ---
	UiClick,
	UiBack,
	UiPopupOpen,
	UiToggle,
	UiError,
	UiStar,
	UiReward,
	UiLevelStart,
	UiUpgrade,
	UiEquip,

	// --- Cards / placement ---
	CardPickUp,
	CardPlaceOk,
	CardPlaceInvalid,
	CardReady,

	// --- Zombies / civilians ---
	WaveWarning,
	ZombieHit,
	ZombieDeath,
	ZombieInfect,
	CivilianLost,

	// --- Weapons ---
	SniperShot,
	SoldierShot,
	HeliGun,
	BombExplosion,
	BarricadeHit,
	BarricadeBreak,

	// --- Transport / rescue ---
	HeliArrive,
	HeliTakeoff,
	CarArrive,
	RescueDeparted,
	RescueTick,
	GoalReached,

	// --- Abilities / boss ---
	AbilityReady,
	AbilityActivate,
	BossRage,

	// --- Results ---
	Win,
	PerfectClear,
	Lose
}
