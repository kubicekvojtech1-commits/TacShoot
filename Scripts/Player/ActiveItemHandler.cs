using Godot;

public partial class ActiveItemHandler : Node3D
{
	public bool HasEquippedItem => _currentData != null;
	[Export] private Camera3D _playerCamera;
	
	[ExportGroup("Sway & Inertia Settings")]
	[Export] private float _swayAmount = 0.02f;
	[Export] private float _maxSway = 0.1f;
	[Export] private float _swaySmoothness = 12f;
	[Export] private float _breathingAmplitude = 0.005f;
	[Export] private float _breathingFrequency = 1.2f;

	// UI Signals
	[Signal] public delegate void WeaponEquippedEventHandler(string name);
	[Signal] public delegate void AmmoChangedEventHandler(int current, int reserve);
	[Signal] public delegate void HotbarSlotSelectedEventHandler(int slotIndex);
	
	// Core State
	private ItemData _currentData;
	private Node3D _currentViewModel;
	private PhysicsDirectSpaceState3D _spaceState;
	private TacticalPlayerController _player;
	
	// Weapon State
	private int _currentAmmo;
	private int _reserveAmmo = 120; // Temporary mock for reserve ammo
	private double _nextFireTime;
	
	// Sway State
	private Vector2 _mouseInput;
	private Vector3 _basePosition;
	private Vector3 _baseRotation;
	
	// Expose the current data so the inventory knows what to drop
	public ItemData CurrentData => _currentData;

	public void Unequip()
	{
		if (_currentViewModel != null)
		{
			_currentViewModel.QueueFree();
			_currentViewModel = null;
		}

		_currentData = null;
		_currentAmmo = 0;

		// Reset UI
		EmitSignal(SignalName.WeaponEquipped, "Unarmed");
		EmitSignal(SignalName.AmmoChanged, 0, 0);
	}

		public override void _Ready()
	{
		_spaceState = GetWorld3D().DirectSpaceState;
		
		// Robustly find the player controller by walking up the scene tree
		Node currentNode = GetParent();
		while (currentNode != null && currentNode is not TacticalPlayerController)
		{
			currentNode = currentNode.GetParent();
		}
		
		_player = currentNode as TacticalPlayerController;
	
		if (_player == null)
		{
			GD.PrintErr("ActiveItemHandler: Could not find TacticalPlayerController in parent hierarchy!");
		}
		
		_basePosition = Position;
		_baseRotation = Rotation;
	}
	
	public void EquipItem(ItemData itemData, int slotIndex)
	{
		if (_currentViewModel != null)
		{
			_currentViewModel.QueueFree();
			_currentViewModel = null;
		}

		_currentData = itemData;

		if (_currentData != null && _currentData.ViewModelPrefab != null)
		{
			_currentViewModel = _currentData.ViewModelPrefab.Instantiate<Node3D>();
			AddChild(_currentViewModel);
			
			if (_currentData is WeaponData weaponData)
			{
				_currentAmmo = weaponData.MagazineSize;
				EmitSignal(SignalName.AmmoChanged, _currentAmmo, _reserveAmmo);
			}
		}
		
		EmitSignal(SignalName.WeaponEquipped, _currentData != null ? _currentData.DisplayName : "Unarmed");
		EmitSignal(SignalName.HotbarSlotSelected, slotIndex);
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (_player != null && _player.IsMenuOpen) return; // Block input while in menu

		// Capture raw mouse input for sway calculation
		if (@event is InputEventMouseMotion mouseMotion && Input.MouseMode == Input.MouseModeEnum.Captured)
		{
			// Change from '=' to '+='
			_mouseInput += mouseMotion.Relative; 
		}

		// Handle Hotkeys 1-0 (Slots 0-9)
		for (int i = 0; i < 10; i++)
		{
			Key keyToCheck = (i == 9) ? Key.Key0 : (Key.Key1 + i);
			
			if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo && keyEvent.Keycode == keyToCheck)
			{
				// In the future, this will equip the actual item from the inventory list
				EmitSignal(SignalName.HotbarSlotSelected, i);
				return; 
			}
		}

		// Handle Firing
		if (_currentData is WeaponData weaponData)
		{
			bool fireInput = weaponData.IsAutomatic 
				? Input.IsActionPressed("fire") 
				: Input.IsActionJustPressed("fire");

			if (fireInput)
			{
				TryFire(weaponData);
			}
		}
	}

	public override void _Process(double delta)
	{
		if (_currentViewModel == null) return;
		
		ApplyProceduralSway((float)delta);
	}

	private void ApplyProceduralSway(float delta)
	{
		// 1. Look Inertia
		float targetSwayX = Mathf.Clamp(-_mouseInput.Y * _swayAmount, -_maxSway, _maxSway);
		float targetSwayY = Mathf.Clamp(-_mouseInput.X * _swayAmount, -_maxSway, _maxSway);

		// 2. Breathing
		float time = (float)(Time.GetTicksMsec() / 1000.0);
		float breathX = Mathf.Cos(time * _breathingFrequency) * _breathingAmplitude;
		float breathY = Mathf.Sin(time * _breathingFrequency * 2f) * (_breathingAmplitude / 2f);

		// Calculate Targets
		Vector3 targetPosition = _basePosition;
		targetPosition.X += targetSwayY + breathX;
		targetPosition.Y += targetSwayX + breathY;

		Vector3 targetRotation = _baseRotation;
		targetRotation.Z += targetSwayY * 2f; 
		targetRotation.X += targetSwayX;      

		// Interpolate
		Position = Position.Lerp(targetPosition, _swaySmoothness * delta);
		
		Vector3 currentRot = Rotation;
		currentRot.X = Mathf.LerpAngle(currentRot.X, targetRotation.X, _swaySmoothness * delta);
		currentRot.Z = Mathf.LerpAngle(currentRot.Z, targetRotation.Z, _swaySmoothness * delta);
		Rotation = currentRot;

		// Decay mouse input
		_mouseInput = _mouseInput.Lerp(Vector2.Zero, _swaySmoothness * delta);
	}

	private void TryFire(WeaponData weaponData)
	{
		double currentTime = Time.GetTicksMsec() / 1000.0;
		
		if (currentTime >= _nextFireTime && _currentAmmo > 0)
		{
			_nextFireTime = currentTime + weaponData.TimeBetweenShots;
			_currentAmmo--;
			
			EmitSignal(SignalName.AmmoChanged, _currentAmmo, _reserveAmmo);
			ExecuteHitscanFire(weaponData);
			ApplyRecoilAndEffects();
		}
	}

	private void ExecuteHitscanFire(WeaponData weapon)
	{
		Vector3 origin = _playerCamera.GlobalPosition;
		Vector3 direction = -_playerCamera.GlobalTransform.Basis.Z;
		Vector3 end = origin + (direction * 1000f);

		var query = PhysicsRayQueryParameters3D.Create(origin, end);
		
		if (Owner is CollisionObject3D ownerBody)
		{
			query.Exclude = new Godot.Collections.Array<Rid> { ownerBody.GetRid() };
		}

		var result = _spaceState.IntersectRay(query);

		if (result.Count > 0)
		{
			Vector3 hitPoint = result["position"].AsVector3();
			Node3D hitCollider = result["collider"].As<Node3D>();
			GD.Print($"Hit {hitCollider.Name} at {hitPoint} for {weapon.BaseDamage} damage.");
		}
	}

	private void ApplyRecoilAndEffects()
	{
		if (_currentViewModel != null)
		{
			_currentViewModel.Position = new Vector3(0, 0.02f, 0.15f);
			
			var tween = CreateTween();
			tween.TweenProperty(_currentViewModel, "position", Vector3.Zero, 0.15f)
				 .SetEase(Tween.EaseType.Out)
				 .SetTrans(Tween.TransitionType.Back);
		}
	}
}
