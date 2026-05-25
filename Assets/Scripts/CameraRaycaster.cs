using UnityEngine;

// ЭТОТ СКРИПТ ВЕШАЕТСЯ НА ГЛАВНУЮ КАМЕРУ
public class CameraRaycaster : MonoBehaviour
{
	[SerializeField] private Transform playerTransform; // Сюда в инспекторе перетащи трансформ Игрока (или фокусную точку)
	[SerializeField] private LayerMask buildingLayer;    // Слой, на котором находятся здания (создай слой "Building")

	void Update()
	{
		if (playerTransform == null) return;

		// Считаем направление от камеры к игроку
		Vector3 direction = playerTransform.position - transform.position;
		float distance = direction.magnitude;

		// Пускаем луч. Если он пересекает объект на слое Buildings...
		if (Physics.Raycast(transform.position, direction, out RaycastHit hit, distance, buildingLayer))
		{
			// Ищем у объекта компонент BuildingFader
			BuildingFader fader = hit.collider.GetComponent<BuildingFader>();
			if (fader != null)
			{
				fader.FadeOut(); // Приказываем дому стать прозрачным
			}
		}
	}
}