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

    private NavMeshObstacle obstacle;
    private Collider cachedCollider;

    // renderers + PropertyBlock (same pattern as in Zombie.cs)
    private Renderer[] allRenderers;
    private MaterialPropertyBlock propertyBlock;
    private Dictionary<Renderer, Color> originalColors = new Dictionary<Renderer, Color>();
    private Dictionary<Renderer, int> colorPropertyIds = new Dictionary<Renderer, int>();

    private Coroutine hitFlashRoutine;

    // ── lifecycle ───────────────────────────────────────────────────

    private void Awake()
    {
        obstacle = GetComponent<NavMeshObstacle>();
        cachedCollider = GetComponent<Collider>();

        // Carve NavMesh immediately on placement, without delay
        obstacle.carving = true;
        obstacle.carvingMoveThreshold = 0f;
        obstacle.carvingTimeToStationary = 0f;

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
            }
            else if (mat.HasProperty("_Color"))
            {
                colorPropertyIds[r] = Shader.PropertyToID("_Color");
                originalColors[r] = mat.GetColor("_Color");
            }
        }
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
                maxHealth = 999999;

            float widthMult = run.GetModifier("barricade_width_mult");
            if (widthMult > 0f)
                transform.localScale *= (1f + widthMult);
        }

        currentHealth = maxHealth;
    }

    private void OnEnable()  => AllBarricades.Add(this);
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
                Collider[] nearby = Physics.OverlapSphere(transform.position, 3f);
                foreach (var c in nearby)
                {
                    Zombie z = c?.GetComponent<Zombie>();
                    if (z != null)
                    {
                        z.TakeDamage(Mathf.RoundToInt(damage * reflectPct));
                        break;
                    }
                }
            }

            if (run.HasFlag("barricade_death_zone"))
            {
                Collider[] deathZone = Physics.OverlapSphere(transform.position, 1.5f);
                foreach (var c in deathZone)
                    c?.GetComponent<Zombie>()?.TakeDamage(99999);
            }
        }

        currentHealth -= damage;

        if (currentHealth <= 0)
        {
            Die();
            return;
        }

        if (hitFlashRoutine != null) StopCoroutine(hitFlashRoutine);
        hitFlashRoutine = StartCoroutine(HitFlashRoutine());
    }

    // ── helpers ──────────────────────────────────────────────────

    private void Die()
    {
        if (isDead) return;
        isDead = true;

        if (hitFlashRoutine != null)
        {
            StopCoroutine(hitFlashRoutine);
            hitFlashRoutine = null;
        }

        // Remove obstacle — NavMesh restores, zombies can pass through
        obstacle.enabled = false;

        // Disable collider so zombies don't attack the corpse
        if (cachedCollider != null) cachedCollider.enabled = false;

        StartCoroutine(DeathRoutine());
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
        yield return new WaitForSeconds(hitFlashDuration);
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
