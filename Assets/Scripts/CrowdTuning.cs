using UnityEngine;

// feature/crowd-tuning-pass: single shared config for every zombie/civilian street-AI gameplay
// number (speeds, radii, durations, shares, siege, infection, grid, debug). Supersedes
// ChaosSettings and the per-prefab duplicates that used to live on Zombie/EvacueeBase (moveSpeed,
// attackDistance, barricadeCheckRadius, walkSpeed, runSpeed, panicRadius, etc.).
//
// Zombie/EvacueeBase/CrowdFieldManager read every field here directly through a property each
// time it's needed (no caching into local fields at spawn time), so edits in the Inspector take
// effect immediately in Play Mode. The one deliberate exception is the handful of already-cached
// WaitForSeconds objects (attack windup/cooldown, hit-flash duration) that predate this pass for
// GC reasons (Docs/REFACTOR_AUDIT.md Phase B) -- those pick up an edit on the next spawn/pool
// reuse, not mid-attack. That's an existing, unrelated tradeoff this pass doesn't revisit.
//
// Speeds (zombieSpeed, civCalmSpeed, civPanicSpeed) are ABSOLUTE world units/second, not
// multipliers on some other base speed field.
[CreateAssetMenu(fileName = "CrowdTuning", menuName = "Game/Crowd Tuning")]
public class CrowdTuning : ScriptableObject
{
	// ───────────────────────────── Zombie ─────────────────────────────
	[Header("Zombie")]
	[Range(0.2f, 4f)]
	[Tooltip("Базовая скорость зомби, м/с (абсолютное значение, не множитель).")]
	public float zombieSpeed = 1.0f;

	[Range(0.5f, 4f)]
	[Tooltip("Дистанция укуса/атаки зомби, метры. (Было по умолчанию 1.4 в коде, но на префабе обычного зомби фактически стояло 1.0 — у ZombieBoss было 1.4. При объединении в общий конфиг один из них неизбежно теряет свою отличную настройку; взято значение обычного зомби как более массовое.)")]
	public float attackDistance = 1.0f;

	[Range(1f, 8f)]
	[Tooltip("Радиус, в котором зомби переключается на атаку барикады вместо обхода.")]
	public float barricadeCheckRadius = 3.5f;

	[Range(1, 999)]
	[Tooltip("Урон по барикаде за один удар.")]
	public int barricadeDamage = 25;

	[Range(0.1f, 5f)]
	[Tooltip("Кулдаун между ударами по барикаде, секунды.")]
	public float attackCooldown = 1.0f;

	[Range(0f, 2f)]
	[Tooltip("Радиус, в котором зомби расходятся вокруг приманки (Bait).")]
	public float crowdRadius = 0.8f;

	[Range(0f, 1f)]
	[Tooltip("Доля зомби обычной волны, которые становятся Hunter (остальные — Wanderer).")]
	public float hunterShare = 0.7f;

	[Range(0f, 1f)]
	[Tooltip("hunterShare ночью (во время SuddenDeath) — обычно все зомби становятся Hunter.")]
	public float nightHunterShare = 1.0f;

	[Range(1f, 15f)]
	[Tooltip("Клетки сетки. Wanderer замечает гражданского на той же прямой улице в этом радиусе и становится Hunter.")]
	public float aggroRadiusCells = 5f;

	[Range(0f, 1f)]
	[Tooltip("Если доля живых гражданских падает ниже этого значения — все зомби становятся Hunter.")]
	public float lateWaveHunterThreshold = 0.2f;

	[Range(0.2f, 5f)]
	[Tooltip("Минимальный интервал между решениями навигации (зомби и гражданских), секунды.")]
	public float retargetCooldown = 1.5f;

	[Range(1, 10)]
	[Tooltip("Сколько ближайших (по прямой) гражданских рассматривает Hunter перед случайным выбором цели.")]
	public int hunterCandidateCount = 4;

	[Range(1, 5)]
	[Tooltip("Максимум зомби, которые могут одновременно преследовать одного и того же гражданского.")]
	public int maxZombiesPerTarget = 2;

	[Range(0f, 0.5f)]
	[Tooltip("Случайный разброс скорости зомби при спавне, доля от базовой скорости (например 0.15 = ±15%).")]
	public float speedVariance = 0.15f;

	[Tooltip("A/B переключатель: ставит всем зомби ObstacleAvoidanceType.LowQuality вместо значения из префаба.")]
	public bool zombieAvoidanceLow = false;

	// ───────────────────────────── Civilian ─────────────────────────────
	[Header("Civilian")]
	[Range(0.1f, 3f)]
	[Tooltip("Скорость гражданского в спокойном состоянии, м/с (абсолютное значение).")]
	public float civCalmSpeed = 0.45f;

	[Range(0.2f, 6f)]
	[Tooltip("Скорость гражданского в панике, м/с (абсолютное значение). Должна быть больше zombieSpeed.")]
	public float civPanicSpeed = 1.4f;

	[Range(1f, 10f)]
	[Tooltip("Клетки сетки. Зомби в этом радиусе (по прямой) запускает панику у гражданского.")]
	public float fearRadiusCells = 4f;

	[Range(1, 10)]
	[Tooltip("Паника: цель убегания выбирается шагами вперёд по полю F_zed (от зомби) за один SetDestination, а не по одной клетке.")]
	public int panicLookaheadCells = 6;

	[Range(0.5f, 5f)]
	[Tooltip("Секунды, на которые фиксируется направление паники (бежать от зомби или к эвакуации), прежде чем пересчитать угрозу.")]
	public float panicDuration = 2.5f;

	[Range(0f, 8f)]
	[Tooltip("Клетки сетки. Спокойный гражданский в этом радиусе от уже паникующего — тоже начинает паниковать (цепная реакция).")]
	public float panicContagionCells = 2f;

	[Range(1f, 15f)]
	[Tooltip("Клетки сетки. Спокойный гражданский бродит только между перекрёстками, где поле F_zed не меньше этого значения (безопасное расстояние от зомби).")]
	public float calmSafeDistanceCells = 6f;

	[Range(1f, 15f)]
	[Tooltip("Клетки сетки (шаги поля F_evac, а не метры). В этом радиусе от активной точки эвакуации гражданский идёт к ней вместо того, чтобы бродить.")]
	public float evacRadiusCells = 8f;

	// ───────────────────────────── Siege ─────────────────────────────
	[Header("Siege")]
	[Range(1, 10)]
	[Tooltip("В режиме Siege зомби идёт по полю F_civ вперёд на столько клеток (или до перекрёстка) за один SetDestination.")]
	public int lookaheadCells = 6;

	[Range(1f, 4f)]
	[Tooltip("Hunter преследует гражданского напрямую, только если путь по NavMesh не длиннее, чем (расстояние по полю F_civ у зомби, в метрах) × этот коэффициент. Иначе — Siege.")]
	public float maxDetourRatio = 1.5f;

	[Range(0f, 10f)]
	[Tooltip("Клетки сетки. В этом радиусе от барикады в режиме Siege стоять на месте — нормально (очередь к пролому); защита от зависания не срабатывает.")]
	public float siegeQueueRadiusCells = 3f;

	[Range(0.1f, 5f)]
	[Tooltip("Секунды, которые Hunter может стоять почти без движения (не атакуя), прежде чем бросить цель и перейти в Siege.")]
	public float stallTime = 1f;

	[Range(1f, 30f)]
	[Tooltip("fix/barricade-jerk-wave1 (Bug 1). Клетки сетки. Когда барикада ставится или разрушается, все зомби в этом радиусе от неё немедленно пересчитывают решение в тот же кадр (не ждут обычный тик/кулдаун) — иначе был рывок назад на ~0.3с, пока NavMesh/поле не успевали отреагировать.")]
	public float barricadeReactRadiusCells = 12f;

	[Range(3f, 20f)]
	[Tooltip("fix/barricade-jerk-wave1 (Bug 2). Радиус (метры) для NavMesh.SamplePosition при постановке зомби на NavMesh после спавна. Точка спавна дальше 3м от NavMesh (старый радиус) оставляла агента вне меша — зомби вообще не двигался.")]
	public float spawnSnapRadius = 10f;

	// ───────────────────────────── Infection ─────────────────────────────
	[Header("Infection")]
	[Range(0f, 2f)]
	[Tooltip("Задержка перед укусом/ударом после сближения с целью, секунды.")]
	public float infectDelay = 0.15f;

	[Range(0f, 5f)]
	[Tooltip("Секунды между укусом и превращением гражданского в зомби. Убить заражённого раньше — превращение не произойдёт.")]
	public float turnDelay = 0.7f;

	// ───────────────────────────── Grid ─────────────────────────────
	[Header("Grid")]
	[Range(0.05f, 2f)]
	[Tooltip("Как часто (секунды) пересчитываются общие поля F_civ/F_zed/F_evac.")]
	public float fieldRefresh = 0.3f;

	[Range(0.5f, 10f)]
	[Tooltip("Минимальный размер клетки сетки дорог, метры.")]
	public float roadGridMinCellSize = 2f;

	[Range(8, 128)]
	[Tooltip("Целевое число клеток сетки на ось — на больших уровнях клетка увеличивается, чтобы держаться около этого числа.")]
	public int roadGridTargetCellsPerAxis = 48;

	[Range(0.05f, 1f)]
	[Tooltip("Клетка проходима, только если NavMesh найден в пределах этой доли от размера клетки вокруг её центра.")]
	public float roadGridSnapTolerance = 0.35f;

	[Range(1f, 50f)]
	[Tooltip("Стоимость прохода зомби через клетку с активной барикадой (вместо 1 для открытой дороги). Конечная — зомби всё равно пройдёт/атакует, если это лучший вариант.")]
	public float barricadePathCost = 15f;

	// ───────────────────────────── Debug ─────────────────────────────
	[Header("Debug")]
	[Tooltip("Показывать отладочную сетку дорог и направление поля F_civ в Scene View (только редактор).")]
	public bool drawGridGizmos = false;

	[Tooltip("Цвет проходимых клеток в отладочной сетке.")]
	public Color gizmoWalkableColor = new Color(0f, 1f, 0f, 0.15f);

	[Tooltip("Цвет стрелок направления поля F_civ в отладочной сетке.")]
	public Color gizmoDescentArrowColor = new Color(1f, 0.5f, 0f, 0.85f);

	[Tooltip("Показывать режим зомби (Hunt/Wander/Siege), линию до текущей цели и счётчик застрявших зомби (редактор и development build).")]
	public bool showZombieSiegeOverlay = false;

	// ───────────────────────────── Legacy ─────────────────────────────
	[Header("Legacy")]
	[Tooltip("Включает старую случайную \"химию\" поведения зомби (случайная потеря цели). По умолчанию выключено — пути из fix/zombie-siege-mode достаточно для текущего поведения.")]
	public bool legacyChaosEnabled = false;

	[Range(0f, 1f)]
	[Tooltip("(legacyChaosEnabled) Шанс, что зомби в случайный момент теряет текущую цель.")]
	public float zombieLoseTargetChance = 0.03f;

	[Range(0.1f, 5f)]
	[Tooltip("(legacyChaosEnabled) Как часто (секунды) проверяется шанс потери цели.")]
	public float zombieLoseTargetCheckInterval = 0.75f;

	[Range(0.1f, 5f)]
	[Tooltip("(legacyChaosEnabled) Сколько секунд зомби бродит бесцельно после потери цели.")]
	public float zombieLoseTargetDuration = 0.6f;

	[Range(0f, 1f)]
	[Tooltip("УСТАРЕЛО, нигде не читается — старый шанс паники человека до перехода на детерминированную систему (fearRadiusCells).")]
	public float humanPanicChance = 0.05f;

	[Range(0.1f, 5f)]
	[Tooltip("УСТАРЕЛО, нигде не читается — старый интервал проверки паники.")]
	public float humanPanicCheckInterval = 1.0f;

	[Range(0.1f, 5f)]
	[Tooltip("УСТАРЕЛО, нигде не читается — длительность старого спринта паники.")]
	public float humanPanicDuration = 0.4f;

	[Range(0f, 10f)]
	[Tooltip("УСТАРЕЛО, нигде не читается — дистанция старого спринта паники.")]
	public float humanPanicMoveDistance = 3.0f;

	[Range(0f, 10f)]
	[Tooltip("УСТАРЕЛО, нигде не читается — старый радиус срабатывания паники.")]
	public float humanPanicTriggerRadius = 4.5f;

	private void OnValidate()
	{
		if (civPanicSpeed <= zombieSpeed)
		{
			Debug.LogWarning($"[CrowdTuning] civPanicSpeed ({civPanicSpeed}) <= zombieSpeed ({zombieSpeed}) — гражданские в панике не смогут убежать от зомби.", this);
		}
	}
}
