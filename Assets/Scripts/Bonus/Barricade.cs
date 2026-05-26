using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using System.Collections.Generic;

// Баррикада — размещается на дороге, блокирует путь через NavMeshObstacle.
// Зомби могут атаковать и разрушить её. Реализует IDamageable, поэтому
// ZombieBoss также может наносить ей урон в режиме ярости.
[RequireComponent(typeof(NavMeshObstacle))]
[RequireComponent(typeof(Collider))]
public class Barricade : MonoBehaviour, IDamageable
{
    public static List<Barricade> AllBarricades = new List<Barricade>();

    [Header("Данные карты")]
    public CardData myCardData;

    [Header("HP (переопределяется из CardData.StatType.Health)")]
    public int maxHealth = 200;

    [Header("Хит-фидбэк (как у зомби)")]
    public Color hitFlashColor = new Color(1f, 0.2f, 0.2f);
    public float hitFlashDuration = 0.1f;
    public float deathShrinkDuration = 0.2f;

    // ── внутреннее состояние ──────────────────────────────────────────────
    private int currentHealth;
    private bool isDead = false;

    private NavMeshObstacle obstacle;
    private Collider cachedCollider;

    // рендеры + PropertyBlock (точно как в Zombie.cs)
    private Renderer[] allRenderers;
    private MaterialPropertyBlock propertyBlock;
    private Dictionary<Renderer, Color> originalColors = new Dictionary<Renderer, Color>();
    private Dictionary<Renderer, int> colorPropertyIds = new Dictionary<Renderer, int>();

    private Coroutine hitFlashRoutine;

    // ── жизненный цикл ───────────────────────────────────────────────────

    private void Awake()
    {
        obstacle = GetComponent<NavMeshObstacle>();
        cachedCollider = GetComponent<Collider>();

        // Вырезаем NavMesh сразу при установке, без задержки
        obstacle.carving = true;
        obstacle.carvingMoveThreshold = 0f;
        obstacle.carvingTimeToStationary = 0f;

        // Собираем рендеры (один раз в Awake, как в Zombie)
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
        // Читаем HP из CardData, если назначена
        if (myCardData != null && PlayerProfile.Instance != null)
        {
            int level = 1;
            var progress = PlayerProfile.Instance.ownedCardsProgress
                .Find(p => p.cardId == myCardData.name);
            if (progress != null) level = progress.currentLevel;

            int hpFromCard = (int)myCardData.GetCalculatedStat(StatType.Health, level);
            if (hpFromCard > 0) maxHealth = hpFromCard;
        }

        currentHealth = maxHealth;
    }

    private void OnEnable()  => AllBarricades.Add(this);
    private void OnDisable() => AllBarricades.Remove(this);

    // ── IDamageable + публичный метод для Zombie ─────────────────────────

    /// <summary>Вызывается из ZombieBoss.BreakBuildingsAround (float-версия интерфейса).</summary>
    public void TakeDamage(float damage) => TakeDamage(Mathf.CeilToInt(damage));

    /// <summary>Вызывается из Zombie.InfectTarget напрямую.</summary>
    public void TakeDamage(int damage)
    {
        if (isDead) return;

        currentHealth -= damage;

        if (currentHealth <= 0)
        {
            Die();
            return;
        }

        if (hitFlashRoutine != null) StopCoroutine(hitFlashRoutine);
        hitFlashRoutine = StartCoroutine(HitFlashRoutine());
    }

    // ── вспомогательные ──────────────────────────────────────────────────

    private void Die()
    {
        if (isDead) return;
        isDead = true;

        if (hitFlashRoutine != null)
        {
            StopCoroutine(hitFlashRoutine);
            hitFlashRoutine = null;
        }

        // Снимаем препятствие — NavMesh восстанавливается, зомби проходят
        obstacle.enabled = false;

        // Выключаем коллайдер, чтобы зомби не атаковали труп
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
