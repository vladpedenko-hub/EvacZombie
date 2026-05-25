using UnityEngine;

// Этот атрибут заставляет скрипт выполняться даже когда игра не запущена
[ExecuteInEditMode]
public class EditorSpawnGizmo : MonoBehaviour
{
	[Header("Настройки редактора")]
	// Имя иконки. Важно: иконка должна лежать в папке Assets/Gizmos
	public string gizmoIconName = "SkullIcon.png";
	public Color gizmoTintColor = new Color(1f, 0f, 0f, 0.5f); // Красный полупрозрачный
	public float gizmoSize = 1f;

	// Этот метод вызывается ТОЛЬКО окном Scene в редакторе
	private void OnDrawGizmos()
	{
		// Мы рисуем простую сферу, чтобы обозначить точку
		Gizmos.color = gizmoTintColor;
		Gizmos.DrawSphere(transform.position, gizmoSize * 0.5f);

		// --- ДОПОЛНИТЕЛЬНО: Можно рисовать иконку ---
		// 1. Создайте папку Assets/Gizmos (имя должно быть точным)
		// 2. Положите туда иконку черепа (например, "SkullIcon.png")
		// 3. Раскомментируйте строку ниже:
		// Gizmos.DrawIcon(transform.position + Vector3.up * gizmoSize, gizmoIconName, true);
	}
}