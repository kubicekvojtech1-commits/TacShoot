using Godot;

public partial class TacticalPlayerController : CharacterBody3D
{
	public enum Stance
	{
		Stand,
		Crouch,
		Prone
	}

	[ExportGroup("Node References")]
	[Export] private CollisionShape3D _collisionShape;
	[Export] private Node3D _yawPivot;
	[Export] private Node3D _stancePivot;
	[Export] private Node3D _leanPivot;
	[Export] private Node3D _pitchPivot;
	[Export] private Camera3D _camera;
	[Export] private PlayerInteractor _interactor;

	[ExportGroup("Movement Settings")]
	[Export] private float _walkSpeed = 2.8f;
	[Export] private float _sprintSpeed = 5.5f;
	[Export] private float _crouchSpeed = 1.5f;
	[Export] private float _proneSpeed = 0.8f;
	[Export] private float _acceleration = 10f;
	[Export] private float _friction = 12f;
	[Export] private float _gravity = 9.81f;

	[ExportGroup("Stance Settings")]
	[Export] private float _standHeight = 1.8f;
	[Export] private float _crouchHeight = 1.0f;
	[Export] private float _proneHeight = 0.4f;
	[Export] private float _stanceTransitionSpeed = 8f;

	[ExportGroup("Lean Settings")]
	[Export] private float _leanAngle = 25f; // Degrees
	[Export] private float _leanOffset = 0.35f; // Meters
	[Export] private float _leanSpeed = 10f;

	[ExportGroup("Look Settings")]
	[Export] private float _mouseSensitivity = 0.002f;
	[Export] private float _maxPitch = 85f;
	[Export] private float _minPitch = -85f;

	[ExportGroup("Vitals Settings")]
	[Export] private float _maxHealth = 100f;
	[Export] private float _maxStamina = 100f;
	[Export] private float _staminaDrainRate = 15f; // Per second sprinting
	[Export] private float _staminaRegenRate = 10f; // Per second resting

	// UI Signals
	[Signal] public delegate void HealthChangedEventHandler(float current, float max);
	[Signal] public delegate void StaminaChangedEventHandler(float current, float max);
	[Signal] public delegate void MenuToggledEventHandler(bool isOpen);

	// Core State
	private Stance _currentStance = Stance.Stand;
	private CapsuleShape3D _capsule;
	private Vector2 _mouseInput;
	private bool _isSprinting;
	public bool IsMenuOpen { get; private set; }
	
	// Vitals State
	private float _currentHealth;
	private float _currentStamina;
	
	// Target Values for Interpolation
	private float _targetHeight;
	private float _targetLeanRotation;
	private float _targetLeanOffset;

	public override void _Ready()
	{
		Input.MouseMode = Input.MouseModeEnum.Captured;
		
		// Ensure we are working with a unique resource so modifying height doesn't affect other entities
		_capsule = _collisionShape.Shape as CapsuleShape3D;
		if (_capsule != null)
		{
			_capsule = (CapsuleShape3D)_capsule.Duplicate();
			_collisionShape.Shape = _capsule;
		}

		_targetHeight = _standHeight;
		
		// Initialize vitals
		_currentHealth = _maxHealth;
		_currentStamina = _maxStamina;

		// Assign player RID to Meta for weapons/raycasts to ignore
		SetMeta("PlayerRID", GetRid());

		// Emit initial values next frame to ensure HUD is fully instantiated and ready to receive
		CallDeferred(MethodName.EmitInitialVitals);
	}

	private void EmitInitialVitals()
	{
		EmitSignal(SignalName.HealthChanged, _currentHealth, _maxHealth);
		EmitSignal(SignalName.StaminaChanged, _currentStamina, _maxStamina);
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		// Handle TAB Menu
		if (@event.IsActionPressed("ui_focus_next")) // Usually mapped to TAB
		{
			IsMenuOpen = !IsMenuOpen;
			Input.MouseMode = IsMenuOpen ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;
			EmitSignal(SignalName.MenuToggled, IsMenuOpen);
			return;
		}
		
		// Handle Item Dropping
		if (@event.IsActionPressed("drop") && !IsMenuOpen)
		{
			var inventory = GetNodeOrNull<PlayerInventory>("PlayerInventory");
			if (inventory != null)
			{
				// Calculate drop position half a meter in front of the camera
				Vector3 dropPos = _camera.GlobalPosition - _camera.GlobalTransform.Basis.Z * 0.5f;
				Vector3 throwDir = -_camera.GlobalTransform.Basis.Z;
				
				inventory.DropEquippedItem(dropPos, throwDir);
			}
			return;
		}
		
		if (@event.IsActionPressed("interact") && !IsMenuOpen)
		{
			// Pass 'this' (the root player node) so items can find the PlayerInventory child
			_interactor?.TryInteract(this);
			return;
		}

		if (IsMenuOpen) return; // Block further input if menu is open

		// Capture Mouse Look
		if (@event is InputEventMouseMotion mouseMotion && Input.MouseMode == Input.MouseModeEnum.Captured)
		{
			// Change this from '=' to '+=' to accumulate all movement between frames
			_mouseInput += mouseMotion.Relative;
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		float fDelta = (float)delta;

		if (!IsMenuOpen)
		{
			HandleCameraLook(fDelta);
			HandleStance(fDelta);
			HandleLeaning(fDelta);
			HandleMovement(fDelta);
		}
		else
		{
			// Decelerate smoothly if the player opens the menu while moving
			Vector3 velocity = Velocity;
			velocity.X = Mathf.MoveToward(velocity.X, 0, _friction * fDelta);
			velocity.Z = Mathf.MoveToward(velocity.Z, 0, _friction * fDelta);
			Velocity = velocity;
			MoveAndSlide();
		}
		
		ProcessVitals(fDelta);
	}

	private void HandleCameraLook(float delta)
	{
		if (_mouseInput == Vector2.Zero) return;

		// Yaw (Left/Right)
		_yawPivot.RotateY(-_mouseInput.X * _mouseSensitivity);

		// Pitch (Up/Down)
		_pitchPivot.RotateX(-_mouseInput.Y * _mouseSensitivity);
		
		// Clamp Pitch
		Vector3 pitchRot = _pitchPivot.Rotation;
		pitchRot.X = Mathf.Clamp(pitchRot.X, Mathf.DegToRad(_minPitch), Mathf.DegToRad(_maxPitch));
		_pitchPivot.Rotation = pitchRot;

		_mouseInput = Vector2.Zero; // Reset after processing
	}

	private void HandleStance(float delta)
	{
		if (Input.IsActionJustPressed("prone"))
		{
			_currentStance = _currentStance == Stance.Prone ? Stance.Stand : Stance.Prone;
		}
		else if (Input.IsActionJustPressed("crouch"))
		{
			_currentStance = _currentStance == Stance.Crouch ? Stance.Stand : Stance.Crouch;
		}

		switch (_currentStance)
		{
			case Stance.Stand: _targetHeight = _standHeight; break;
			case Stance.Crouch: _targetHeight = _crouchHeight; break;
			case Stance.Prone: _targetHeight = _proneHeight; break;
		}

		// Interpolate Capsule Height
		_capsule.Height = Mathf.MoveToward(_capsule.Height, _targetHeight, _stanceTransitionSpeed * delta);
		
		// Keep feet on the ground by offsetting position
		_collisionShape.Position = new Vector3(0, _capsule.Height / 2f, 0);

		// Interpolate Camera Height
		Vector3 stancePos = _stancePivot.Position;
		stancePos.Y = Mathf.Lerp(stancePos.Y, _capsule.Height * 0.9f, _stanceTransitionSpeed * delta);
		_stancePivot.Position = stancePos;
	}

	private void HandleLeaning(float delta)
	{
		if (_currentStance == Stance.Prone)
		{
			_targetLeanRotation = 0f;
			_targetLeanOffset = 0f;
		}
		else
		{
			float leanInput = Input.GetAxis("lean_left", "lean_right");
			_targetLeanRotation = leanInput * Mathf.DegToRad(-_leanAngle);
			_targetLeanOffset = leanInput * _leanOffset;
		}

		// Interpolate Lean Rotation
		Vector3 leanRot = _leanPivot.Rotation;
		leanRot.Z = Mathf.Lerp(leanRot.Z, _targetLeanRotation, _leanSpeed * delta);
		_leanPivot.Rotation = leanRot;

		// Interpolate Lean Position
		Vector3 leanPos = _leanPivot.Position;
		leanPos.X = Mathf.Lerp(leanPos.X, _targetLeanOffset, _leanSpeed * delta);
		_leanPivot.Position = leanPos;
	}

	private void HandleMovement(float delta)
	{
		Vector3 velocity = Velocity;

		// Gravity
		if (!IsOnFloor())
		{
			velocity.Y -= _gravity * delta;
		}
		else if (Input.IsActionJustPressed("jump") && _currentStance == Stance.Stand && _currentStamina > 10f)
		{
			velocity.Y = 4.5f; 
			_currentStamina -= 10f; // Jumping costs stamina
			EmitSignal(SignalName.StaminaChanged, _currentStamina, _maxStamina);
		}

		// Determine target speed based on stance, stamina, and sprint state
		_isSprinting = Input.IsActionPressed("sprint") && 
					   _currentStance == Stance.Stand && 
					   !Input.IsActionPressed("move_backward") && 
					   _currentStamina > 0;

		float targetSpeed = _walkSpeed;
		
		if (_currentStance == Stance.Crouch) targetSpeed = _crouchSpeed;
		else if (_currentStance == Stance.Prone) targetSpeed = _proneSpeed;
		else if (_isSprinting) targetSpeed = _sprintSpeed;

		// Get Input Direction relative to where the player is looking
		Vector2 inputDir = Input.GetVector("move_left", "move_right", "move_forward", "move_backward");
		Vector3 direction = (_yawPivot.Transform.Basis * new Vector3(inputDir.X, 0, inputDir.Y)).Normalized();

		// Apply Acceleration and Friction
		if (direction != Vector3.Zero)
		{
			velocity.X = Mathf.MoveToward(velocity.X, direction.X * targetSpeed, _acceleration * delta);
			velocity.Z = Mathf.MoveToward(velocity.Z, direction.Z * targetSpeed, _acceleration * delta);
		}
		else
		{
			velocity.X = Mathf.MoveToward(velocity.X, 0, _friction * delta);
			velocity.Z = Mathf.MoveToward(velocity.Z, 0, _friction * delta);
		}

		Velocity = velocity;
		MoveAndSlide();
	}

	private void ProcessVitals(float delta)
	{
		bool staminaChanged = false;

		// Drain stamina if moving and sprinting
		if (_isSprinting && Velocity.LengthSquared() > 0.1f)
		{
			_currentStamina = Mathf.Max(0, _currentStamina - (_staminaDrainRate * delta));
			staminaChanged = true;
		}
		// Regen stamina if not sprinting
		else if (_currentStamina < _maxStamina)
		{
			_currentStamina = Mathf.Min(_maxStamina, _currentStamina + (_staminaRegenRate * delta));
			staminaChanged = true;
		}

		if (staminaChanged)
		{
			EmitSignal(SignalName.StaminaChanged, _currentStamina, _maxStamina);
		}
	}
}
