using UnityEngine;

[CreateAssetMenu(fileName = "ChaosSettings", menuName = "Game/Chaos Settings")]
public class ChaosSettings : ScriptableObject
{
	[Header("Глобально")]
	public bool chaosEnabled = true;

	[Header("Зомби")]
	[Range(0f, 1f)]
	public float zombieLoseTargetChance = 0.03f;

	[Tooltip("Как часто зомби может проверять, не затупить ли (в секундах)")]
	public float zombieLoseTargetCheckInterval = 0.75f;

	[Tooltip("На сколько секунд зомби теряет цель")]
	public float zombieLoseTargetDuration = 0.6f;

	[Header("Люди")]
	[Range(0f, 1f)]
	public float humanPanicChance = 0.05f;

	[Tooltip("Как часто человек может проверять, не запаниковать ли")]
	public float humanPanicCheckInterval = 1.0f;

	[Tooltip("На сколько секунд человек делает странный рывок")]
	public float humanPanicDuration = 0.4f;

	[Tooltip("Насколько сильно человек отклоняется в панике")]
	public float humanPanicMoveDistance = 3.0f;

	[Tooltip("Паника срабатывает, только если зомби ближе этого радиуса")]
	public float humanPanicTriggerRadius = 4.5f;
}