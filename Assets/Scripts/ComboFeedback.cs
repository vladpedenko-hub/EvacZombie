using System.Collections;
using DG.Tweening;
using TMPro;
using UnityEngine;

// feature/bomb-combo (step 5/6): small, reusable "big moment" trigger -- slowmo + camera punch +
// world-space label + stinger SFX + VFX. Trigger(...) is the one public entry point; Bomb.cs is the
// first caller, but any future card can call it too. Auto-created, DontDestroyOnLoad, same
// Bootstrap pattern as VfxManager/CameraShake/AudioManager.
public class ComboFeedback : MonoBehaviour
{
	private static ComboFeedback instance;

	private float savedTimeScale = 1f;
	private bool slowmoActive;
	private Coroutine slowmoRoutine;

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
	private static void ResetStatics() => instance = null;

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void Bootstrap()
	{
		if (instance != null) return;
		var go = new GameObject("[ComboFeedback]");
		DontDestroyOnLoad(go);
		instance = go.AddComponent<ComboFeedback>();
	}

	private void Awake()
	{
		if (instance != null && instance != this) { Destroy(gameObject); return; }
		instance = this;
	}

	// "Always restore in OnDisable / scene unload" -- covers both a mid-slowmo scene change and
	// this singleton ever being destroyed.
	private void OnDisable()
	{
		RestoreTimeScaleIfNeeded();
		if (instance == this) instance = null;
	}

	public static void Trigger(string label, int count, Vector3 worldPos, int supplyRefund)
	{
		if (instance == null) return;
		instance.TriggerInternal(label, count, worldPos, supplyRefund);
	}

	private void TriggerInternal(string label, int count, Vector3 worldPos, int supplyRefund)
	{
		ComboTuning tuning = ComboTuning.Active;
		float scale = tuning != null ? tuning.slowmoScale : 0.25f;
		float duration = tuning != null ? tuning.slowmoDuration : 0.3f;
		float punch = tuning != null ? tuning.zoomPunch : 0.06f;
		float labelDuration = tuning != null ? tuning.labelDuration : 1f;

		// "Single owner, overlapping calls must not stack" -- a second Trigger() while slowmo is
		// already running restarts the countdown but keeps the ORIGINAL pre-slowmo timeScale as the
		// restore target, not whatever (already-slowed) value is currently active.
		// "Never run while paused (timeScale ~ 0)".
		if (!Mathf.Approximately(Time.timeScale, 0f))
		{
			if (slowmoRoutine != null)
			{
				StopCoroutine(slowmoRoutine);
			}
			else
			{
				savedTimeScale = Time.timeScale;
			}

			slowmoActive = true;
			slowmoRoutine = StartCoroutine(SlowmoRoutine(scale, duration));
		}

		CameraShake.Shake(punch, 0.15f);

		Feedback.Play(FeedbackEvent.ComboBlast, worldPos);
		// Ground shockwave ring + dust at the epicenter -- reuses the existing PlacePop (Ring shape)
		// and BarricadeDust VFX ids rather than adding new ones.
		VfxManager.Play(VfxId.PlacePop, worldPos, 2.5f);
		VfxManager.Play(VfxId.BarricadeDust, worldPos + Vector3.up * 0.2f, 1.8f);

		if (supplyRefund > 0)
			SupplyManager.Instance?.Gain(supplyRefund);

		// "If the Supply bar RectTransform can be found cheaply... otherwise just append '+N' to the
		// label" -- SupplyHud keeps its root RectTransform private with no public accessor, so this
		// goes with the label fallback.
		string text = supplyRefund > 0 ? $"{label} x{count}  +{supplyRefund}" : $"{label} x{count}";
		SpawnLabel(text, worldPos, labelDuration);
	}

	private IEnumerator SlowmoRoutine(float scale, float duration)
	{
		Time.timeScale = scale;

		float elapsed = 0f;
		while (elapsed < duration)
		{
			elapsed += Time.unscaledDeltaTime;
			yield return null;
		}

		Time.timeScale = savedTimeScale;
		slowmoActive = false;
		slowmoRoutine = null;
	}

	private void RestoreTimeScaleIfNeeded()
	{
		if (!slowmoActive) return;

		if (slowmoRoutine != null) StopCoroutine(slowmoRoutine);
		Time.timeScale = savedTimeScale;
		slowmoActive = false;
		slowmoRoutine = null;
	}

	// "Reuse the project's existing floating-text pattern if any" -- none found (checked
	// CardInfoPopup/TopHudV2/the popup UIs: all screen-space dialogs, nothing world-space/floating).
	// Built fresh: a 3D TextMeshPro (not the UGUI variant -- no Canvas needed) + the existing
	// Billboard component to face the camera. Uses DOTween.To with a plain color getter/setter
	// (not TextMeshPro's own .DOFade() extension) since this project's DOTween install isn't
	// confirmed to include the optional TMP module.
	private static void SpawnLabel(string text, Vector3 worldPos, float duration)
	{
		var go = new GameObject("ComboLabel");
		go.transform.position = worldPos + Vector3.up * 2f;
		go.AddComponent<Billboard>();

		var tmp = go.AddComponent<TextMeshPro>();
		tmp.text = text;
		tmp.fontSize = 6;
		tmp.alignment = TextAlignmentOptions.Center;
		tmp.color = new Color(1f, 0.25f, 0.15f, 1f);

		Renderer tmpRenderer = go.GetComponent<Renderer>();
		if (tmpRenderer != null) tmpRenderer.sortingOrder = 50;

		go.transform.localScale = Vector3.zero;

		float remain = Mathf.Max(0.1f, duration - 0.15f);

		Sequence seq = DOTween.Sequence();
		seq.Append(go.transform.DOScale(Vector3.one, 0.15f).SetEase(Ease.OutBack).SetUpdate(true));
		seq.Append(go.transform.DOMoveY(go.transform.position.y + 1.2f, remain).SetEase(Ease.OutSine).SetUpdate(true));
		seq.Join(DOTween.To(() => tmp.color, c => tmp.color = c, new Color(tmp.color.r, tmp.color.g, tmp.color.b, 0f), remain).SetUpdate(true));
		seq.SetUpdate(true);
		seq.OnComplete(() => Destroy(go));
		seq.SetLink(go);
	}
}
