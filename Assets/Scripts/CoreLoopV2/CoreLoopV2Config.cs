using UnityEngine;

// Single tunables asset for the whole Core Loop v2 vertical slice (Docs/CORE_LOOP_V2.md). Lives at
// Assets/Resources/CoreLoopV2Config.asset so every v2-only system can reach it via the static
// .Active accessor without any scene/prefab wiring -- same pattern as CameraReadabilityConfig.
[CreateAssetMenu(fileName = "CoreLoopV2Config", menuName = "ZombieGame/Core Loop V2 Config")]
public class CoreLoopV2Config : ScriptableObject
{
	private static CoreLoopV2Config cached;

	public static CoreLoopV2Config Active
	{
		get
		{
			if (cached == null) cached = Resources.Load<CoreLoopV2Config>("CoreLoopV2Config");
			return cached;
		}
	}

	// Support "Enter Play Mode without domain reload" -- don't leak a stale cached reference
	// between play sessions.
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
	private static void ResetStatics() => cached = null;

	[Header("Supply economy")]
	[Tooltip("Starting Supply when LevelData.startingSupply is 0.")]
	public int startingSupplyDefault = 6;

	[Tooltip("Supply cap. Overflow income is lost.")]
	public int supplyCap = 20;

	[Tooltip("Supply granted per rescued Human when a +N fly-in lands at the counter.")]
	public int supplyIncomeHuman = 1;

	[Tooltip("Supply granted per rescued Scientist when a +N fly-in lands at the counter.")]
	public int supplyIncomeScientist = 3;

	[Tooltip("Passive Supply trickle amount, granted every supplyTrickleInterval seconds during Playing/SuddenDeath (never during Planning).")]
	public int supplyTrickleAmount = 1;

	[Tooltip("Seconds between each passive Supply trickle tick.")]
	public float supplyTrickleInterval = 5f;

	[Tooltip("Supply bonus granted when a wave is cleared (all zombies dead) rather than forced by its timeout.")]
	public int waveClearedBonus = 2;

	[Header("Card cooldowns")]
	[Tooltip("Flat anti-double-tap cooldown used by every card in v2 levels, replacing each card prefab's own cooldownTime.")]
	public float v2CardCooldown = 0.5f;

	[Header("Planning phase")]
	[Tooltip("Planning auto-starts the level after this many seconds if GO isn't pressed first.")]
	public float planningMaxDuration = 15f;

	[Tooltip("Supply granted by the Planning fake-door ad button (LevelData.v2ShowAdOffer), once per attempt.")]
	public int fakeDoorAdSupplyGrant = 3;

	[Tooltip("Supply bonus applied to the next attempt after taking the Lose popup's fake-door ad retry offer.")]
	public int loseRetryAdSupplyGrant = 5;

	[Header("Waves")]
	[Tooltip("Max seconds a wave is allowed to run (measured from its last spawn) before it's force-ended even if zombies remain.")]
	public float waveMaxDuration = 25f;

	[Tooltip("Seconds of calm between waves (no new spawns; units/transports/civilians keep working).")]
	public float breatherDuration = 6f;

	[Header("Night / Last Call")]
	[Tooltip("The free, one-shot Night 'LAST TRANSPORT' CardData (reuses the Helicopter prefab via HelicopterController.ConfigureAsLastTransport). Referenced here so it reaches the build despite living outside a Resources folder -- a ScriptableObject in Resources pulls in anything it references.")]
	public CardData lastTransportCard;

	[Tooltip("Seconds the time-limited Night phase lasts in v2, after which the Last Transport departs and the level ends.")]
	public float lastCallDuration = 18f;

	[Tooltip("Boarding interval for the Last Transport (must be fast enough to load everyone within lastCallDuration).")]
	public float lastTransportBoardingInterval = 0.1f;

	[Tooltip("Seconds after Night starts before the Last Transport auto-places itself if the player hasn't placed it yet.")]
	public float lastTransportAutoPlaceDelay = 6f;

	[Header("Signal Flare ultimate")]
	[Tooltip("World-space radius of the Signal Flare's stun + rescue-redirect effect.")]
	public float flareRadius = 12f;

	[Tooltip("Seconds zombies caught in the Signal Flare radius are stunned for.")]
	public float flareStunDuration = 4f;
}
