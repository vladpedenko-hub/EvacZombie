using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.AI;

public class Bait : MonoBehaviour
{
	public static List<Bait> AllBaits = new List<Bait>();

	[HideInInspector] public float attractRadius;
	[HideInInspector] public Vector3 attractPoint;
	[HideInInspector] public bool hasValidAttractPoint;

	[Header("Card & Settings")]
	public CardData myCardData;

	[Header("Radius Ring")]
	public float ringYOffset = 0.08f;
	public float ringWidth = 0.15f;
	public int ringSegments = 48;
	public Color ringColor = new Color(1f, 0.45f, 0.1f, 0.9f);
	public float pulseScaleMultiplier = 1.12f;
	public float pulseDuration = 0.75f;

	[Header("Attract Point Visual")]
	public Transform attractPointVisual;

	[Header("Audio")]
	public AudioSource audioSource;
	public AudioClip[] pingClips;
	public int pingCount = 3;
	public float pingInterval = 0.8f;
	public float pingVolume = 1f;

	[Header("Debug")]
	public float navMeshSearchRadius = 6f; // overwritten from BaitTuning in InitStatsFromCardData
	public bool drawGizmos = true;

	// feature/bait-rework (step 3): crowd heat -- a plain counter, no per-frame allocation. Reported
	// by Zombie.LatchOntoBait/OnTakenFromPool/OnReturnedToPool; never read/written from Bait itself.
	public int LatchedCount { get; private set; }
	private bool crowdCapReported;

	private float lifeTime = 5f;
	private float maxLifeTime = 5f;
	private float lifeElapsed;
	private bool statsInitialized = false;
	private bool launched = false;
	private bool isFadingOut = false;

	// Cached so PingRoutine's repeated yields don't allocate a new WaitForSeconds each time.
	private WaitForSeconds pingIntervalWait;
	private WaitForSeconds lifeTimeWait;

	private LineRenderer radiusRenderer;
	private GameObject radiusRingObject;
	private Coroutine pingRoutine;

	// feature/bait-rework: all animation (pop-in, pulse via the ring redraw) happens on this
	// transform, never the root. attractPointVisual isn't wired on Bait.prefab (fileID: 0) -- created
	// and the mesh moved onto it the first time, same spirit as Barricade/Sniper's "Visual" child.
	private Vector3 visualBaseScale = Vector3.one;

	private static BaitTuning Tuning => BaitTuning.Active;

	private void OnEnable()
	{
		if (!AllBaits.Contains(this))
			AllBaits.Add(this);
	}

	private void OnDisable()
	{
		AllBaits.Remove(this);
		KillVisuals();
	}

	private void Start()
	{
		InitStatsFromCardData();
	}

	private void Update()
	{
		if (!launched) return;

		lifeElapsed += Time.deltaTime;
		if (!isFadingOut) UpdateRing();
	}

	private void InitStatsFromCardData()
	{
		if (statsInitialized) return;
		statsInitialized = true;

		int currentLevel = 1;

		if (PlayerProfile.Instance != null && myCardData != null)
		{
			var progress = PlayerProfile.Instance.ownedCardsProgress.Find(p => p.cardId == myCardData.name);
			if (progress != null) currentLevel = progress.currentLevel;

			attractRadius = myCardData.GetCalculatedStat(StatType.Radius, currentLevel);

			float duration = myCardData.GetCalculatedStat(StatType.Duration, currentLevel);
			if (duration > 0f) lifeTime = duration;
		}
		else
		{
			Debug.LogWarning("Bait has no CardData assigned! Using fallback values.");
			attractRadius = 10f;
			lifeTime = 5f;
		}

		if (attractRadius <= 0f) attractRadius = 10f;
		if (lifeTime <= 0f) lifeTime = 5f;

		// feature/bait-rework (step 1): durationMult/radiusMult stack ON TOP of the CardData-derived
		// values above (CardData itself is never edited). legacyFeelEnabled reverts both to x1 and
		// the snap distance to the old hardcoded 6m (SandboxDevPanel "Bait: Old" toggle).
		BaitTuning tuning = Tuning;
		bool legacy = tuning != null && tuning.legacyFeelEnabled;

		if (tuning != null && !legacy)
		{
			attractRadius *= tuning.radiusMult;
			lifeTime *= tuning.durationMult;
			navMeshSearchRadius = tuning.navSnapMaxDistance;
		}
		else
		{
			navMeshSearchRadius = 6f;
		}

		maxLifeTime = lifeTime;

		pingIntervalWait = new WaitForSeconds(pingInterval);
		lifeTimeWait = new WaitForSeconds(lifeTime);
	}

	public void Launch(Vector3 pos)
	{
		if (!statsInitialized)
			InitStatsFromCardData();

		launched = true;
		transform.position = pos;

		ResolveVisualRoot();
		ResolveAttractPoint(pos);
		SetupRadiusRing();
		StartPingIfNeeded();

		BaitTuning tuning = Tuning;
		bool legacy = tuning != null && tuning.legacyFeelEnabled;

		if (attractPointVisual != null)
		{
			attractPointVisual.localScale = Vector3.zero;
			attractPointVisual.DOScale(visualBaseScale, 0.15f).SetEase(Ease.OutBack).SetLink(gameObject);
		}

		if (!legacy && tuning != null && tuning.placementRipple)
			StartCoroutine(RippleRoutine(attractRadius));

		StartCoroutine(LifeRoutine());
	}

	// feature/bait-rework: Bait.prefab's mesh lives directly on the root GameObject and
	// attractPointVisual is unassigned there -- move the mesh onto a new child by copying the
	// mesh/material references across, then remove the root's own renderer so nothing draws twice.
	private void ResolveVisualRoot()
	{
		if (attractPointVisual == null)
			attractPointVisual = transform.Find("Visual");

		if (attractPointVisual == null)
		{
			var visualGo = new GameObject("Visual");
			visualGo.transform.SetParent(transform, false);

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

			attractPointVisual = visualGo.transform;
		}

		visualBaseScale = attractPointVisual.localScale;
		if (visualBaseScale == Vector3.zero) visualBaseScale = Vector3.one;
	}

	private void ResolveAttractPoint(Vector3 desiredPoint)
	{
		if (NavMesh.SamplePosition(desiredPoint, out NavMeshHit hit, navMeshSearchRadius, NavMesh.AllAreas))
		{
			attractPoint = hit.position;
			hasValidAttractPoint = true;
		}
		else
		{
			attractPoint = desiredPoint;
			hasValidAttractPoint = false;
		}

		if (attractPointVisual != null)
		{
			attractPointVisual.position = attractPoint + Vector3.up * 0.05f;
			attractPointVisual.rotation = Quaternion.Euler(90f, 0f, 0f);
		}
	}

	private void SetupRadiusRing()
	{
		if (radiusRingObject != null)
		{
			Destroy(radiusRingObject);
		}

		radiusRingObject = new GameObject("BaitRadiusRing");
		radiusRingObject.transform.SetParent(transform, true);
		radiusRingObject.transform.position = new Vector3(attractPoint.x, attractPoint.y + ringYOffset, attractPoint.z);
		radiusRingObject.transform.rotation = Quaternion.identity;
		radiusRingObject.transform.localScale = Vector3.one;

		radiusRenderer = radiusRingObject.AddComponent<LineRenderer>();
		radiusRenderer.useWorldSpace = true;
		radiusRenderer.loop = true;
		radiusRenderer.positionCount = ringSegments;
		radiusRenderer.startWidth = ringWidth;
		radiusRenderer.endWidth = ringWidth;
		radiusRenderer.material = SharedMaterials.SpritesDefaultMaterial;
		radiusRenderer.startColor = ringColor;
		radiusRenderer.endColor = ringColor;
		radiusRenderer.sortingOrder = 10;

		DrawRing(radiusRenderer, attractRadius);
	}

	private void DrawRing(LineRenderer lr, float radius)
	{
		if (lr == null) return;

		Vector3 center = new Vector3(attractPoint.x, attractPoint.y + ringYOffset, attractPoint.z);
		float angleStep = 360f / ringSegments;

		if (lr.positionCount != ringSegments) lr.positionCount = ringSegments;

		for (int i = 0; i < ringSegments; i++)
		{
			float angle = Mathf.Deg2Rad * (i * angleStep);
			float x = Mathf.Cos(angle) * radius;
			float z = Mathf.Sin(angle) * radius;
			lr.SetPosition(i, center + new Vector3(x, 0f, z));
		}
	}

	// feature/bait-rework (step 3/4): replaces the old DOTween-only pulse loop so the pulse, the
	// crowd-heat width/color, and the life shrink/fade/blink can all compose on the same ring without
	// two things (a Tween + this) fighting over the same LineRenderer every frame.
	private void UpdateRing()
	{
		if (radiusRenderer == null) return;

		BaitTuning tuning = Tuning;
		bool legacy = tuning != null && tuning.legacyFeelEnabled;

		float pulseT = pulseDuration > 0f ? Mathf.PingPong(Time.time / pulseDuration, 1f) : 0f;
		float pulseFactor = Mathf.Lerp(1f, pulseScaleMultiplier, Mathf.SmoothStep(0f, 1f, pulseT));

		bool shrinkEnabled = !legacy && tuning != null && tuning.shrinkWithLife;
		float lifeFraction = maxLifeTime > 0f ? Mathf.Clamp01(1f - lifeElapsed / maxLifeTime) : 1f;
		float shrinkFactor = shrinkEnabled ? Mathf.Lerp(0.5f, 1f, lifeFraction) : 1f;
		float lifeAlpha = shrinkEnabled ? Mathf.Lerp(0.25f, 1f, lifeFraction) : 1f;

		if (shrinkEnabled && lifeFraction <= tuning.lowLifeBlinkFraction)
			lifeAlpha *= Mathf.Lerp(0.25f, 1f, Mathf.PingPong(Time.time * 6f, 1f));

		DrawRing(radiusRenderer, attractRadius * pulseFactor * shrinkFactor);

		float heatFraction = 0f;
		if (!legacy && tuning != null && tuning.heatZombiesForMax > 0)
			heatFraction = Mathf.Clamp01((float)LatchedCount / tuning.heatZombiesForMax);

		float baseWidth = (!legacy && tuning != null) ? tuning.ringBaseWidth : ringWidth;
		float maxMult = (!legacy && tuning != null) ? tuning.ringMaxWidthMult : 1f;
		float width = Mathf.Lerp(baseWidth, baseWidth * maxMult, heatFraction);
		radiusRenderer.startWidth = width;
		radiusRenderer.endWidth = width;

		Color heatColor = Color.Lerp(ringColor, Color.Lerp(ringColor, Color.white, 0.4f), heatFraction * 0.6f);
		heatColor.a = ringColor.a * lifeAlpha;
		radiusRenderer.startColor = heatColor;
		radiusRenderer.endColor = heatColor;
	}

	// feature/bait-rework (step 5): one expanding, fading ring ping out to the final radius -- a
	// temporary, self-destroying LineRenderer, separate from the main (persistent) radiusRenderer.
	private IEnumerator RippleRoutine(float targetRadius)
	{
		var rippleGo = new GameObject("BaitRipple");
		rippleGo.transform.SetParent(transform, true);

		var rippleRenderer = rippleGo.AddComponent<LineRenderer>();
		rippleRenderer.useWorldSpace = true;
		rippleRenderer.loop = true;
		rippleRenderer.positionCount = ringSegments;
		rippleRenderer.material = SharedMaterials.SpritesDefaultMaterial;
		rippleRenderer.sortingOrder = 9;

		const float duration = 0.4f;
		float t = 0f;

		while (t < 1f)
		{
			t += Time.deltaTime / duration;
			float clamped = Mathf.Clamp01(t);

			float radius = Mathf.Lerp(0f, targetRadius, clamped);
			Color c = ringColor;
			c.a = Mathf.Lerp(0.8f, 0f, clamped);
			rippleRenderer.startColor = c;
			rippleRenderer.endColor = c;
			rippleRenderer.startWidth = ringWidth * 0.6f;
			rippleRenderer.endWidth = ringWidth * 0.6f;

			DrawRing(rippleRenderer, radius);
			yield return null;
		}

		Destroy(rippleGo);
	}

	private void StartPingIfNeeded()
	{
		if (audioSource != null && pingClips != null && pingClips.Length > 0)
		{
			pingRoutine = StartCoroutine(PingRoutine());
		}
	}

	private IEnumerator PingRoutine()
	{
		int count = Mathf.Max(1, pingCount);

		for (int i = 0; i < count; i++)
		{
			AudioClip clip = pingClips[Random.Range(0, pingClips.Length)];
			if (clip != null && AudioManager.SfxAllowed)
				audioSource.PlayOneShot(clip, pingVolume);

			if (i < count - 1)
				yield return pingIntervalWait;
		}
	}

	private IEnumerator LifeRoutine()
	{
		yield return lifeTimeWait;
		yield return StartCoroutine(FadeOutRoutine());
		Destroy(gameObject);
	}

	// feature/bait-rework (step 4): replaces the abrupt Destroy() with a short fade of the ring +
	// attract-point visual.
	private IEnumerator FadeOutRoutine()
	{
		isFadingOut = true;
		const float fadeDuration = 0.3f;

		Color startColor = radiusRenderer != null ? radiusRenderer.startColor : Color.clear;
		Vector3 startScale = attractPointVisual != null ? attractPointVisual.localScale : Vector3.zero;

		float t = 0f;
		while (t < 1f)
		{
			t += Time.deltaTime / fadeDuration;
			float clamped = Mathf.Clamp01(t);

			if (radiusRenderer != null)
			{
				Color c = startColor;
				c.a = Mathf.Lerp(startColor.a, 0f, clamped);
				radiusRenderer.startColor = c;
				radiusRenderer.endColor = c;
			}

			if (attractPointVisual != null)
				attractPointVisual.localScale = Vector3.Lerp(startScale, Vector3.zero, clamped);

			yield return null;
		}
	}

	// feature/bait-rework (step 7): LevelPaletteApplier calls this with LevelPalette.baitColor on
	// generated levels (and on a live sandbox palette cycle). UpdateRing() already recomputes the
	// live heat/life-blended color from ringColor every frame, so updating the base field is enough.
	public void ApplyPaletteTint(Color color)
	{
		ringColor = color;
	}

	// feature/bait-rework (step 3): called by Zombie.LatchOntoBait/OnTakenFromPool/OnReturnedToPool.
	// Plain counter, no per-frame allocation, never leaks across pool reuse (unlatch is reported
	// before a pooled zombie's currentBait is cleared).
	public void ReportLatch()
	{
		LatchedCount++;

		BaitTuning tuning = Tuning;
		if (crowdCapReported || tuning == null || tuning.legacyFeelEnabled) return;
		if (tuning.heatZombiesForMax > 0 && LatchedCount >= tuning.heatZombiesForMax)
		{
			crowdCapReported = true;
			Feedback.Play(FeedbackEvent.BaitCrowd, transform.position);
		}
	}

	public void ReportUnlatch()
	{
		if (LatchedCount > 0) LatchedCount--;
	}

	private void KillVisuals()
	{
		if (attractPointVisual != null) attractPointVisual.DOKill();

		if (pingRoutine != null)
		{
			StopCoroutine(pingRoutine);
			pingRoutine = null;
		}

		if (radiusRingObject != null)
		{
			Destroy(radiusRingObject);
		}
	}

	private void OnDrawGizmosSelected()
	{
		if (!drawGizmos) return;

		Vector3 point = (launched || Application.isPlaying) ? attractPoint : transform.position;

		Gizmos.color = new Color(1f, 0.7f, 0.1f, 0.35f);
		Gizmos.DrawWireSphere(point, attractRadius);

		Gizmos.color = Color.red;
		Gizmos.DrawSphere(point + Vector3.up * 0.1f, 0.15f);
	}
}
