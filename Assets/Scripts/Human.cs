using System.Collections.Generic;

// [WHERE IT LIVES]: On the Human prefab (Human).
// AI is shared with Scientist — see EvacueeBase.cs. The only thing specific to Human is
// which static roster it registers itself in.
public class Human : EvacueeBase
{
	public static List<Human> AllHumans = new List<Human>();

	private void OnEnable() => AllHumans.Add(this);
	private void OnDisable() => AllHumans.Remove(this);
}
