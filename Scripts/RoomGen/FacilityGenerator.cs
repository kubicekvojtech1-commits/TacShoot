using Godot;
using System.Collections.Generic;

public partial class FacilityGenerator : Node3D
{
	[ExportCategory("Prefabs")]
	[Export] private PackedScene[] _roomPrefabs; 
	[Export(PropertyHint.ResourceType, "PackedScene")] private PackedScene _doorBlockerPrefab; 
	[Export(PropertyHint.ResourceType, "PackedScene")] private PackedScene _deadEndPrefab; 

	[ExportCategory("Generation Settings")]
	[Export] private int _maxRooms = 20;
	[Export] private bool _capOpeningsOnFinish = true;

	[ExportCategory("Debug")]
	[Export] private bool _stepByStepMode = false;
	[Export] private string _stepInputAction = "ui_accept";

	private List<RoomChunk> _spawnedRooms = new List<RoomChunk>();
	private Queue<PendingSocket> _pendingSockets = new Queue<PendingSocket>();
	private RandomNumberGenerator _rng = new RandomNumberGenerator();

	private struct PendingSocket
	{
		public Transform3D Transform;
		public RoomChunk ParentRoom;
	}

	private enum GenState { NotStarted, SpawningRooms, CappingSockets, Finished }
	private GenState _currentState = GenState.NotStarted;

	public override void _Ready()
	{
		_rng.Randomize();
		StartGeneration();

		if (!_stepByStepMode)
		{
			while (_currentState != GenState.Finished)
			{
				StepGeneration();
			}
		}
		else
		{
			GD.Print($"[Debug Gen] Step-by-Step mode ON. Press '{_stepInputAction}' to advance.");
		}
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (_stepByStepMode && _currentState != GenState.Finished)
		{
			if (@event.IsActionPressed(_stepInputAction))
			{
				StepGeneration();
			}
		}
	}

	private void StartGeneration()
	{
		if (_roomPrefabs == null || _roomPrefabs.Length == 0) return;
		
		PackedScene startPrefab = GetRandomPrefab();
		RoomChunk startRoom = startPrefab.Instantiate<RoomChunk>();
		AddChild(startRoom);
		startRoom.GlobalTransform = GlobalTransform;
		
		_spawnedRooms.Add(startRoom);
		
		foreach (Marker3D exit in startRoom.GetExitSockets())
		{
			_pendingSockets.Enqueue(new PendingSocket 
			{ 
				Transform = exit.GlobalTransform, 
				ParentRoom = startRoom 
			});
		}
		
		_currentState = GenState.SpawningRooms;
	}

	private void StepGeneration()
	{
		switch (_currentState)
		{
			case GenState.SpawningRooms:
				if (_pendingSockets.Count > 0 && _spawnedRooms.Count < _maxRooms)
				{
					PendingSocket target = _pendingSockets.Dequeue();
					
					PackedScene randomPrefab = GetRandomPrefab();
					RoomChunk candidateRoom = randomPrefab.Instantiate<RoomChunk>();
					candidateRoom.GlobalTransform = target.Transform;
					
					if (!IsSpaceOccupied(candidateRoom, target.Transform, target.ParentRoom, out RoomChunk hitRoom))
					{
						// Add to tree first, then re-apply transform so children update correctly
						AddChild(candidateRoom);
						candidateRoom.GlobalTransform = target.Transform; 
						_spawnedRooms.Add(candidateRoom);
						
						foreach (Marker3D exit in candidateRoom.GetExitSockets())
						{
							_pendingSockets.Enqueue(new PendingSocket 
							{ 
								Transform = exit.GlobalTransform, 
								ParentRoom = candidateRoom 
							});
						}
						GD.Print($"[Step] Spawned Room {_spawnedRooms.Count}/{_maxRooms}");
					}
					else
					{
						candidateRoom.QueueFree(); 
						SpawnCap(target.Transform, _doorBlockerPrefab);
						GD.Print($"[Step] Blocked door! Candidate overlapped with {(hitRoom != null ? hitRoom.Name : "Unknown")}");
					}
				}
				
				if (_pendingSockets.Count == 0 || _spawnedRooms.Count >= _maxRooms)
				{
					GD.Print("[Step] Room limit reached or out of sockets. Moving to Capping phase.");
					_currentState = GenState.CappingSockets;
				}
				break;

			case GenState.CappingSockets:
				if (_capOpeningsOnFinish && _pendingSockets.Count > 0)
				{
					PendingSocket target = _pendingSockets.Dequeue();
					
					RoomChunk candidateDeadEnd = _deadEndPrefab.Instantiate<RoomChunk>();
					candidateDeadEnd.GlobalTransform = target.Transform;

					if (!IsSpaceOccupied(candidateDeadEnd, target.Transform, target.ParentRoom, out RoomChunk hitRoom))
					{
						AddChild(candidateDeadEnd);
						candidateDeadEnd.GlobalTransform = target.Transform;
						GD.Print("[Step] Spawned Dead-End Room.");
					}
					else
					{
						candidateDeadEnd.QueueFree();
						SpawnCap(target.Transform, _doorBlockerPrefab);
						GD.Print($"[Step] Blocked dead-end! Space was occupied by {(hitRoom != null ? hitRoom.Name : "Unknown")}");
					}
				}
				else
				{
					_pendingSockets.Clear();
					_currentState = GenState.Finished;
					GD.Print("[Step] Generation Complete!");
				}
				break;
		}
	}

	private void SpawnCap(Transform3D spawnTransform, PackedScene capPrefab)
	{
		if (capPrefab == null) return;
		Node3D capInstance = capPrefab.Instantiate<Node3D>();
		AddChild(capInstance);
		capInstance.GlobalTransform = spawnTransform;
	}

	private PackedScene GetRandomPrefab()
	{
		return _roomPrefabs[_rng.RandiRange(0, _roomPrefabs.Length - 1)];
	}

	// UPDATED: Now takes the target Transform3D so we can inject it into the orphan candidate
	private bool IsSpaceOccupied(RoomChunk candidateRoom, Transform3D targetTransform, RoomChunk parentToIgnore, out RoomChunk hitRoom)
	{
		hitRoom = null;
		if (_spawnedRooms.Count == 0) return false;

		// Get the mathematically precise footprint of the candidate room
		Vector2[] candidatePoly = candidateRoom.GetGlobalPolygon(targetTransform);
		if (candidatePoly.Length < 3) return false;

		foreach (RoomChunk existingRoom in _spawnedRooms)
		{
			if (existingRoom == parentToIgnore) continue;

			Vector2[] existingPoly = existingRoom.GetGlobalPolygon(existingRoom.GlobalTransform);
			if (existingPoly.Length < 3) continue;

			// Godot's built-in Clipper library natively calculates polygon intersections
			// If the array count is greater than 0, the shapes are overlapping.
			var intersections = Geometry2D.IntersectPolygons(candidatePoly, existingPoly);
			
			if (intersections.Count > 0)
			{
				hitRoom = existingRoom; 
				return true;
			}
		}
		return false;
	}
}
