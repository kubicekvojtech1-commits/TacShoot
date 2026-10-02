using Godot;

[GlobalClass]
public partial class ItemData : Resource
{
	[Export] public string ItemId { get; private set; } = "item_base";
	[Export] public string DisplayName { get; private set; } = "Base Item";
	[Export] public float WeightKg { get; private set; } = 1.0f;
	[Export] public Texture2D Icon { get; private set; }
	
	[Export] public PackedScene ViewModelPrefab { get; private set; }
	[Export] public PackedScene WorldModelPrefab { get; private set; }
}
