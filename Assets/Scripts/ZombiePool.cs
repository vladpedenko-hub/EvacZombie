using System.Collections.Generic;
using UnityEngine;

public class ZombiePool : MonoBehaviour
{
	public static ZombiePool Instance;

	[Header("Pool Settings")]
	[SerializeField] private GameObject zombiePrefab;
	[SerializeField] private int prewarmCount = 40;
	[SerializeField] private Transform poolParent;

	private readonly Queue<Zombie> available = new Queue<Zombie>();

	private void Awake()
	{
		if (Instance != null && Instance != this)
		{
			Destroy(gameObject);
			return;
		}

		Instance = this;

		if (poolParent == null)
		{
			GameObject holder = new GameObject("ZombiePool_Container");
			poolParent = holder.transform;
		}

		Prewarm();
	}

	private void Prewarm()
	{
		if (zombiePrefab == null) return;

		for (int i = 0; i < prewarmCount; i++)
		{
			CreateAndStore();
		}
	}

	private Zombie CreateAndStore()
	{
		GameObject go = Instantiate(zombiePrefab, poolParent);
		go.SetActive(false);

		Zombie zombie = go.GetComponent<Zombie>();
		if (zombie == null)
		{
			Debug.LogError("[ZombiePool] Prefab does not contain Zombie component.");
			return null;
		}

		available.Enqueue(zombie);
		return zombie;
	}

	public Zombie Get(Vector3 position, Quaternion rotation)
	{
		if (zombiePrefab == null)
		{
			Debug.LogError("[ZombiePool] zombiePrefab is not assigned.");
			return null;
		}

		Zombie zombie = available.Count > 0 ? available.Dequeue() : CreateAndStore();
		if (zombie == null) return null;

		Transform t = zombie.transform;
		t.SetParent(null);
		t.position = position;
		t.rotation = rotation;
		t.localScale = Vector3.one;

		GameObject go = zombie.gameObject;
		go.SetActive(true);

		zombie.OnTakenFromPool(position, rotation);
		return zombie;
	}

	public void Release(Zombie zombie)
	{
		if (zombie == null) return;

		zombie.OnReturnedToPool();

		Transform t = zombie.transform;
		t.SetParent(poolParent);
		zombie.gameObject.SetActive(false);

		available.Enqueue(zombie);
	}
}