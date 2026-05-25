using UnityEngine;
using UnityEngine.EventSystems;
using System.Collections.Generic;

// Повесь этот скрипт на любой объект на сцене (например, на Manager)
public class UIDetector : MonoBehaviour
{
	void Update()
	{
		// Если нажали левую кнопку мыши или коснулись экрана
		if (Input.GetMouseButtonDown(0))
		{
			PointerEventData eventData = new PointerEventData(EventSystem.current);
			eventData.position = Input.mousePosition;

			List<RaycastResult> results = new List<RaycastResult>();
			EventSystem.current.RaycastAll(eventData, results);

			// Пишем в консоль, кто перехватил клик
			if (results.Count > 0)
			{
				Debug.Log("Клик поймал объект: " + results[0].gameObject.name, results[0].gameObject);
			}
			else
			{
				Debug.Log("Клик улетел в пустоту (EventSystem не видит UI)");
			}
		}
	}
}