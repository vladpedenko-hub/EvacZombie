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

	[Header("Связь с карточкой")]
	public CardData myCardData;

	[Header("Кольцо радиуса")]
	public float ringYOffset = 0.08f;
	public float ringWidth = 0.15f;
	public int ringSegments = 48;
	public Color ringColor = new Color(1f, 0.45f, 0.1f, 0.9f);
	public float pulseScaleMultiplier = 1.12f;
	public float pulseDuration = 0.75f;

	[Header("Точка притяжения")]
	public Transform attractPointVisual;

	[Header("Звук")]
	public AudioSource audioSource;
	public AudioClip[] pingClips;
	public int pingCount = 3;
	public float pingInterval = 0.8f;
	public float pingVolume = 1f;

	[Header("Прочее")]
	public float navMeshSearchRadius = 6f;
	public bool drawGizmos = true;

	private float lifeTime = 5f;
	private bool statsInitialized = false;
	private bool launched = false;

	private LineRenderer radiusRenderer;
	private GameObject radiusRingObject;
	private Tween ringPulseTween;
	private Coroutine pingRoutine;

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
			Debug.LogWarning("У приманки не назначен CardData! Используем базовые значения.");
			attractRadius = 10f;
			lifeTime = 5f;
		}

		if (attractRadius <= 0f) attractRadius = 10f;
		if (lifeTime <= 0f) lifeTime = 5f;
	}

	public void Launch(Vector3 pos)
	{
		if (!statsInitialized)
			InitStatsFromCardData();

		launched = true;
		transform.position = pos;

		ResolveAttractPoint(pos);
		SetupRadiusRing();
		StartRingPulse();
		StartPingIfNeeded();

		StartCoroutine(LifeRoutine());
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
		radiusRenderer.material = new Material(Shader.Find("Sprites/Default"));
		radiusRenderer.startColor = ringColor;
		radiusRenderer.endColor = ringColor;
		radiusRenderer.sortingOrder = 10;

		DrawRadiusRing(attractRadius);
	}

	private void DrawRadiusRing(float radius)
	{
		if (radiusRenderer == null) return;

		Vector3 center = new Vector3(attractPoint.x, attractPoint.y + ringYOffset, attractPoint.z);
		float angleStep = 360f / ringSegments;

		for (int i = 0; i < ringSegments; i++)
		{
			float angle = Mathf.Deg2Rad * (i * angleStep);
			float x = Mathf.Cos(angle) * radius;
			float z = Mathf.Sin(angle) * radius;
			radiusRenderer.SetPosition(i, center + new Vector3(x, 0f, z));
		}
	}

	private void StartRingPulse()
	{
		float startRadius = attractRadius;
		float targetRadius = attractRadius * pulseScaleMultiplier;

		ringPulseTween = DOTween.To(
			() => startRadius,
			value => DrawRadiusRing(value),
			targetRadius,
			pulseDuration)
			.SetEase(Ease.InOutSine)
			.SetLoops(-1, LoopType.Yoyo)
			.SetLink(gameObject);
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
			if (clip != null)
				audioSource.PlayOneShot(clip, pingVolume);

			if (i < count - 1)
				yield return new WaitForSeconds(pingInterval);
		}
	}

	private IEnumerator LifeRoutine()
	{
		yield return new WaitForSeconds(lifeTime);
		Destroy(gameObject);
	}

	private void KillVisuals()
	{
		ringPulseTween?.Kill();

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