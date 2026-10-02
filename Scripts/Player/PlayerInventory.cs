using Godot;
using System.Collections.Generic;

[GlobalClass]
public partial class PlayerInventory : Node
{
	private readonly List<ItemData> _items = new();
	
	[Export] private ActiveItemHandler _itemHandler;

	[Signal] public delegate void ItemAddedEventHandler(ItemData item);

	public bool AddItem(ItemData itemData)
	{
		// Future: Check weight limits or slot availability here
		_items.Add(itemData);
		EmitSignal(SignalName.ItemAdded, itemData);

		// Quality of Life: Auto-equip if our hands are empty and we pick up a weapon
		// Note: You will need to add `public bool HasEquippedItem => _currentData != null;` to your ActiveItemHandler
		if (_itemHandler != null && !_itemHandler.HasEquippedItem && itemData is WeaponData)
		{
			_itemHandler.EquipItem(itemData, 0); 
		}

		return true; 
	}
	
	public void DropEquippedItem(Vector3 dropPosition, Vector3 throwDirection)
	{
		if (_itemHandler == null || !_itemHandler.HasEquippedItem) return;

		ItemData itemToDrop = _itemHandler.CurrentData;

		// 1. Remove from inventory data
		_items.Remove(itemToDrop);
		
		// 2. Clear hands
		_itemHandler.Unequip();

		// 3. Spawn physical item in the world
		if (itemToDrop.WorldModelPrefab != null)
		{
			Node3D spawnedNode = itemToDrop.WorldModelPrefab.Instantiate<Node3D>();
			
			// Cast strictly to WorldItem instead of RigidBody3D
			if (spawnedNode is WorldItem dropItem)
			{
				// Inject the data so we can pick it back up!
				dropItem.Initialize(itemToDrop);
				
				GetTree().CurrentScene.AddChild(dropItem);
				
				dropItem.GlobalPosition = dropPosition;
				
				dropItem.Rotation = new Vector3(
					(float)GD.RandRange(0, Mathf.Tau), 
					(float)GD.RandRange(0, Mathf.Tau), 
					(float)GD.RandRange(0, Mathf.Tau)
				);
				
				dropItem.ApplyCentralImpulse(throwDirection * 3.5f);
			}
			else
			{
				GD.PrintErr("Drop failed: WorldModelPrefab is not a WorldItem!");
				spawnedNode.QueueFree();
			}
		}
	}

	public IReadOnlyList<ItemData> GetItems() => _items.AsReadOnly();
}
