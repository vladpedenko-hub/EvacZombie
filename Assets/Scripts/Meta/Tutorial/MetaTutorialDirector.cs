using System.Collections;
using UnityEngine;

// Runs the Laboratory tutorial chain in MainMenu:
//   1. meta_lab_freed: the Mad Scientist's dialogue (started by the Laboratory's TriggerDialogueReward)
//   2. meta_research_tour: highlights the side Research icon, the first node, and the Buy button
// Start, resume and skip are decided from the save, not from a fixed script. Both sequences are skippable and
// resumable, and the screen is released on every exit path (see TutorialManager).
public class MetaTutorialDirector : MonoBehaviour
{
	public const string LabFreedId = "meta_lab_freed";
	public const string ResearchTourId = "meta_research_tour";

	// Target ids used by the research tour steps. Must match the TutorialSequence assets.
	public const string FirstNodeTargetId = "research_node_1";
	public const string BuyTargetId = "research_buy";
	public const string DockTargetId = SideDock.TutorialTargetId;

	private MetaService service;
	private BaseView view;
	private SideDock dock;
	private bool subscribed;

	public void Build(MetaService metaService, BaseView baseView, SideDock sideDock)
	{
		service = metaService;
		view = baseView;
		dock = sideDock;
		Subscribe();
		StartCoroutine(StartOnEntry());
	}

	private void OnEnable()
	{
		if (service != null) Subscribe();
	}

	private void OnDisable() => Unsubscribe();

	private void Subscribe()
	{
		if (subscribed || service == null) return;
		service.OnDialogueRequested += OnDialogueRequested;
		TutorialManager.OnTutorialFinished += OnTutorialFinished;
		TutorialManager.OnStepChanged += OnStepChanged;
		if (view != null) view.ResearchStateChanged += OnResearchStateChanged;
		subscribed = true;
	}

	private void Unsubscribe()
	{
		if (!subscribed) return;
		service.OnDialogueRequested -= OnDialogueRequested;
		TutorialManager.OnTutorialFinished -= OnTutorialFinished;
		TutorialManager.OnStepChanged -= OnStepChanged;
		if (view != null) view.ResearchStateChanged -= OnResearchStateChanged;
		subscribed = false;
	}

	// One frame later: the tab controller and BaseView have finished their Start, so their rects exist.
	private IEnumerator StartOnEntry()
	{
		yield return null;
		RegisterStaticTargets();
		TryStartNext();
	}

	private void RegisterStaticTargets()
	{
		TutorialManager tutorial = TutorialManager.Instance;
		if (tutorial == null) return;

		if (dock != null)
		{
			RectTransform icon = dock.IconRect("research");
			if (icon != null) tutorial.RegisterTarget(DockTargetId, icon);
		}

		RectTransform buy = view != null ? view.ResearchBuyRect : null;
		if (buy != null) tutorial.RegisterTarget(BuyTargetId, buy);
	}

	// Starts the next unfinished step of the chain, if the Laboratory is restored and no tutorial is running.
	private void TryStartNext()
	{
		TutorialManager tutorial = TutorialManager.Instance;
		if (tutorial == null || tutorial.IsActive || service == null) return;
		if (!service.State.restoredBuildings.Contains("lab")) return;

		// A player who already bought a node knows how research works. The tour is skipped for them.
		if (service.State.purchasedNodes.Count > 0 && !TutorialProgress.IsDone(ResearchTourId))
			TutorialProgress.MarkDone(ResearchTourId);

		if (!TutorialProgress.IsDone(LabFreedId))
		{
			StartSequence(LabFreedId);
			return;
		}

		if (!TutorialProgress.IsDone(ResearchTourId))
		{
			// A saved step that points into research needs the overlay. If it is closed, resume from the dock step.
			// Steps that do not need research (the closing line) resume where they were.
			TutorialSequence tour = service.FindSequence(ResearchTourId);
			int saved = TutorialProgress.GetStep(ResearchTourId);
			bool needsResearch = tour != null && saved < tour.steps.Count && IsResearchStep(tour.steps[saved].targetId);
			if (needsResearch && !(view != null && view.IsResearchOpen)) TutorialProgress.SetStep(ResearchTourId, 0);
			StartSequence(ResearchTourId);
		}
	}

	private void StartSequence(string id)
	{
		TutorialSequence sequence = service.FindSequence(id);
		if (sequence == null)
		{
			Debug.LogWarning($"[MetaTutorial] Sequence '{id}' is not in MetaContentDatabase.");
			return;
		}
		TutorialManager.Instance.StartTutorial(sequence);
	}

	// TriggerDialogueReward (Laboratory restore) sends the dialogue id here.
	private void OnDialogueRequested(string dialogueId)
	{
		if (dialogueId == LabFreedId) TryStartNext();
	}

	private void OnTutorialFinished()
	{
		// Finishing the dialogue hands over to the research tour.
		TryStartNext();
	}

	// Research opened: the first node and the Buy button become targets. Research closed during a research step:
	// release the tour (its progress is saved, so it resumes at the dock step next time).
	private void OnResearchStateChanged()
	{
		TutorialManager tutorial = TutorialManager.Instance;
		if (tutorial == null) return;

		if (view != null && view.IsResearchOpen)
		{
			RectTransform node = view.ResearchNodeRect(0);
			if (node != null) tutorial.RegisterTarget(FirstNodeTargetId, node);
			RegisterStaticTargets();
			return;
		}

		if (tutorial.IsActive && IsResearchStep(tutorial.CurrentTargetId))
			tutorial.AbandonTutorial();
	}

	// The Buy button is disabled when the first node is not affordable, so its tap would never advance.
	// In that case the Buy step is skipped one frame later and the closing line still makes sense.
	private void OnStepChanged()
	{
		TutorialManager tutorial = TutorialManager.Instance;
		if (tutorial == null || !tutorial.IsActive || tutorial.CurrentTargetId != BuyTargetId) return;
		if (service.GetNodeState(FirstNodeId) == NodeState.Available) return;

		StartCoroutine(AdvancePastBuyStep());
	}

	private IEnumerator AdvancePastBuyStep()
	{
		yield return null;
		TutorialManager tutorial = TutorialManager.Instance;
		if (tutorial != null && tutorial.IsActive && tutorial.CurrentTargetId == BuyTargetId) tutorial.NextStep();
	}

	// The first node of the Laboratory tree, read from data (the tour always points at it).
	private string FirstNodeId
	{
		get
		{
			SkillTreeDefinition tree = service.FindTreeForBuilding("lab");
			return tree != null && tree.nodes.Count > 0 && tree.nodes[0] != null ? tree.nodes[0].id : "";
		}
	}

	private static bool IsResearchStep(string targetId) =>
		targetId == FirstNodeTargetId || targetId == BuyTargetId;
}
