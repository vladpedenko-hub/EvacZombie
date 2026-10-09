using System.Collections.Generic;

// [WHERE IT LIVES]: On the Scientist prefab (Scientist).
// AI is shared with Human — see EvacueeBase.cs. The only thing specific to Scientist is
// which static roster it registers itself in.
public class Scientist : EvacueeBase
{
	public static List<Scientist> AllScientists = new List<Scientist>();

	private void OnEnable() => AllScientists.Add(this);
	private void OnDisable() => AllScientists.Remove(this);
}
