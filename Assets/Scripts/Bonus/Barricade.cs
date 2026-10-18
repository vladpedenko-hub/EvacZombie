using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using System.Collections.Generic;

// Barricade — placed on the road, blocks passage via NavMeshObstacle.
// Zombies can attack and destroy it. Implements IDamageable, so
// ZombieBoss can also deal damage to it in rage mode.
[RequireComponent(typeof(NavMeshObstacle))]
[RequireComponent(typeof(Collider))]
public class Barricade : MonoBehaviour, IDamageable
{
    public static List<Barricade> AllBarricades = new List<Barricade>();

    // fix/barricade-jerk-wave1 (Bug 1): fired with the barricade's world position whenever one
    // is placed OR destroyed — carving/un-carving instantly changes which NavMesh paths are
    // valid, so CrowdFieldManager needs to react immediately (fresh field recompute + nearby
    // zombies re-deciding the same frame) instead of waiting for the next fieldRefresh/Brain tick.
    public static event System.Action<Vector3> OnBarricadeChanged;

    [Header("Card Data")]
    public CardData myCardData;

    [Header("HP (overridden from CardData.StatType.Health)")]
    public int maxHealth = 200;

    [Header("Hit Feedback (same as zombie)")]
    public Color hitFlashColor = new Color(1f, 0.2f, 0.2f);
    public float hitFlashDuration = 0.1f;
    public float deathShrinkDuration = 0.2f;

    // ── internal state ──────────────────────────────────────────────
    private int currentHealth;
    private bool isDead = false;
    public bool IsDead => isDead;

    private NavMeshObstacle obstacle;
    private Collider cachedCollider;

    // renderers + PropertyBlock (same pattern as in Zombie.cs)
    private Renderer[] allRenderers;
    private MaterialPropertyBlock propertyBlock;
    private Dictionary<Renderer, Color> originalColors = new Dictionary<Renderer, Color>();
    private Dictionary<Renderer, int> colorPropertyIds = new Dictionary<Renderer, int>();

    private Coroutine hitFlashRoutine;

    // Cached so HitFlashRoutine's repeated yield doesn't allocate a new WaitForSeconds per hit.
    private WaitForSeconds hitFlashWait;

    // Reused buffer for Physics.OverlapSphereNonAlloc — the two roguelite checks in TakeDamage
    // run back-to-back, each fully consumed before the next, so one buffer is safe to reuse.
    private Collider[] overlapBuffer = new Collider[8];

    // ── feature/barricade-feel ───────────────────────────────────────
    // StreetSegment this instance was snapped to at placement (InputManager.SetSegment), used by
    // InputManager to find "the same street's barricade" for the repair-on-place check. Null on
    // legacy (non-StreetSegment) levels -- repair is simply never offered there.
    public StreetSegment Segment { get; private set; }
    public void SetSegment(StreetSegment segment) => Segment = segment;
    public int MaxHealth => maxHealth;
    public bool IsFullHealth => currentHealth >= maxHealth;

    private static BarricadeFeelTuning Tuning => BarricadeFeelTuning.Active;
    private bool FeelEnabled => Tuning != null && !Tuning.legacyFeelEnabled;

    private bool indestructible;

    // All animation happens on this child, never on the root (collider/NavMeshObstacle must stay
    // put so carving/blocking is unaffected). Found by name on the existing prefab; created and the
    // existing children reparented under it if a variant prefab doesn't have one.
    private Transform visualTransform;
    private Vector3 visualBaseScale = Vector3.one;
    private Vector3 visualBasePosition;

    // The barricade's "true" base color before per-stage darkening -- from the palette if active,
    // else the prefab's own material color (see Awake). Re-set by ApplyPaletteTint so a sandbox
    // palette cycle mid-fight still darkens from the right starting point.
    private Color pureBaseColor = Color.white;
    private int currentStage = 1; // 1 = healthy, 2 = damaged, 3 = critical

    private GameObject[] crackQuads = new GameObject[4];

    private Coroutine visualScaleRoutine;
    private float lastHitChipTime = -999f;

    // ── lifecycle ───────────────────────────────────────────────────

    private void Awake()
    {
        obstacle = GetComponent<NavMeshObstacle>();
        cachedCollider = GetComponent<Collider>();

        // Carve NavMesh immediately on placement, without delay
        obstacle.carving = true;
        obstacle.carvingMoveThreshold = 0f;
        obstacle.carvingTimeToStationary = 0f;

        hitFlashWait = new WaitForSeconds(hitFlashDuration);

        ResolveVisualRoot();

        // Collect renderers once in Awake, same as in Zombie
        allRenderers = GetComponentsInChildren<Renderer>();
        propertyBlock = new MaterialPropertyBlock();

        foreach (Renderer r in allRenderers)
        {
            if (r == null) continue;
            Material mat = r.sharedMaterial;
            if (mat == null) continue;

            if (mat.HasProperty("_BaseColor"))
            {
                colorPropertyIds[r] = Shader.PropertyToID("_BaseColor");
                originalColors[r] = mat.GetColor("_BaseColor");
                pureBaseColor = originalColors[r];
            }
            else if (mat.HasProperty("_Color"))
            {
                colorPropertyIds[r] = Shader.PropertyToID("_Color");
                originalColors[r] = mat.GetColor("_Color");
                pureBaseColor = originalColors[r];
            }
        }

        // feature/readability-palette: generated-city-only (LevelPaletteService.Active is null on
        // hand-built levels). Barricades are never pooled, so Awake() alone covers every instance.
        if (LevelPaletteService.Active != null) ApplyPaletteTint(LevelPaletteService.Active.barricadeColor);
    }

    // feature/barricade-feel: finds the prefab's own "Visual" child (present on Barricade.prefab)
    // or, for any variant prefab that doesn't have one, creates it and reparents the existing
    // children under it. Either way every subsequent animation (drop, squash, punch) only ever
    // touches this child transform, never the root (collider/NavMeshObstacle stay untouched).
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
            foreach (Transform child in existingChildren) child.SetParent(visualGo.transform, true);

            visualTransform = visualGo.transform;
        }

        visualBaseScale = visualTransform.localScale;
        visualBasePosition = visualTransform.localPosition;
    }

    // feature/readability-palette: overwrites originalColors itself (not just the live property
    // block), so HitFlashRoutine's RestoreOriginalColors() restores to the palette tint rather
    // than this prefab's own material color.
    // feature/barricade-feel: also refreshes pureBaseColor and re-applies the CURRENT damage
    // stage's darkening on top of the new base, so a sandbox palette cycle mid-fight doesn't
    // reset a cracked barricade back to full brightness.
    public void ApplyPaletteTint(Color color)
    {
        pureBaseColor = color;
        RefreshStageColor();
    }

    private void RefreshStageColor()
    {
        Color stageColor = ComputeStageColor();

        for (int i = 0; i < allRenderers.Length; i++)
        {
            Renderer r = allRenderers[i];
            if (r == null || !colorPropertyIds.ContainsKey(r)) continue;
            originalColors[r] = stageColor;
        }

        SetAllRendererColors(stageColor);
    }

    private Color ComputeStageColor()
    {
        if (indestructible || currentStage <= 1) return pureBaseColor;

        BarricadeFeelTuning tuning = Tuning;
        float darken = tuning != null ? tuning.stageDarken : 0.12f;

        Color.RGBToHSV(pureBaseColor, out float h, out float s, out float v);
        v = Mathf.Clamp01(v - (currentStage - 1) * darken);
        return Color.HSVToRGB(h, s, v);
    }

    private void Start()
    {
        // Read HP from CardData if assigned
        if (myCardData != null && PlayerProfile.Instance != null)
        {
            int level = 1;
            var progress = PlayerProfile.Instance.ownedCardsProgress
                .Find(p => p.cardId == myCardData.name);
            if (progress != null) level = progress.currentLevel;

            int hpFromCard = (int)myCardData.GetCalculatedStat(StatType.Health, level);
            if (hpFromCard > 0) maxHealth = hpFromCard;
        }

        // Run-modifiers (roguelite)
        var run = RunSessionData.Instance;
        if (run != null)
        {
            float hpMult = 1f + run.GetModifier("barricade_hp_mult");
            maxHealth = Mathf.RoundToInt(maxHealth * hpMult);

            if (run.HasFlag("barricade_indestructible"))
            {
                maxHealth = 999999;
                indestructible = true;
            }

            float widthMult = run.GetModifier("barricade_width_mult");
            if (widthMult > 0f)
                transform.localScale *= (1f + widthMult);
        }

        currentHealth = maxHealth;

        // feature/barricade-feel: indestructible skips stages/cracks entirely (ComputeStageColor
        // already short-circuits on it); still re-run so the color/crack state matches that from
        // the start rather than only from the next TakeDamage.
        if (indestructible) RefreshStageColor();

        if (FeelEnabled)
            visualScaleRoutine = StartCoroutine(PlaceRoutine());
    }

    private void OnEnable()
    {
        AllBarricades.Add(this);
        OnBarricadeChanged?.Invoke(transform.position);
    }

    private void OnDisable() => AllBarricades.Remove(this);

    // ── IDamageable + public method for Zombie ─────────────────────────

    /// <summary>Called from ZombieBoss.BreakBuildingsAround (float overload of the interface).</summary>
    public void TakeDamage(float damage) => TakeDamage(Mathf.CeilToInt(damage));

    /// <summary>Called directly from Zombie.InfectTarget.</summary>
    public void TakeDamage(int damage)
    {
        if (isDead) return;

        // Roguelite: reflect damage and death zone
        var run = RunSessionData.Instance;
        if (run != null)
        {
            float reflectPct = run.GetModifier("barricade_reflect_pct");
            if (reflectPct > 0f)
            {
                int nearbyCount = PhysicsNonAlloc.OverlapSphere(transform.position, 3f, ref overlapBuffer);
                for (int i = 0; i < nearbyCount; i++)
                {
                    Zombie z = overlapBuffer[i]?.GetComponent<Zombie>();
                    if (z != null)
                    {
                        z.TakeDamage(Mathf.RoundToInt(damage * reflectPct));
                        break;
                    }
                }
            }

            if (run.HasFlag("barricade_death_zone"))
            {
                int deathZoneCount = PhysicsNonAlloc.OverlapSphere(transform.position, 1.5f, ref overlapBuffer);
                for (int i = 0; i < deathZoneCount; i++)
                    overlapBuffer[i]?.GetComponent<Zombie>()?.TakeDamage(99999);
            }
        }

        currentHealth -= damage;

        if (currentHealth <= 0)
        {
            Die();
            return;
        }

        Feedback.Play(FeedbackEvent.BarricadeHit, transform.position + Vector3.up * 0.5f);

        if (hitFlashRoutine != null) StopCoroutine(hitFlashRoutine);
        hitFlashRoutine = StartCoroutine(HitFlashRoutine());

        if (FeelEnabled)
        {
            TriggerHitPunch();
            TriggerHitChip();
        }

        RecomputeStage();
    }

    /// <summary>
    /// feature/barricade-feel: called by InputManager when a Barricade card is placed on top of an
    /// existing, below-max-HP Barricade on the same StreetSegment, instead of spawning a duplicate.
    /// </summary>
    public void Repair(int amount)
    {
        if (isDead || amount <= 0) return;

        currentHealth = Mathf.Min(maxHealth, currentHealth + amount);

        if (FeelEnabled) TriggerHitPunch(); // reuse the same bump as a "thunk" of repair impact

        RecomputeStage();
    }

    // ── helpers ──────────────────────────────────────────────────

    private void Die()
    {
        if (isDead) return;
        isDead = true;

        Feedback.Play(FeedbackEvent.BarricadeBreak, transform.position + Vector3.up * 0.5f);

        // feature/barricade-feel: a bigger, more readable opening -- stacks an extra dust cloud on
        // top of the Feedback-table debris burst so the breach reads instantly even mid-swarm.
        if (FeelEnabled) VfxManager.Play(VfxId.BarricadeDust, transform.position + Vector3.up * 0.3f, 1.4f);

        if (hitFlashRoutine != null)
        {
            StopCoroutine(hitFlashRoutine);
            hitFlashRoutine = null;
        }

        // Remove obstacle — NavMesh restores, zombies can pass through
        obstacle.enabled = false;
        OnBarricadeChanged?.Invoke(transform.position);

        // Disable collider so zombies don't attack the corpse
        if (cachedCollider != null) cachedCollider.enabled = false;

        StartCoroutine(DeathRoutine());
    }

    // ── feature/barricade-feel ───────────────────────────────────────

    private void RecomputeStage()
    {
        if (!FeelEnabled || indestructible)
        {
            if (currentStage != 1)
            {
                currentStage = 1;
                RefreshStageColor();
                SetCrackCount(0);
            }
            return;
        }

        BarricadeFeelTuning tuning = Tuning;
        float t2 = tuning.stage2Threshold;
        float t3 = tuning.stage3Threshold;
        float frac = maxHealth > 0 ? (float)currentHealth / maxHealth : 1f;

        int newStage = frac <= t3 ? 3 : (frac <= t2 ? 2 : 1);
        if (newStage == currentStage) return;

        bool gettingWorse = newStage > currentStage;
        currentStage = newStage;

        RefreshStageColor();
        SetCrackCount(newStage == 1 ? 0 : (newStage == 2 ? 2 : 4));

        // Only the "getting worse" direction (hit, not repair) gets the crackier break SFX/VFX —
        // repairing back out of a stage is already covered by the Repair caller's own feedback.
        if (gettingWorse)
            Feedback.Play(FeedbackEvent.BarricadeStageBreak, transform.position + Vector3.up * 0.5f);
    }

    private void TriggerHitPunch()
    {
        BarricadeFeelTuning tuning = Tuning;
        if (tuning == null) return;

        if (visualScaleRoutine != null) StopCoroutine(visualScaleRoutine);
        visualScaleRoutine = StartCoroutine(PunchRoutine(tuning.hitPunchScale, tuning.hitPunchDuration));
    }

    private void TriggerHitChip()
    {
        float now = Time.time;
        if (now - lastHitChipTime < 0.1f) return; // throttle under a zombie pile
        lastHitChipTime = now;

        VfxManager.Play(VfxId.BarricadeDust, transform.position + Vector3.up * 0.3f, 0.5f);
    }

    private IEnumerator PlaceRoutine()
    {
        BarricadeFeelTuning tuning = Tuning;
        float dropHeight = tuning.placeDropHeight;
        float duration = Mathf.Max(0.01f, tuning.placeDropDuration);
        float squash = tuning.placeSquash;

        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / duration;
            float clamped = Mathf.Clamp01(t);
            float eased = 1f - Mathf.Pow(1f - clamped, 3f); // ease-out settle

            visualTransform.localPosition = visualBasePosition + new Vector3(0f, Mathf.Lerp(dropHeight, 0f, eased), 0f);

            // Squash on impact, stretch back out as it settles (0 -> 1 -> 0 bump over the fall).
            float bump = Mathf.Sin(clamped * Mathf.PI) * squash;
            ApplyVisualScaleMultiplier(new Vector3(1f + bump * 0.5f, 1f - bump, 1f + bump * 0.5f));

            yield return null;
        }

        visualTransform.localPosition = visualBasePosition;
        ApplyVisualScaleMultiplier(Vector3.one);
        visualScaleRoutine = null;

        Feedback.Play(FeedbackEvent.BarricadePlace, transform.position + Vector3.up * 0.3f);
        if (tuning.placeShake > 0f) CameraShake.Shake(tuning.placeShake, 0.15f);
    }

    private IEnumerator PunchRoutine(float punchScale, float punchDuration)
    {
        float duration = Mathf.Max(0.01f, punchDuration);
        float t = 0f;

        while (t < 1f)
        {
            t += Time.deltaTime / duration;
            float bump = Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI) * punchScale;
            ApplyVisualScaleMultiplier(new Vector3(1f + bump, 1f - bump, 1f + bump));
            yield return null;
        }

        ApplyVisualScaleMultiplier(Vector3.one);
        visualScaleRoutine = null;
    }

    private void ApplyVisualScaleMultiplier(Vector3 multiplier)
    {
        visualTransform.localScale = Vector3.Scale(visualBaseScale, multiplier);
    }

    // Lazily builds up to 4 thin, flat, collider-less boxes on the barricade's top face (counteracts
    // Visual's own non-uniform baked mesh scale so they read as a constant absolute crack size, not
    // stretched by the barricade's authored width/depth). Toggled active/inactive per stage —
    // created once, never destroyed, so repeated stage changes (hit then repair) don't reallocate.
    private void SetCrackCount(int count)
    {
        EnsureCrackQuads();
        for (int i = 0; i < crackQuads.Length; i++)
            if (crackQuads[i] != null) crackQuads[i].SetActive(i < count);
    }

    private void EnsureCrackQuads()
    {
        if (crackQuads[0] != null) return;

        float invX = Mathf.Abs(visualBaseScale.x) > 0.0001f ? 1f / visualBaseScale.x : 1f;
        float invY = Mathf.Abs(visualBaseScale.y) > 0.0001f ? 1f / visualBaseScale.y : 1f;
        float invZ = Mathf.Abs(visualBaseScale.z) > 0.0001f ? 1f / visualBaseScale.z : 1f;

        var crackMaterialBlock = new MaterialPropertyBlock();
        crackMaterialBlock.SetColor("_Color", new Color(0.08f, 0.07f, 0.06f, 0.85f));

        for (int i = 0; i < crackQuads.Length; i++)
        {
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Cube);
            quad.name = "CrackDecal_" + i;
            Collider quadCollider = quad.GetComponent<Collider>();
            if (quadCollider != null) Destroy(quadCollider);

            quad.transform.SetParent(visualTransform, false);

            float xSlot = ((i % 4) - 1.5f) * 0.2f;
            float zJitter = (i % 2 == 0) ? -0.08f : 0.08f;
            quad.transform.localPosition = new Vector3(xSlot, 0.505f, zJitter);
            quad.transform.localRotation = Quaternion.Euler(0f, Random.Range(-20f, 20f), 0f);
            quad.transform.localScale = new Vector3(0.3f * invX, 0.02f * invY, 0.05f * invZ);

            Renderer quadRenderer = quad.GetComponent<Renderer>();
            quadRenderer.sharedMaterial = SharedMaterials.SpritesDefaultMaterial;
            quadRenderer.SetPropertyBlock(crackMaterialBlock);
            quadRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            quad.SetActive(false);
            crackQuads[i] = quad;
        }
    }

    private IEnumerator DeathRoutine()
    {
        RestoreOriginalColors();

        Vector3 startScale = transform.localScale;
        float t = 0f;

        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.01f, deathShrinkDuration);
            float eased = Mathf.SmoothStep(0f, 1f, t);
            transform.localScale = Vector3.Lerp(startScale, Vector3.zero, eased);
            yield return null;
        }

        Destroy(gameObject);
    }

    private IEnumerator HitFlashRoutine()
    {
        SetAllRendererColors(hitFlashColor);
        yield return hitFlashWait;
        RestoreOriginalColors();
    }

    private void SetAllRendererColors(Color color)
    {
        foreach (Renderer r in allRenderers)
        {
            if (r == null || !colorPropertyIds.ContainsKey(r)) continue;
            r.GetPropertyBlock(propertyBlock);
            propertyBlock.SetColor(colorPropertyIds[r], color);
            r.SetPropertyBlock(propertyBlock);
        }
    }

    private void RestoreOriginalColors()
    {
        foreach (Renderer r in allRenderers)
        {
            if (r == null) continue;
            if (!colorPropertyIds.ContainsKey(r) || !originalColors.ContainsKey(r))
            {
                r.SetPropertyBlock(null);
                continue;
            }
            r.GetPropertyBlock(propertyBlock);
            propertyBlock.SetColor(colorPropertyIds[r], originalColors[r]);
            r.SetPropertyBlock(propertyBlock);
        }
    }

    // ── Gizmos ───────────────────────────────────────────────────────────

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.6f, 0.3f, 0f, 0.5f);
        Gizmos.DrawWireCube(transform.position, transform.localScale);
    }
}
