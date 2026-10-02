using Godot;
using System.Collections.Generic;

public partial class FacilityGenerator : Node3D
{
	[ExportCategory("Prefabs")]
	[Export] private PackedScene[] _roomPrefabs; 
	[Export(PropertyHint.ResourceType, "PackedScene")] private PackedScene _doorBlockerPrefab; 
	[Export(PropertyHint.ResourceType, "PackedScene")] private PackedScene _deadEndPrefab; 

	[ExportCategory("Generation Settings")]
	[Export] private int _maxBudget = 20;
	[Export] private bool _capOpeningsOnFinish = true;

	[ExportCategory("Debug")]
	[Export] private bool _stepByStepMode = false;
	[Export] private string _stepInputAction = "ui_accept";

	// NEW: Caches prefab data so we don't have to instantiate them to read their weights
	private struct RoomTemplate
	{
		public PackedScene Prefab;
		public int Cost;
		public float Weight;
	}
	private List<RoomTemplate> _templatePool = new List<RoomTemplate>();
	private float _totalPoolWeight = 0f;
	private int _currentFacilityCost = 0;

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
		InitializeTemplatePool();
		StartGeneration();

		if (!_stepByStepMode)
		{
			while (_currentState != GenState.Finished) StepGeneration();
		}
		else
		{
			GD.Print($"[Debug Gen] Step-by-Step mode ON. Press '{_stepInputAction}' to advance.");
		}
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (_stepByStepMode && _currentState != GenState.Finished && @event.IsActionPressed(_stepInputAction))
		{
			StepGeneration();
		}
	}

	private void InitializeTemplatePool()
	{
		if (_roomPrefabs == null || _roomPrefabs.Length == 0) return;

		foreach (PackedScene prefab in _roomPrefabs)
		{
			if (prefab == null) continue;

			// Instantiate once as an orphan to read the data
			RoomChunk tempInstance = prefab.Instantiate<RoomChunk>();
			
			// Apply fallbacks just in case editor values are set to 0 or negative
			int safeCost = tempInstance.GenerationCost > 0 ? tempInstance.GenerationCost : 1;
			float safeWeight = tempInstance.SelectionWeight > 0 ? tempInstance.SelectionWeight : 1f;

			_templatePool.Add(new RoomTemplate 
			{ 
				Prefab = prefab, 
				Cost = safeCost, 
				Weight = safeWeight 
			});

			_totalPoolWeight += safeWeight;
			
			// Clean up immediately to prevent memory leaks
			tempInstance.QueueFree();
		}
	}

	private void StartGeneration()
	{
		if (_templatePool.Count == 0) return;
		
		RoomTemplate startTemplate = GetRandomTemplate();
		RoomChunk startRoom = startTemplate.Prefab.Instantiate<RoomChunk>();
		AddChild(startRoom);
		startRoom.GlobalTransform = GlobalTransform;
		
		_spawnedRooms.Add(startRoom);
		_currentFacilityCost += startTemplate.Cost;
		
		foreach (Marker3D exit in startRoom.GetExitSockets())
		{
			_pendingSockets.Enqueue(new PendingSocket { Transform = exit.GlobalTransform, ParentRoom = startRoom });
		}
		
		_currentState = GenState.SpawningRooms;
	}

	private void StepGeneration()
	{
		switch (_currentState)
		{
			case GenState.SpawningRooms:
				if (_pendingSockets.Count > 0 && _currentFacilityCost < _maxBudget)
				{
					PendingSocket target = _pendingSockets.Dequeue();
					
					RoomTemplate template = GetRandomTemplate();
					
					// If this specific room pushes us over budget, we skip it and spawn a blocker.
					// This creates organic dead ends as the budget drains.
					if (_currentFacilityCost + template.Cost > _maxBudget)
					{
						SpawnCap(target.Transform, _doorBlockerPrefab);
						GD.Print($"[Step] Blocked door! Candidate cost ({template.Cost}) exceeded remaining budget.");
						break;
					}

					RoomChunk candidateRoom = template.Prefab.Instantiate<RoomChunk>();
					candidateRoom.GlobalTransform = target.Transform;
					
					if (!IsSpaceOccupied(candidateRoom, target.Transform, target.ParentRoom, out RoomChunk hitRoom))
					{
						AddChild(candidateRoom);
						candidateRoom.GlobalTransform = target.Transform; 
						_spawnedRooms.Add(candidateRoom);
						_currentFacilityCost += template.Cost;
						
						foreach (Marker3D exit in candidateRoom.GetExitSockets())
						{
							_pendingSockets.Enqueue(new PendingSocket { Transform = exit.GlobalTransform, ParentRoom = candidateRoom });
						}
						GD.Print($"[Step] Spawned Room. Cost: {_currentFacilityCost}/{_maxBudget}");
					}
					else
					{
						candidateRoom.QueueFree(); 
						SpawnCap(target.Transform, _doorBlockerPrefab);
						GD.Print($"[Step] Blocked door! Candidate overlapped with {(hitRoom != null ? hitRoom.Name : "Unknown")}");
					}
				}
				
				if (_pendingSockets.Count == 0 || _currentFacilityCost >= _maxBudget)
				{
					GD.Print("[Step] Budget reached or out of sockets. Moving to Capping phase.");
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

	// NEW: Weighted random selection algorithm
	private RoomTemplate GetRandomTemplate()
	{
		float randomRoll = _rng.RandfRange(0, _totalPoolWeight);
		float currentSum = 0;

		foreach (RoomTemplate template in _templatePool)
		{
			currentSum += template.Weight;
			if (randomRoll <= currentSum)
			{
				return template;
			}
		}
		
		// Failsafe return
		return _templatePool[_templatePool.Count - 1];
	}

	private void SpawnCap(Transform3D spawnTransform, PackedScene capPrefab)
	{
		if (capPrefab == null) return;
		Node3D capInstance = capPrefab.Instantiate<Node3D>();
		AddChild(capInstance);
		capInstance.GlobalTransform = spawnTransform;
	}

	private bool IsSpaceOccupied(RoomChunk candidateRoom, Transform3D targetTransform, RoomChunk parentToIgnore, out RoomChunk hitRoom)
	{
		hitRoom = null;
		if (_spawnedRooms.Count == 0) return false;

		Vector2[] candidatePoly = candidateRoom.GetGlobalPolygon(targetTransform);
		if (candidatePoly.Length < 3) return false;

		foreach (RoomChunk existingRoom in _spawnedRooms)
		{
			if (existingRoom == parentToIgnore) continue;

			Vector2[] existingPoly = existingRoom.GetGlobalPolygon(existingRoom.GlobalTransform);
			if (existingPoly.Length < 3) continue;

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
