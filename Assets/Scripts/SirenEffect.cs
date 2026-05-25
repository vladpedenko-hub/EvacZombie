using UnityEngine;

public class SirenEffect : MonoBehaviour
{
	private SpriteRenderer sr;
	private float targetRadius;
	private float duration;
	private float timer = 0;

	public void Setup(float radius, float time)
	{
		sr = GetComponent<SpriteRenderer>();
		targetRadius = radius;
		duration = time;
		transform.localScale = Vector3.zero; // Начинаем с нуля
	}

	private void Update()
	{
		timer += Time.deltaTime;
		float progress = timer / duration;

		// Расширяем круг ровно до того радиуса, который в машине
		transform.localScale = new Vector3(targetRadius * 2, targetRadius * 2, 1);

		// Плавное исчезновение
		if (sr != null)
		{
			Color c = sr.color;
			c.a = Mathf.Lerp(0.6f, 0, progress);
			sr.color = c;
		}

		if (progress >= 1) Destroy(gameObject);
	}
}