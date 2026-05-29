using UnityEngine;

[CreateAssetMenu(fileName = "ChaosSettings", menuName = "Game/Chaos Settings")]
public class ChaosSettings : ScriptableObject
{
	[Header("General")]
	public bool chaosEnabled = true;

	[Header("Zombies")]
	[Range(0f, 1f)]
	public float zombieLoseTargetChance = 0.03f;

	[Tooltip("How often a zombie checks whether to lose its target (in seconds)")]
	public float zombieLoseTargetCheckInterval = 0.75f;

	[Tooltip("How long a zombie wanders aimlessly after losing its target")]
	public float zombieLoseTargetDuration = 0.6f;

	[Header("Humans")]
	[Range(0f, 1f)]
	public float humanPanicChance = 0.05f;

	[Tooltip("How often a human checks whether to panic (in seconds)")]
	public float humanPanicCheckInterval = 1.0f;

	[Tooltip("How long a human runs in a random direction during panic")]
	public float humanPanicDuration = 0.4f;

	[Tooltip("Distance a human moves during a panic sprint")]
	public float humanPanicMoveDistance = 3.0f;

	[Tooltip("Zombie must be within this radius to trigger human panic")]
	public float humanPanicTriggerRadius = 4.5f;
}