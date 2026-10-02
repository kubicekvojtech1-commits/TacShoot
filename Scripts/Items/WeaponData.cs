using Godot;

[GlobalClass]
public partial class WeaponData : ItemData
{
	[ExportGroup("Ballistics")]
	[Export] public float BaseDamage { get; private set; } = 25f;
	[Export] public float MuzzleVelocity { get; private set; } = 800f; // m/s
	[Export] public float ArmorPenetration { get; private set; } = 0.5f;
	
	[ExportGroup("Handling")]
	[Export] public float RoundsPerMinute { get; private set; } = 600f;
	[Export] public int MagazineSize { get; private set; } = 30;
	[Export] public bool IsAutomatic { get; private set; } = true;
	
	public float TimeBetweenShots => 60f / RoundsPerMinute;
}		
