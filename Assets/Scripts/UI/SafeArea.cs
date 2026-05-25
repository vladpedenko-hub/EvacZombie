using UnityEngine;

// [ГДЕ ВИСИТ]: Создай пустой RectTransform внутри Canvas (назови SafeAreaContainer). 
// Растяни его на весь экран (Stretch/Stretch, по нулям). 
// Закинь ВЕСЬ свой UI (Карту, Меню, Попапы) ВНУТРЬ этого SafeAreaContainer. 
// Повесь этот скрипт на SafeAreaContainer.
[RequireComponent(typeof(RectTransform))]
public class SafeArea : MonoBehaviour
{
	private RectTransform rectTransform;
	private Rect lastSafeArea = new Rect(0, 0, 0, 0);

	private void Awake()
	{
		rectTransform = GetComponent<RectTransform>();
		ApplySafeArea();
	}

	private void Update()
	{
		// Проверяем, не перевернул ли игрок экран или не изменилась ли челка (бывает на Android)
		if (Screen.safeArea != lastSafeArea)
		{
			ApplySafeArea();
		}
	}

	private void ApplySafeArea()
	{
		lastSafeArea = Screen.safeArea;

		// Преобразуем пиксели экрана в нормализованные координаты для Anchor
		Vector2 anchorMin = lastSafeArea.position;
		Vector2 anchorMax = lastSafeArea.position + lastSafeArea.size;

		anchorMin.x /= Screen.width;
		anchorMin.y /= Screen.height;
		anchorMax.x /= Screen.width;
		anchorMax.y /= Screen.height;

		rectTransform.anchorMin = anchorMin;
		rectTransform.anchorMax = anchorMax;
	}
}