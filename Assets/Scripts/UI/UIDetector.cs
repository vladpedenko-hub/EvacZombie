using UnityEngine;
using UnityEngine.EventSystems;
using System.Collections.Generic;

// Attach this script to any scene object (e.g. a Manager) for UI click debugging
public class UIDetector : MonoBehaviour
{
	void Update()
	{
		// Detect left mouse button click or screen touch
		if (Input.GetMouseButtonDown(0))
		{
			PointerEventData eventData = new PointerEventData(EventSystem.current);
			eventData.position = Input.mousePosition;

			List<RaycastResult> results = new List<RaycastResult>();
			EventSystem.current.RaycastAll(eventData, results);

			// Log which UI element intercepted the click
			if (results.Count > 0)
			{
				Debug.Log("Click hit UI: " + results[0].gameObject.name, results[0].gameObject);
			}
			else
			{
				Debug.Log("Click missed UI (EventSystem found no UI elements)");
			}
		}
	}
}