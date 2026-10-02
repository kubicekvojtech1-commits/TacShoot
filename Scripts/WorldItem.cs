using Godot;
using TacShoot.Core;

public partial class WorldItem : RigidBody3D, IInteractable
{
	// Note: [Export] has been removed. 
	public ItemData ItemData { get; private set; }

	public void Initialize(ItemData data)
	{
		ItemData = data;
	}

	public override void _Ready()
	{
		CollisionLayer = 4; 
		CollisionMask = 1;  
	}

	public string GetInteractText()
	{
		return ItemData != null ? $"Pick up {ItemData.DisplayName}" : "Pick up item";
	}

	public void Interact(Node3D interactor)
	{
		var inventory = interactor.GetNodeOrNull<PlayerInventory>("PlayerInventory");
		if (inventory != null && ItemData != null)
		{
			if (inventory.AddItem(ItemData))
			{
				QueueFree();
			}
		}
	}
}
