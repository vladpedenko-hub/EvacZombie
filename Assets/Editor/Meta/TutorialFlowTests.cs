using NUnit.Framework;
using UnityEngine;

// Guards the original tutorial behaviour: every new option defaults to off, and progress keys are unchanged.
public class TutorialFlowTests
{
	private const string Id = "unit_test_tutorial";

	[TearDown]
	public void TearDown() => TutorialProgress.Reset(Id);

	[Test]
	public void NewSequence_DefaultsReproduceOriginalBehaviour()
	{
		var sequence = ScriptableObject.CreateInstance<TutorialSequence>();

		Assert.IsFalse(sequence.allowSkip, "No Skip button unless the sequence opts in");
		Assert.IsFalse(sequence.resumable, "No pause/scene-change release unless the sequence opts in");
	}

	[Test]
	public void NewStep_DefaultTimeoutIsWaitForever()
	{
		var step = new TutorialStep();

		Assert.AreEqual(0f, step.missingTargetTimeout, "0 keeps the original wait-forever behaviour");
	}

	[Test]
	public void Done_RoundTrips_AndReset_Clears()
	{
		Assert.IsFalse(TutorialProgress.IsDone(Id));

		TutorialProgress.MarkDone(Id);
		Assert.IsTrue(TutorialProgress.IsDone(Id));

		TutorialProgress.Reset(Id);
		Assert.IsFalse(TutorialProgress.IsDone(Id));
	}

	[Test]
	public void Step_RoundTrips_ClearAndFallback()
	{
		Assert.IsFalse(TutorialProgress.HasStep(Id));
		Assert.AreEqual(0, TutorialProgress.GetStep(Id), "No saved step starts at the beginning");

		TutorialProgress.SetStep(Id, 3);
		Assert.IsTrue(TutorialProgress.HasStep(Id));
		Assert.AreEqual(3, TutorialProgress.GetStep(Id));

		TutorialProgress.ClearStep(Id);
		Assert.IsFalse(TutorialProgress.HasStep(Id));
	}

	[Test]
	public void ProgressKeys_MatchOriginalFormat()
	{
		// Existing battle tutorials store completion under this exact key. Changing it would replay them.
		TutorialProgress.MarkDone(Id);
		Assert.AreEqual(1, PlayerPrefs.GetInt("TUTORIAL_DONE_" + Id, 0));
	}
}
