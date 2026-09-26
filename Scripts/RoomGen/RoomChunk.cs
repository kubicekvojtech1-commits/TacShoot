using Godot;
using System.Collections.Generic;

public partial class RoomChunk : Node3D
{
	[ExportCategory("Generation Rules")]
	// How much of the facility budget this room consumes.
	[Export] public int GenerationCost { get; private set; } = 1;
	
	// Higher numbers mean it is more likely to be picked from the pool.
	[Export] public float SelectionWeight { get; private set; } = 10f;
	[ExportCategory("Spatial Data")]
	[Export] public Marker3D RoomCenter { get; private set; }

	// NEW: Define the exact floorplan of your room (Top-Down X/Z perspective).
	// You map the corners of the room in order (clockwise or counter-clockwise).
	[Export] public Vector2[] FloorplanPolygon { get; private set; } 
		= new Vector2[] { new Vector2(-3, -3), new Vector2(3, -3), new Vector2(3, 3), new Vector2(-3, 3) };

	public List<Marker3D> GetExitSockets()
	{
		List<Marker3D> sockets = new List<Marker3D>();
		Node3D exitsContainer = GetNodeOrNull<Node3D>("Exits");

		if (exitsContainer != null)
		{
			foreach (Node child in exitsContainer.GetChildren())
			{
				if (child is Marker3D marker) sockets.Add(marker);
			}
		}
		else
		{
			foreach (Node child in GetChildren())
			{
				if (child is Marker3D marker && marker != RoomCenter) sockets.Add(marker);
			}
		}
		return sockets;
	}

	// Calculates the precise 2D footprint of the room in global space, accounting for rotation
	public Vector2[] GetGlobalPolygon(Transform3D baseTransform)
	{
		if (FloorplanPolygon == null || FloorplanPolygon.Length < 3)
		{
			GD.PushWarning($"RoomChunk '{Name}' has an invalid FloorplanPolygon. Needs at least 3 points.");
			return new Vector2[0];
		}

		Vector2[] globalPoly = new Vector2[FloorplanPolygon.Length];
		
		// Find the local center to apply a microscopic shrink 
		// (prevents mathematically flush walls from falsely triggering an overlap)
		Vector2 localCenter = Vector2.Zero;
		foreach (Vector2 p in FloorplanPolygon) localCenter += p;
		localCenter /= FloorplanPolygon.Length;

		for (int i = 0; i < FloorplanPolygon.Length; i++)
		{
			// Shrink point 0.05m toward the center
			Vector2 dir = (localCenter - FloorplanPolygon[i]).Normalized();
			Vector2 safePoint = FloorplanPolygon[i] + (dir * 0.05f);

			// Convert local 2D (X, Y) footprint to local 3D (X, 0, Z)
			Vector3 localPos3D = new Vector3(safePoint.X, 0, safePoint.Y);
			
			// Multiply by transform to get exact global position and rotation
			Vector3 globalPos3D = baseTransform * localPos3D;
			
			// Flatten back to 2D for the intersection math
			globalPoly[i] = new Vector2(globalPos3D.X, globalPos3D.Z);
		}

		return globalPoly;
	}
}
