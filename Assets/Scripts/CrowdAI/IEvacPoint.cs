using UnityEngine;

// Implemented by CarController/HelicopterController so CrowdFieldManager can seed F_evac from
// whichever evac vehicles are currently on the map and actually accepting passengers (not while
// still driving in from off-screen or already leaving).
public interface IEvacPoint
{
	Vector3 EvacPosition { get; }
	bool IsAcceptingPassengers { get; }
}
