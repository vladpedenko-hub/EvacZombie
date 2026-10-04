using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;

// Visuals for one building. Primitives unless ruinedVisual / restoredVisual prefabs are assigned.
// Locked = dimmed + padlock. Available = pulsing ring + "!". Restored = clean house, fog gone.
public class MetaHouseView : MonoBehaviour
{
	private static readonly Color RuinedColor = new Color(0.2f, 0.18f, 0.16f);
	private static readonly Color DebrisColor = new Color(0.14f, 0.13f, 0.12f);
	private static readonly Color CleanColor = new Color(0.86f, 0.86f, 0.82f);
	private static readonly Color DoorColor = new Color(0.3f, 0.2f, 0.12f);
	private static readonly Color PadlockColor = new Color(0.55f, 0.55f, 0.6f);
	private static readonly Color RingColor = new Color(0.95f, 0.8f, 0.2f);
	private const float LockedTint = 0.45f;

	public BuildingDefinition Building { get; private set; }

	private GameObject ruined;
	private GameObject restored;
	private GameObject lockMarker;
	private GameObject availableMarker;
	private GameObject ring;
	private readonly List<(Renderer renderer, Color baseColor)> tinted = new List<(Renderer, Color)>();
	private Vector3 ringScale;
	private Tween pulseTween;
	private bool firstRefresh = true;

	public void Initialize(BuildingDefinition building)
	{
		Building = building;
		transform.position = new Vector3(building.worldPosition.x, 0f, building.worldPosition.y);

		// One collider on the root so taps hit the whole house.
		var collider = gameObject.AddComponent<BoxCollider>();
		collider.center = new Vector3(0f, 1.2f, 0f);
		collider.size = new Vector3(3.4f, 2.6f, 3.4f);

		ruined = building.ruinedVisual != null ? Instantiate(building.ruinedVisual, transform) : BuildRuined();
		restored = building.restoredVisual != null ? Instantiate(building.restoredVisual, transform) : BuildRestored();
		CacheTintable(ruined);
		CacheTintable(restored);

		ring = BuildRing();
		lockMarker = BuildLockMarker();
		availableMarker = BuildAvailableMarker();

		// Fog sits on the ground around the building (see MetaFogZone).
		MetaFogZone.Create(transform, building.zoneRadius);
	}

	private void OnDestroy() => pulseTween?.Kill();

	public void Refresh(BuildingStatus status)
	{
		bool isRestored = status.state == BuildingState.Restored;
		bool isLocked = status.state == BuildingState.Locked;
		bool isAvailable = status.state == BuildingState.Available;

		ruined.SetActive(!isRestored);
		restored.SetActive(isRestored);
		lockMarker.SetActive(isLocked);
		availableMarker.SetActive(isAvailable || status.state == BuildingState.AvailableNotAffordable);

		Tint(isLocked ? LockedTint : 1f);
		SetPulse(isAvailable);

		if (isRestored)
		{
			// Animate only a live restore; a building that was already restored at load just loses its fog.
			MetaFogZone fog = GetComponentInChildren<MetaFogZone>(true);
			if (fog != null)
			{
				if (firstRefresh) fog.HideImmediately();
				else fog.FadeOut();
			}
		}

		firstRefresh = false;
	}

	// --- Pulse ---

	private void SetPulse(bool on)
	{
		if (on && pulseTween == null)
		{
			ring.SetActive(true);
			ring.transform.localScale = ringScale;
			pulseTween = ring.transform.DOScale(ringScale * 1.15f, 0.7f)
				.SetLoops(-1, LoopType.Yoyo)
				.SetEase(Ease.InOutSine);
		}
		else if (!on && pulseTween != null)
		{
			pulseTween.Kill();
			pulseTween = null;
			ring.SetActive(false);
		}
		else if (!on)
		{
			ring.SetActive(false);
		}
	}

	// --- Tint (locked dims the house; works for primitives and prefabs) ---

	private void CacheTintable(GameObject root)
	{
		foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
			tinted.Add((r, r.material.GetColor("_BaseColor")));
	}

	private void Tint(float multiplier)
	{
		foreach (var (renderer, baseColor) in tinted)
		{
			if (renderer == null) continue;
			Color c = baseColor * multiplier;
			c.a = baseColor.a;
			renderer.material.SetColor("_BaseColor", c);
		}
	}

	// --- Primitive builders ---

	private GameObject BuildRuined()
	{
		var root = NewRoot("Ruined");

		Box("Shell", root, new Vector3(0f, 0.7f, 0f), new Vector3(3f, 1.4f, 3f), RuinedColor);
		Box("BrokenTop", root, new Vector3(0.4f, 1.55f, -0.3f), new Vector3(1.4f, 0.5f, 1.2f), RuinedColor, new Vector3(0f, 20f, 8f));

		// Debris: small tilted cubes around the base, placed deterministically per building id.
		var random = new System.Random(Seed(Building.id));
		for (int i = 0; i < 4; i++)
		{
			Vector3 pos = new Vector3(random.Next(-20, 21) / 10f, 0.2f, random.Next(-20, 21) / 10f);
			float size = random.Next(6, 10) / 10f;
			Vector3 tilt = new Vector3(random.Next(0, 40), random.Next(0, 360), random.Next(0, 40));
			Box("Debris" + i, root, pos, Vector3.one * size, DebrisColor, tilt);
		}
		return root.gameObject;
	}

	private GameObject BuildRestored()
	{
		var root = NewRoot("Restored");

		Box("Body", root, new Vector3(0f, 1.1f, 0f), new Vector3(3f, 2.2f, 3f), CleanColor);
		Box("Roof", root, new Vector3(0f, 2.45f, 0f), new Vector3(3.5f, 0.5f, 3.5f), Building.roofColor);
		Box("Door", root, new Vector3(0f, 0.5f, 1.52f), new Vector3(0.6f, 1f, 0.1f), DoorColor);
		return root.gameObject;
	}

	private GameObject BuildRing()
	{
		GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
		go.name = "AvailableRing";
		go.transform.SetParent(transform, false);
		go.transform.localPosition = new Vector3(0f, 0.03f, 0f);
		ringScale = new Vector3(2.8f, 0.02f, 2.8f);
		go.transform.localScale = ringScale;
		Destroy(go.GetComponent<Collider>());
		Paint(go.GetComponent<Renderer>(), RingColor);
		go.SetActive(false);
		return go;
	}

	private GameObject BuildLockMarker()
	{
		var root = NewRoot("LockMarker");
		Box("Padlock", root, new Vector3(0f, 3.4f, 0f), new Vector3(0.7f, 0.6f, 0.5f), PadlockColor);
		Label(root, "LOCK", new Vector3(0f, 4.1f, 0f), new Color(0.75f, 0.75f, 0.8f));
		root.gameObject.SetActive(false);
		return root.gameObject;
	}

	private GameObject BuildAvailableMarker()
	{
		var root = NewRoot("AvailableMarker");
		Label(root, "!", new Vector3(0f, 3.9f, 0f), RingColor);
		root.gameObject.SetActive(false);
		return root.gameObject;
	}

	private Transform NewRoot(string name)
	{
		var go = new GameObject(name);
		go.transform.SetParent(transform, false);
		return go.transform;
	}

	private static GameObject Box(string name, Transform parent, Vector3 localPos, Vector3 scale, Color color,
		Vector3 eulerAngles = default)
	{
		GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
		go.name = name;
		go.transform.SetParent(parent, false);
		go.transform.localPosition = localPos;
		go.transform.localScale = scale;
		go.transform.localEulerAngles = eulerAngles;
		Destroy(go.GetComponent<Collider>());
		Paint(go.GetComponent<Renderer>(), color);
		return go;
	}

	private static void Paint(Renderer renderer, Color color) => renderer.material.SetColor("_BaseColor", color);

	// Flat 3D text that reads from the top-down camera.
	private static void Label(Transform parent, string text, Vector3 localPos, Color color)
	{
		var go = new GameObject("Label_" + text);
		go.transform.SetParent(parent, false);
		go.transform.localPosition = localPos;
		go.transform.localEulerAngles = new Vector3(90f, 0f, 0f);
		go.transform.localScale = Vector3.one * 0.25f;

		TextMeshPro tmp = go.AddComponent<TextMeshPro>();
		tmp.text = text;
		tmp.fontSize = 20f;
		tmp.alignment = TextAlignmentOptions.Center;
		tmp.color = color;
	}

	private static int Seed(string text)
	{
		int seed = 17;
		foreach (char ch in text) seed = seed * 31 + ch;
		return seed;
	}
}
