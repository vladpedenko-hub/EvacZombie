using UnityEngine;

// fix/civilian-flee (step 1): sandbox-only death/infection counters by civilian state at the
// moment of death, read by SandboxDevPanel's counters text. Also logs a one-line warning per
// civilian whose death looks like "ran straight into a zombie" -- within 1m of the zombie and
// moving toward it (dot of velocity with the direction to the zombie > 0.5) -- so a tuning pass
// can grep the Console for exactly how often that still happens.
public static class CivilianDeathTelemetry
{
	public enum DeathState { Calm, PanicFlee, PanicEvac, Rescue, Boarding }

	public static int DeathsCalm { get; private set; }
	public static int DeathsPanicFlee { get; private set; }
	public static int DeathsPanicEvac { get; private set; }
	public static int DeathsRescue { get; private set; }
	public static int DeathsBoarding { get; private set; }

	// Support "Enter Play Mode without domain reload" -- same reasoning as every other static
	// cache in this project (CardScale.cs, etc).
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
	private static void ResetStatics()
	{
		DeathsCalm = 0;
		DeathsPanicFlee = 0;
		DeathsPanicEvac = 0;
		DeathsRescue = 0;
		DeathsBoarding = 0;
	}

	private static bool SandboxActive =>
		LevelManager.Instance != null && LevelManager.Instance.currentData != null && LevelManager.Instance.currentData.isSandboxLevel;

	// civilianVelocity/nearestZombiePos are straight-line world data sampled by the caller at the
	// moment of death -- nearestZombiePos is null if no zombie exists at all (can't happen in
	// practice, since a zombie had to be the one biting, but kept defensive).
	public static void RecordDeath(DeathState state, string civilianName, Vector3 civilianPos, Vector3 civilianVelocity, Vector3? nearestZombiePos)
	{
		if (!SandboxActive) return;

		switch (state)
		{
			case DeathState.Calm: DeathsCalm++; break;
			case DeathState.PanicFlee: DeathsPanicFlee++; break;
			case DeathState.PanicEvac: DeathsPanicEvac++; break;
			case DeathState.Rescue: DeathsRescue++; break;
			case DeathState.Boarding: DeathsBoarding++; break;
		}

		if (nearestZombiePos == null || civilianVelocity.sqrMagnitude < 0.0001f) return;

		Vector3 toZombie = nearestZombiePos.Value - civilianPos;
		toZombie.y = 0f;
		float distance = toZombie.magnitude;
		if (distance > 1f) return;

		float dot = Vector3.Dot(civilianVelocity.normalized, toZombie.normalized);
		if (dot > 0.5f)
			Debug.LogWarning($"[CivilianDeathTelemetry] {civilianName} died {distance:F2}m from a zombie while moving toward it (dot={dot:F2}), state={state}.");
	}
}
