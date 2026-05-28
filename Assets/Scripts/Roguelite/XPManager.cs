using UnityEngine;

public class XPManager : MonoBehaviour
{
    public static XPManager Instance { get; private set; }

    [Header("XP за события")]
    public int xpPerZombieKill      = 5;
    public int xpPerBossKill        = 50;
    public int xpPerCivilian        = 20;
    public int xpPerScientist       = 40;
    public int xpBonusFullTransport = 25;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public void OnZombieKilled(bool isBoss = false)
    {
        int raw = isBoss ? xpPerBossKill : xpPerZombieKill;
        Grant(raw);
    }

    public void OnCivilianEvacuated(int humanCount, int scientistCount, bool wasFullTransport = false)
    {
        int raw = humanCount * xpPerCivilian + scientistCount * xpPerScientist;
        if (wasFullTransport) raw += xpBonusFullTransport;
        Grant(raw);
    }

    private void Grant(int rawAmount)
    {
        if (RunSessionData.Instance == null) return;

        float mult = 1f + RunSessionData.Instance.GetModifier("general_xp_mult");
        int final = Mathf.RoundToInt(rawAmount * mult);
        RunSessionData.Instance.AddXP(final);
    }
}
