using UnityEngine;

// Fit-to-width math for CameraController (per-level camera) and CameraSwitcher (debug 2D/3D
// toggle).
//
// This does NOT re-derive "the ideal camera" from level geometry. An earlier version of this
// file measured world bounds from Building-tagged colliders (plus spawn points), but on
// Level_1 that produced wildly wrong results: a single building ("building-f") sits ~40 degrees
// off-axis from the authored camera, which isn't something the original cameraPosition/FOV ever
// framed in the first place -- level content isn't reliable ground truth for "what the camera
// should show," and re-deriving it risked moving every level's camera far from its authored,
// already-art-directed framing (a "no gameplay/visual changes" violation, not a fix).
//
// Instead this trusts LevelData's authored orthographicSize/cameraPosition/cameraFieldOfView as
// correct for a 1080x1920 (the project's CanvasScaler reference resolution -- a reasonable stand-
// in for whatever aspect these were tuned against in the Editor Game view), and only grows the
// framing -- narrower aspect, or more HUD/card-hand reservation, needs *more* visible area, never
// less -- so it can only reveal more of the already-authored shot, never re-frame it.
public static class CameraFraming
{
	private const float ReferenceAspect = 1080f / 1920f;

	// Orthographic size (half-height, world units) that keeps at least as much width/height
	// visible as the authored size did at the reference aspect, after reserving
	// topReservedScreenFraction / bottomReservedScreenFraction of the screen for the HUD / card
	// hand. Never returns less than authoredSize (that stays the fallback/min).
	public static float ComputeOrthographicSize(float authoredSize, float currentAspect,
		float topReservedScreenFraction, float bottomReservedScreenFraction, float marginPercent)
	{
		float sizeForWidth = authoredSize * ReferenceAspect / Mathf.Max(0.01f, currentAspect);
		float sizeForTop = authoredSize / Mathf.Max(0.1f, 1f - 2f * topReservedScreenFraction);
		float sizeForBottom = authoredSize / Mathf.Max(0.1f, 1f - 2f * bottomReservedScreenFraction);

		float required = Mathf.Max(sizeForWidth, Mathf.Max(sizeForTop, sizeForBottom)) * (1f + marginPercent);
		return Mathf.Max(required, authoredSize);
	}

	// Distance from camPos to the y=0 ground plane along 'forward', used as the perspective
	// camera's reference depth (its orthographic-size equivalent). Null if the camera doesn't
	// look downward (shouldn't happen for this project's cameras, all pitched at a city below).
	public static float? GetGroundDistance(Vector3 camPos, Vector3 forward)
	{
		if (Mathf.Abs(forward.y) < 0.0001f) return null;
		float t = -camPos.y / forward.y;
		return t > 0f ? (float?)t : null;
	}

	// How much farther back (along the camera's forward axis) a perspective camera must move
	// from its authored position to keep at least as much width/height in frame as it did at the
	// reference aspect, after the same top/bottom reservations. Returns 0 if the authored
	// position already covers it (never moves the camera closer than authored).
	public static float ComputePerspectivePullback(float referenceGroundDistance, float currentAspect,
		float topReservedScreenFraction, float bottomReservedScreenFraction, float marginPercent)
	{
		float depthForWidth = referenceGroundDistance * ReferenceAspect / Mathf.Max(0.01f, currentAspect);
		float depthForTop = referenceGroundDistance / Mathf.Max(0.1f, 1f - 2f * topReservedScreenFraction);
		float depthForBottom = referenceGroundDistance / Mathf.Max(0.1f, 1f - 2f * bottomReservedScreenFraction);

		float requiredDepth = Mathf.Max(depthForWidth, Mathf.Max(depthForTop, depthForBottom)) * (1f + marginPercent);
		return Mathf.Max(0f, requiredDepth - referenceGroundDistance);
	}
}
