using UnityEngine;
using System.Collections;
using System.Collections.Generic;

// [File name]: Sniper (stationary unit placed on a building).
// [Setup]: assign myCardData in the inspector.
public class Sniper : MonoBehaviour
{
	[Header("Card & Settings")]
	public CardData myCardData;

	[Header("Runtime Parameters")]
	// feature/sniper-feel: these two are overwritten from SniperFeelTuning in Start() (fallback
	// values here only matter if the tuning asset is somehow missing). The field names/serialized
	// prefab values stay so nothing else that reads them (none currently) breaks.
	public float aimDuration = 1.1f;        // Time spent aiming before firing
	public float firstShotAimFactor = 0.35f; // Aim time reduction for the first shot
	public float muzzleHeight = 1.5f;
	public float targetHeight = 1.0f;

	[Header("Piercing")]
	public int maxPierceTargets = 3;      // Maximum targets the bullet can pass through
	public float pierceDamageFalloff = 0.5f; // Damage multiplier per pierced target (1, 0.5, 0.25, etc.)

	[Header("Civilian Safety")]
	public float civilianThreatRadius = 6f; // If a zombie is this close to a civilian, it becomes a priority target
	public float progressBias = 0.05f;      // Slight preference for zombies further along the Z axis (legacy targeting only)

	private float cooldownDelay;
	private float attackRange;
	private int damage;
	private float lifespan;
	private float maxLifespan;

	// Cached so SniperRoutine's repeated yields don't allocate a new WaitForSeconds each cycle.
	private static readonly WaitForSeconds afterShotWait = new WaitForSeconds(0.15f);
	private static readonly WaitForSeconds targetLostWait = new WaitForSeconds(0.2f);
	private static readonly WaitForSeconds noTargetWait = new WaitForSeconds(0.35f);
	private WaitForSeconds cooldownWait;

	private bool isExtracting = false;
	private bool isFirstShot = true;
	private LineRenderer laserLine;
	private Transform myBuilding;

	private readonly List<Transform> civilianBuffer = new List<Transform>();

	// Reused buffer for ApplyPiercingDamage's/ScorePierceLine's Physics.RaycastNonAlloc, fully
	// consumed (sorted and iterated) before each call returns, so one buffer per sniper is safe to
	// reuse across sequential calls (no yield between them).
	private RaycastHit[] piercingHitsBuffer = new RaycastHit[8];

	// Cached comparer instance so sorting piercingHitsBuffer by distance doesn't allocate a new
	// Comparer<RaycastHit> wrapper every shot (what Array.Sort(array, Comparison<T>) does internally).
	private sealed class RaycastHitDistanceComparer : IComparer<RaycastHit>
	{
		public static readonly RaycastHitDistanceComparer Instance = new RaycastHitDistanceComparer();
		public int Compare(RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance);
	}

	// ── feature/sniper-feel ───────────────────────────────────────────
	private static SniperFeelTuning Tuning => SniperFeelTuning.Active;

	// Master "new feel" switch -- also gates the shot tracer/recoil/kill-kick/pixel-scaled aim
	// width, none of which have their own dedicated bool in the tuning asset (lockOnRing and
	// lifetimeArc do, and are checked independently so they can be tuned separately in the Inspector).
	private bool UseNewVisuals => Tuning != null && Tuning.useTargetingV2;

	// All animation happens on this child, never on the root (the BoxCollider/tap-to-pick must
	// stay put). Sniper.prefab has no child at all (mesh lives directly on the root) -- created and
	// the mesh moved onto it the first time, same spirit as Barricade's "Visual" child.
	private Transform visualTransform;
	private Vector3 visualBaseScale = Vector3.one;

	private LineRenderer lockOnRingRenderer;
	private LineRenderer lifetimeArcRenderer;

	private bool lockFired;
	private Coroutine recoilRoutine;
	private float lastKillKickTime = -999f;

	private readonly List<Zombie> rangeCandidatesBuffer = new List<Zombie>();

	private struct ShotResult
	{
		public Vector3 farthestPoint;
		public bool killedAny;
	}

	public void Init(Transform buildingTransform)
	{
		myBuilding = buildingTransform;
	}

	private void Awake()
	{
		laserLine = gameObject.AddComponent<LineRenderer>();
		laserLine.material = SharedMaterials.SpritesDefaultMaterial;
		laserLine.enabled = false;
		laserLine.positionCount = 2;

		ResolveVisualRoot();

		var ringGo = new GameObject("LockOnRing");
		ringGo.transform.SetParent(transform, false);
		lockOnRingRenderer = ringGo.AddComponent<LineRenderer>();
		lockOnRingRenderer.material = SharedMaterials.SpritesDefaultMaterial;
		lockOnRingRenderer.loop = true;
		lockOnRingRenderer.enabled = false;

		var arcGo = new GameObject("LifetimeArc");
		arcGo.transform.SetParent(transform, false);
		lifetimeArcRenderer = arcGo.AddComponent<LineRenderer>();
		lifetimeArcRenderer.material = SharedMaterials.SpritesDefaultMaterial;
		lifetimeArcRenderer.loop = false;
		lifetimeArcRenderer.enabled = false;
	}

	// feature/sniper-feel: Sniper.prefab's mesh lives directly on the root GameObject (unlike
	// Barricade, which already has its own "Visual" child) -- move it onto a new child by copying
	// the mesh/material references across, then remove the root's own renderer so nothing draws
	// twice. The root keeps its BoxCollider/this script untouched.
	private void ResolveVisualRoot()
	{
		visualTransform = transform.Find("Visual");

		if (visualTransform == null)
		{
			var visualGo = new GameObject("Visual");
			visualGo.transform.SetParent(transform, false);

			var existingChildren = new List<Transform>();
			for (int i = 0; i < transform.childCount; i++)
			{
				Transform child = transform.GetChild(i);
				if (child != visualGo.transform) existingChildren.Add(child);
			}

			if (existingChildren.Count > 0)
			{
				foreach (Transform child in existingChildren) child.SetParent(visualGo.transform, true);
			}
			else
			{
				MeshFilter rootFilter = GetComponent<MeshFilter>();
				MeshRenderer rootRenderer = GetComponent<MeshRenderer>();
				if (rootFilter != null && rootRenderer != null)
				{
					MeshFilter childFilter = visualGo.AddComponent<MeshFilter>();
					childFilter.sharedMesh = rootFilter.sharedMesh;

					MeshRenderer childRenderer = visualGo.AddComponent<MeshRenderer>();
					childRenderer.sharedMaterials = rootRenderer.sharedMaterials;
					childRenderer.shadowCastingMode = rootRenderer.shadowCastingMode;

					Destroy(rootRenderer);
					Destroy(rootFilter);
				}
			}

			visualTransform = visualGo.transform;
		}

		visualBaseScale = visualTransform.localScale;
	}

	private void Start()
	{
		int currentLevel = 1;
		if (PlayerProfile.Instance != null && myCardData != null)
		{
			var progress = PlayerProfile.Instance.ownedCardsProgress.Find(p => p.cardId == myCardData.name);
			if (progress != null) currentLevel = progress.currentLevel;

			cooldownDelay = myCardData.GetCalculatedStat(StatType.Cooldown, currentLevel);
			attackRange = myCardData.GetCalculatedStat(StatType.Radius, currentLevel);
			damage = (int)myCardData.GetCalculatedStat(StatType.Damage, currentLevel);
			lifespan = myCardData.GetCalculatedStat(StatType.Duration, currentLevel);
		}
		else
		{
			cooldownDelay = 2.0f;
			attackRange = 25f;
			damage = 100;
			lifespan = 15f;
		}

		if (cooldownDelay <= 0f) cooldownDelay = 2f;
		if (attackRange <= 0f) attackRange = 25f;
		if (damage <= 0) damage = 100;
		if (lifespan <= 0f) lifespan = 15f;
		if (civilianThreatRadius <= 0f) civilianThreatRadius = 6f;

		// Run-modifiers (roguelite) -- unchanged
		var run = RunSessionData.Instance;
		if (run != null)
		{
			attackRange *= (1f + run.GetModifier("sniper_range_mult"));
			damage = Mathf.RoundToInt(damage * (1f + run.GetModifier("sniper_damage_mult")));
			cooldownDelay *= Mathf.Max(0.1f, 1f - run.GetModifier("sniper_cooldown_red"));
			lifespan += run.GetModifier("sniper_duration_add");
			maxPierceTargets += (int)run.GetModifier("sniper_pierce_add");

			if (run.HasFlag("sniper_global_range"))
				attackRange = 9999f;

			if (run.HasFlag("sniper_permanent"))
				lifespan = 99999f;
		}

		maxLifespan = lifespan;
		cooldownWait = new WaitForSeconds(cooldownDelay);

		SniperFeelTuning tuning = Tuning;
		aimDuration = tuning != null ? tuning.aimDuration : aimDuration;
		firstShotAimFactor = tuning != null ? tuning.firstShotAimFactor : firstShotAimFactor;

		if (UseNewVisuals)
			StartCoroutine(PlacementPopRoutine());

		StartCoroutine(SniperRoutine());
	}

	private void Update()
	{
		if (isExtracting) return;

		lifespan -= Time.deltaTime;
		if (lifespan <= 0f)
			StartCoroutine(ExtractRoutine());

		UpdateLifetimeArc();
	}

	private IEnumerator ExtractRoutine()
	{
		isExtracting = true;
		laserLine.enabled = false;
		lockOnRingRenderer.enabled = false;
		lifetimeArcRenderer.enabled = false;

		float t = 0f;
		Vector3 startScale = visualTransform.localScale; // animate the visual child, never the root

		while (t < 1f)
		{
			t += Time.deltaTime * 3f;
			visualTransform.localScale = Vector3.Lerp(startScale, Vector3.zero, t);
			yield return null;
		}

		Destroy(gameObject);
	}

	private IEnumerator SniperRoutine()
	{
		while (!isExtracting)
		{
			Zombie target = FindTarget();

			if (target != null)
			{
				SniperFeelTuning tuning = Tuning;
				laserLine.enabled = true;
				float aimTimer = 0f;
				lockFired = false;

				float currentAimDuration = isFirstShot
					? Mathf.Max(aimDuration * firstShotAimFactor, 0.1f)
					: aimDuration;

				bool targetLost = false;

				while (aimTimer < currentAimDuration)
				{
					if (target == null || !HasLineOfSight(target.transform))
					{
						targetLost = true;
						break;
					}

					float progress = aimTimer / currentAimDuration;

					float minWidth = (UseNewVisuals && tuning != null)
						? tuning.laserMinPixelWidth * ComputeWorldPerPixel(target.transform.position)
						: 0f;
					float currentWidth = Mathf.Max(Mathf.Lerp(0.02f, 0.06f, progress), minWidth);
					laserLine.startWidth = currentWidth;
					laserLine.endWidth = currentWidth;

					Color currentColor = new Color(1f, 0f, 0f, Mathf.Lerp(0.2f, 1f, progress));
					laserLine.startColor = currentColor;
					laserLine.endColor = currentColor;

					laserLine.SetPosition(0, transform.position + Vector3.up * muzzleHeight);
					laserLine.SetPosition(1, target.transform.position + Vector3.up * targetHeight);

					if (UseNewVisuals && tuning.lockOnRing)
					{
						bool finalBlink = (currentAimDuration - aimTimer) <= 0.15f;
						if (finalBlink && !lockFired)
						{
							lockFired = true;
							Feedback.Play(FeedbackEvent.SniperLock, target.transform.position);
						}

						float ringAlpha = finalBlink
							? Mathf.Lerp(0.3f, 1f, Mathf.PingPong(Time.time * 10f, 1f))
							: Mathf.Lerp(0.15f, 0.8f, progress);
						float ringRadius = Mathf.Lerp(0.25f, 0.55f, progress);
						UpdateLockOnRing(target.transform.position, ringRadius, ringAlpha);
					}
					else
					{
						lockOnRingRenderer.enabled = false;
					}

					aimTimer += Time.deltaTime;
					yield return null;
				}

				if (!targetLost && target != null)
				{
					Feedback.Play(FeedbackEvent.SniperShot, transform.position + Vector3.up * muzzleHeight);

					ShotResult result = ApplyPiercingDamage(target);
					bool killedAny = result.killedAny;
					Vector3 farthestPoint = result.farthestPoint;

					// Triple target upgrade
					if (RunSessionData.Instance != null && RunSessionData.Instance.HasFlag("sniper_triple_target"))
					{
						var extras = FindTopNTargets(3);
						foreach (var extra in extras)
						{
							if (extra != target)
							{
								ShotResult extraResult = ApplyPiercingDamage(extra);
								killedAny |= extraResult.killedAny;
							}
						}
					}

					isFirstShot = false;
					lockOnRingRenderer.enabled = false;

					if (UseNewVisuals)
					{
						TriggerRecoil();
						yield return StartCoroutine(TracerRoutine(farthestPoint));
						if (killedAny) TriggerKillKick(farthestPoint);
					}
					else
					{
						laserLine.startWidth = 0.15f;
						laserLine.endWidth = 0.05f;
						laserLine.startColor = Color.yellow;
						laserLine.endColor = new Color(1f, 0.5f, 0f);
					}

					yield return afterShotWait;

					laserLine.enabled = false;
					yield return cooldownWait;
				}
				else
				{
					laserLine.enabled = false;
					lockOnRingRenderer.enabled = false;
					yield return targetLostWait;
				}
			}
			else
			{
				laserLine.enabled = false;
				lockOnRingRenderer.enabled = false;
				yield return noTargetWait;
			}
		}
	}

	// ── feature/sniper-feel: shot/placement visuals ─────────────────────

	private IEnumerator PlacementPopRoutine()
	{
		visualTransform.localScale = Vector3.zero;
		const float duration = 0.15f;
		float t = 0f;

		while (t < 1f)
		{
			t += Time.deltaTime / duration;
			float eased = BackEaseOut(Mathf.Clamp01(t));
			visualTransform.localScale = visualBaseScale * eased;
			yield return null;
		}

		visualTransform.localScale = visualBaseScale;
		VfxManager.Play(VfxId.PlacePop, transform.position + Vector3.up * 0.3f, 1f);
	}

	private static float BackEaseOut(float t)
	{
		const float c1 = 1.70158f;
		const float c3 = c1 + 1f;
		float tm1 = t - 1f;
		return 1f + c3 * tm1 * tm1 * tm1 + c1 * tm1 * tm1;
	}

	private void TriggerRecoil()
	{
		SniperFeelTuning tuning = Tuning;
		if (tuning == null) return;

		if (recoilRoutine != null) StopCoroutine(recoilRoutine);
		recoilRoutine = StartCoroutine(RecoilRoutine(tuning.recoilKick, tuning.recoilDuration));
	}

	private IEnumerator RecoilRoutine(float kick, float duration)
	{
		float dur = Mathf.Max(0.01f, duration);
		float t = 0f;

		while (t < 1f)
		{
			t += Time.deltaTime / dur;
			float bump = Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI) * kick;
			visualTransform.localScale = Vector3.Scale(visualBaseScale, new Vector3(1f - bump * 0.5f, 1f + bump, 1f - bump * 0.5f));
			yield return null;
		}

		visualTransform.localScale = visualBaseScale;
		recoilRoutine = null;
	}

	private IEnumerator TracerRoutine(Vector3 farthestPoint)
	{
		SniperFeelTuning tuning = Tuning;
		float duration = Mathf.Max(0.01f, tuning.tracerDuration);
		Vector3 muzzle = transform.position + Vector3.up * muzzleHeight;

		laserLine.enabled = true;

		float worldPerPixel = ComputeWorldPerPixel(muzzle);
		float width = Mathf.Max(0.03f, tuning.laserMinPixelWidth * worldPerPixel * 1.5f);
		laserLine.startWidth = width;
		laserLine.endWidth = width * 0.5f;

		float t = 0f;
		while (t < 1f)
		{
			t += Time.deltaTime / duration;
			float clamped = Mathf.Clamp01(t);

			Vector3 head = Vector3.Lerp(muzzle, farthestPoint, Mathf.Min(1f, clamped * 1.6f));
			laserLine.SetPosition(0, muzzle);
			laserLine.SetPosition(1, head);

			float alpha = Mathf.Lerp(1f, 0f, clamped);
			laserLine.startColor = new Color(1f, 0.95f, 0.3f, alpha);
			laserLine.endColor = new Color(1f, 0.55f, 0.1f, alpha);

			yield return null;
		}
	}

	private void TriggerKillKick(Vector3 position)
	{
		SniperFeelTuning tuning = Tuning;
		if (tuning == null) return;
		if (Time.time - lastKillKickTime < 0.3f) return;
		lastKillKickTime = Time.time;

		CameraShake.Shake(tuning.killCameraKick, 0.15f);
		Feedback.Play(FeedbackEvent.SniperKill, position);
	}

	private void UpdateLockOnRing(Vector3 center, float radius, float alpha)
	{
		lockOnRingRenderer.enabled = true;

		Color c = LevelPaletteService.Active != null ? LevelPaletteService.Active.spawnMarkerColor : Color.red;
		c.a = alpha;
		lockOnRingRenderer.startColor = c;
		lockOnRingRenderer.endColor = c;

		float worldPerPixel = ComputeWorldPerPixel(center);
		float width = Mathf.Max(0.02f, 2f * worldPerPixel);
		lockOnRingRenderer.startWidth = width;
		lockOnRingRenderer.endWidth = width;

		const int segments = 24;
		lockOnRingRenderer.positionCount = segments + 1;
		for (int i = 0; i <= segments; i++)
		{
			float angle = Mathf.Deg2Rad * (360f * i / segments);
			Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
			lockOnRingRenderer.SetPosition(i, center + offset + Vector3.up * 0.05f);
		}
	}

	private void UpdateLifetimeArc()
	{
		SniperFeelTuning tuning = Tuning;
		bool show = UseNewVisuals && tuning != null && tuning.lifetimeArc && !isExtracting;
		lifetimeArcRenderer.enabled = show;
		if (!show) return;

		float fraction = maxLifespan > 0f ? Mathf.Clamp01(lifespan / maxLifespan) : 0f;
		int segments = Mathf.Max(2, Mathf.RoundToInt(32 * fraction));
		const float totalArcDegrees = 300f; // leave a gap so it never reads as a full ring (reserved for the lock-on ring)
		float sweepDegrees = totalArcDegrees * fraction;

		float worldPerPixel = ComputeWorldPerPixel(transform.position);
		float width = Mathf.Max(0.03f, 3f * worldPerPixel);
		lifetimeArcRenderer.startWidth = width;
		lifetimeArcRenderer.endWidth = width;
		lifetimeArcRenderer.startColor = new Color(1f, 1f, 1f, 0.85f);
		lifetimeArcRenderer.endColor = new Color(1f, 1f, 1f, 0.85f);

		const float radius = 1.3f;
		lifetimeArcRenderer.positionCount = segments + 1;
		for (int i = 0; i <= segments; i++)
		{
			float angle = Mathf.Deg2Rad * (sweepDegrees * i / segments - 90f);
			Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
			lifetimeArcRenderer.SetPosition(i, transform.position + offset + Vector3.up * 0.1f);
		}
	}

	// World units covered by one screen pixel at the given world point -- same ortho/perspective
	// split CameraZoomPanController.ApplyScreenPan already uses, reimplemented here (read-only,
	// camera code itself isn't touched) so the aim laser/rings never go sub-pixel-thin at any zoom.
	private static float ComputeWorldPerPixel(Vector3 referencePoint)
	{
		Camera cam = Camera.main;
		if (cam == null) return 0.01f;

		if (cam.orthographic)
			return (2f * cam.orthographicSize) / Mathf.Max(1f, Screen.height);

		float dist = Vector3.Distance(cam.transform.position, referencePoint);
		return (2f * dist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad)) / Mathf.Max(1f, Screen.height);
	}

	// ── targeting ────────────────────────────────────────────────────

	private Zombie FindTarget()
	{
		SniperFeelTuning tuning = Tuning;
		bool useV2 = tuning != null && tuning.useTargetingV2;
		return useV2 ? FindTargetV2() : FindTargetLegacy();
	}

	// feature/sniper-feel (step 2): direction-agnostic priority targeting, replacing the old
	// Z-axis-progress fallback (wrong assumption for a generated grid city with no single "forward"
	// lane). Priority 1 (civilian threat) is byte-for-byte the old logic. Priorities 2-4: a zombie
	// pinned at an active Barricade > the candidate with the best pierce line (kills weighted over
	// mere hits) > nearest. LOS and range are filtered up front for every candidate, same as before.
	private Zombie FindTargetV2()
	{
		CollectCivilians();
		rangeCandidatesBuffer.Clear();

		Zombie bestThreatTarget = null;
		float bestThreatScore = float.PositiveInfinity;
		float threatRadiusSq = civilianThreatRadius * civilianThreatRadius;

		foreach (var z in Zombie.AllZombies)
		{
			if (z == null) continue;

			float d = Vector3.Distance(transform.position, z.transform.position);
			if (d > attackRange) continue;
			if (!HasLineOfSight(z.transform)) continue;

			rangeCandidatesBuffer.Add(z);

			float nearestCivilianDistSq = GetNearestCivilianDistanceSq(z.transform.position);
			if (nearestCivilianDistSq > threatRadiusSq) continue;

			float score = nearestCivilianDistSq - (z.transform.position.z * progressBias);
			if (score < bestThreatScore)
			{
				bestThreatScore = score;
				bestThreatTarget = z;
			}
		}

		if (bestThreatTarget != null) return bestThreatTarget;
		if (rangeCandidatesBuffer.Count == 0) return null;

		// Priority 2: pinned at an active Barricade (siege mode or simply standing next to one) -- nearest among them.
		Zombie bestPinned = null;
		float bestPinnedDistSq = float.PositiveInfinity;
		foreach (var z in rangeCandidatesBuffer)
		{
			if (!IsPinnedAtBarricade(z)) continue;

			float dSq = (z.transform.position - transform.position).sqrMagnitude;
			if (dSq < bestPinnedDistSq)
			{
				bestPinnedDistSq = dSq;
				bestPinned = z;
			}
		}
		if (bestPinned != null) return bestPinned;

		// Priority 3: best pierce line (a tiny distance tiebreak folds priority 4 "nearest" into this
		// in the common case -- see ScorePierceLine).
		Zombie bestPierce = null;
		float bestPierceScore = float.NegativeInfinity;
		foreach (var z in rangeCandidatesBuffer)
		{
			float score = ScorePierceLine(z);
			if (score > bestPierceScore)
			{
				bestPierceScore = score;
				bestPierce = z;
			}
		}
		if (bestPierce != null) return bestPierce;

		// Priority 4: nearest (defensive fallback; priority 3 above already covers every candidate).
		Zombie nearest = null;
		float bestDistSq = float.PositiveInfinity;
		foreach (var z in rangeCandidatesBuffer)
		{
			float dSq = (z.transform.position - transform.position).sqrMagnitude;
			if (dSq < bestDistSq)
			{
				bestDistSq = dSq;
				nearest = z;
			}
		}
		return nearest;
	}

	private bool IsPinnedAtBarricade(Zombie z)
	{
		if (z.CurrentMode == Zombie.ZombieMode.Siege) return true;

		float checkRadius = z.crowdTuning != null ? z.crowdTuning.barricadeCheckRadius : 3.5f;
		float checkRadiusSq = checkRadius * checkRadius;

		foreach (var b in Barricade.AllBarricades)
		{
			if (b == null || b.IsDead) continue;
			if ((b.transform.position - z.transform.position).sqrMagnitude <= checkRadiusSq) return true;
		}

		return false;
	}

	// Simulates the same corridor/falloff ApplyPiercingDamage would actually fire at this candidate
	// (same max distance, same maxPierceTargets/pierceDamageFalloff), scoring kills (using each
	// zombie's maxHealth as a full-health proxy -- current HP isn't exposed) above mere hits, with a
	// tiny distance tiebreak so among equal corridors the nearer candidate wins.
	private float ScorePierceLine(Zombie candidate)
	{
		Vector3 start = transform.position + Vector3.up * muzzleHeight;
		Vector3 end = candidate.transform.position + Vector3.up * targetHeight;
		Vector3 toEnd = end - start;
		float dist = toEnd.magnitude;
		if (dist <= 0.01f) return 0f;

		int hitCount = PhysicsNonAlloc.Raycast(start, toEnd / dist, dist, ref piercingHitsBuffer);
		System.Array.Sort(piercingHitsBuffer, 0, hitCount, RaycastHitDistanceComparer.Instance);

		int hits = 0;
		int kills = 0;
		float currentDamage = damage;

		for (int i = 0; i < hitCount && hits < maxPierceTargets; i++)
		{
			RaycastHit hit = piercingHitsBuffer[i];
			if (hit.collider.CompareTag("Building") && (myBuilding == null || hit.collider.transform != myBuilding))
				break;

			Zombie z = hit.collider.GetComponent<Zombie>();
			if (z == null) continue;

			hits++;
			if (currentDamage >= z.maxHealth) kills++;

			currentDamage *= pierceDamageFalloff;
			if (currentDamage < 1f) break;
		}

		float distancePenalty = dist * 0.001f;
		return kills * 2f + hits - distancePenalty;
	}

	// Old targeting, unchanged: best zombie by Z progress within LOS (reachable via the
	// SandboxDevPanel "Sniper feel: Old" toggle / SniperFeelTuning.useTargetingV2 = false).
	private Zombie FindTargetLegacy()
	{
		CollectCivilians();

		Zombie bestThreatTarget = null;
		float bestThreatScore = float.PositiveInfinity;

		Zombie bestFallbackTarget = null;
		float bestFallbackProgress = float.NegativeInfinity;

		float threatRadiusSq = civilianThreatRadius * civilianThreatRadius;

		foreach (var z in Zombie.AllZombies)
		{
			if (z == null) continue;

			float d = Vector3.Distance(transform.position, z.transform.position);
			if (d > attackRange) continue;
			if (!HasLineOfSight(z.transform)) continue;

			float progressScore = z.transform.position.z;
			if (progressScore > bestFallbackProgress)
			{
				bestFallbackProgress = progressScore;
				bestFallbackTarget = z;
			}

			float nearestCivilianDistSq = GetNearestCivilianDistanceSq(z.transform.position);
			if (nearestCivilianDistSq > threatRadiusSq) continue;

			float score = nearestCivilianDistSq - (z.transform.position.z * progressBias);

			if (score < bestThreatScore)
			{
				bestThreatScore = score;
				bestThreatTarget = z;
			}
		}

		if (bestThreatTarget != null)
			return bestThreatTarget;

		return bestFallbackTarget;
	}

	private void CollectCivilians()
	{
		civilianBuffer.Clear();

		foreach (var h in Human.AllHumans)
		{
			if (h == null) continue;
			if (!h.isActiveAndEnabled) continue;
			civilianBuffer.Add(h.transform);
		}

		foreach (var s in Scientist.AllScientists)
		{
			if (s == null) continue;
			if (!s.isActiveAndEnabled) continue;
			civilianBuffer.Add(s.transform);
		}
	}

	private float GetNearestCivilianDistanceSq(Vector3 zombiePos)
	{
		if (civilianBuffer.Count == 0)
			return float.PositiveInfinity;

		float bestDistSq = float.PositiveInfinity;

		for (int i = 0; i < civilianBuffer.Count; i++)
		{
			Transform civ = civilianBuffer[i];
			if (civ == null) continue;

			Vector3 delta = civ.position - zombiePos;
			delta.y = 0f; // ignore vertical difference

			float distSq = delta.sqrMagnitude;
			if (distSq < bestDistSq)
				bestDistSq = distSq;
		}

		return bestDistSq;
	}

	private bool HasLineOfSight(Transform target)
	{
		Vector3 start = transform.position + Vector3.up * muzzleHeight;
		Vector3 end = target.position + Vector3.up * targetHeight;
		Vector3 dir = end - start;
		float dist = dir.magnitude;

		if (dist <= 0.01f) return true;

		if (Physics.Raycast(start, dir.normalized, out RaycastHit hit, dist))
		{
			Zombie hitZombie = hit.collider.GetComponent<Zombie>();
			if (hitZombie != null && hitZombie.transform == target)
				return true;

			if (hit.collider.CompareTag("Building"))
			{
				if (myBuilding != null && hit.collider.transform == myBuilding)
					return true;

				return false;
			}

			if (hit.collider.transform != target)
				return false;
		}

		return true;
	}

	private ShotResult ApplyPiercingDamage(Zombie primaryTarget)
	{
		Vector3 start = transform.position + Vector3.up * muzzleHeight;
		Vector3 end = primaryTarget.transform.position + Vector3.up * targetHeight;
		Vector3 dir = (end - start).normalized;
		float dist = Vector3.Distance(start, end);

		int hitCount = PhysicsNonAlloc.Raycast(start, dir, dist, ref piercingHitsBuffer);
		System.Array.Sort(piercingHitsBuffer, 0, hitCount, RaycastHitDistanceComparer.Instance);

		int targetsHit = 0;
		float currentDamage = damage;
		Vector3 farthestPoint = end;
		bool killedAny = false;

		for (int i = 0; i < hitCount; i++)
		{
			RaycastHit hit = piercingHitsBuffer[i];
			if (targetsHit >= maxPierceTargets) break;

			if (hit.collider.CompareTag("Building") && (myBuilding == null || hit.collider.transform != myBuilding))
			{
				break;
			}

			Zombie z = hit.collider.GetComponent<Zombie>();
			if (z != null)
			{
				bool wasAlive = !z.IsDead;
				int actualDamage = (RunSessionData.Instance != null && RunSessionData.Instance.HasFlag("sniper_instakill"))
					? 99999
					: Mathf.RoundToInt(currentDamage);
				z.TakeDamage(actualDamage);
				if (wasAlive && z.IsDead) killedAny = true;

				farthestPoint = hit.point;
				targetsHit++;

				currentDamage *= pierceDamageFalloff;
				if (currentDamage < 1f) break;
			}
		}

		if (targetsHit == 0 && primaryTarget != null)
		{
			bool wasAlive = !primaryTarget.IsDead;
			primaryTarget.TakeDamage(damage);
			if (wasAlive && primaryTarget.IsDead) killedAny = true;
			farthestPoint = end;
		}

		return new ShotResult { farthestPoint = farthestPoint, killedAny = killedAny };
	}

	private System.Collections.Generic.List<Zombie> FindTopNTargets(int n)
	{
		var result = new System.Collections.Generic.List<Zombie>();
		foreach (var z in Zombie.AllZombies)
		{
			if (z == null) continue;
			float d = Vector3.Distance(transform.position, z.transform.position);
			if (d <= attackRange)
				result.Add(z);
		}
		result.Sort((a, b) => Vector3.Distance(transform.position, a.transform.position)
			.CompareTo(Vector3.Distance(transform.position, b.transform.position)));
		if (result.Count > n) result.RemoveRange(n, result.Count - n);
		return result;
	}

}
