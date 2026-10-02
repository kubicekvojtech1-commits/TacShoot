using Godot;

public partial class PlayerHud : CanvasLayer
{
	[Export] private ProgressBar _hpBar;
	[Export] private ProgressBar _staminaBar;
	[Export] private HBoxContainer _hotbarBox;
	[Export] private Label _weaponNameLabel;
	[Export] private Label _ammoLabel;
	[Export] private Control _mainOverlay;
	[Export] private Control _tabMenu;

	private ColorRect[] _hotbarSlots = new ColorRect[10];

	public override void _Ready()
	{
		InitializeHotbar();
	}

	private void InitializeHotbar()
	{
		// Dynamically create 10 squares for slots 1-0
		for (int i = 0; i < 10; i++)
		{
			ColorRect slot = new ColorRect
			{
				CustomMinimumSize = new Vector2(40, 40),
				Color = new Color(0.1f, 0.1f, 0.1f, 0.7f) // Dark grey
			};

			Label keyLabel = new Label
			{
				Text = i == 9 ? "0" : (i + 1).ToString(),
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center
			};
			
			// Layout label to fill the square
			keyLabel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
			slot.AddChild(keyLabel);

			_hotbarBox.AddChild(slot);
			_hotbarSlots[i] = slot;
		}
	}

	// Signal Receivers
	public void OnHealthChanged(float current, float max)
	{
		_hpBar.MaxValue = max;
		_hpBar.Value = current;
	}

	public void OnStaminaChanged(float current, float max)
	{
		_staminaBar.MaxValue = max;
		_staminaBar.Value = current;
	}

	public void OnWeaponEquipped(string weaponName)
	{
		_weaponNameLabel.Text = weaponName;
	}

	public void OnAmmoChanged(int current, int reserve)
	{
		_ammoLabel.Text = $"{current} / {reserve}";
	}

	public void OnHotbarSlotChanged(int index)
	{
		// Reset all colors
		foreach (var slot in _hotbarSlots)
		{
			slot.Color = new Color(0.1f, 0.1f, 0.1f, 0.7f);
		}
		
		// Highlight active (Wrap index 9 to key 0)
		if (index >= 0 && index < 10)
		{
			_hotbarSlots[index].Color = new Color(0.8f, 0.8f, 0.8f, 0.9f);
		}
	}

	public void ToggleTabMenu(bool isOpen)
	{
		_tabMenu.Visible = isOpen;
		_mainOverlay.Visible = !isOpen; // Optional: hide HUD while in inventory
	}
}
