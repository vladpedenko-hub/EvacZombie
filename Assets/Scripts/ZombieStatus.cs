// feature/bomb-combo (step 2): small, null-safe static helper so other cards can read a zombie's
// status (gathered at a Bait / held at a Barricade) without each needing its own null-check. Pure
// read-only wrapper around Zombie's own public properties -- no behavior change.
public static class ZombieStatus
{
	public static bool IsGathered(Zombie z) => z != null && z.IsGathered;
	public static bool IsHeld(Zombie z) => z != null && z.IsHeld;
}
