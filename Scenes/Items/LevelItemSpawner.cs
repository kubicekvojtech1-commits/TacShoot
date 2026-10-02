using Godot;

[GlobalClass]
public partial class LevelItemSpawner : Node3D
{
	[Export] private ItemData _itemToSpawn;

	public override void _Ready()
	{
		if (_itemToSpawn != null && _itemToSpawn.WorldModelPrefab != null)
		{
			var itemNode = _itemToSpawn.WorldModelPrefab.Instantiate();
			if (itemNode is WorldItem worldItem)
			{
				worldItem.Initialize(_itemToSpawn);
				
				// Add the actual item to the scene
				GetParent().CallDeferred(Node.MethodName.AddChild, worldItem);
				
				// Match the spawner's position/rotation
				worldItem.GlobalTransform = GlobalTransform;
			}
		}
		
		// Delete this spawner node so only the physical gun remains
		QueueFree(); 
	}
}
