using UnityEngine;

// Persisted tutorial state in PlayerPrefs. Keys are unchanged from the original TutorialManager, so
// existing tutorials keep their completion flags.
//   TUTORIAL_DONE_<id>  = 1 once the sequence is finished or skipped (also the "dialogue seen" flag)
//   TUTORIAL_STEP_<id>  = index of the step to resume at (resumable sequences only)
public static class TutorialProgress
{
	private const string DonePrefix = "TUTORIAL_DONE_";
	private const string StepPrefix = "TUTORIAL_STEP_";

	public static bool IsDone(string id) => PlayerPrefs.GetInt(DonePrefix + id, 0) == 1;

	public static void MarkDone(string id)
	{
		PlayerPrefs.SetInt(DonePrefix + id, 1);
		PlayerPrefs.Save();
	}

	public static int GetStep(string id, int fallback = 0) => PlayerPrefs.GetInt(StepPrefix + id, fallback);

	public static bool HasStep(string id) => PlayerPrefs.HasKey(StepPrefix + id);

	public static void SetStep(string id, int stepIndex)
	{
		PlayerPrefs.SetInt(StepPrefix + id, stepIndex);
		PlayerPrefs.Save();
	}

	public static void ClearStep(string id)
	{
		PlayerPrefs.DeleteKey(StepPrefix + id);
		PlayerPrefs.Save();
	}

	// Debug panel: forget a sequence entirely so it can run again from the start.
	public static void Reset(string id)
	{
		PlayerPrefs.DeleteKey(DonePrefix + id);
		PlayerPrefs.DeleteKey(StepPrefix + id);
		PlayerPrefs.Save();
	}
}
