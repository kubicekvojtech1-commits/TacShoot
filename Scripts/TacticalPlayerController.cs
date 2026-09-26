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

	// State
	private Stance _currentStance = Stance.Stand;
	private CapsuleShape3D _capsule;
	private Vector2 _mouseInput;
	private bool _isSprinting;
	
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
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventMouseMotion mouseMotion && Input.MouseMode == Input.MouseModeEnum.Captured)
		{
			_mouseInput = mouseMotion.Relative;
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		float fDelta = (float)delta;

		HandleCameraLook(fDelta);
		HandleStance(fDelta);
		HandleLeaning(fDelta);
		HandleMovement(fDelta);
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
		// Stance Input handling
		if (Input.IsActionJustPressed("prone"))
		{
			_currentStance = _currentStance == Stance.Prone ? Stance.Stand : Stance.Prone;
		}
		else if (Input.IsActionJustPressed("crouch"))
		{
			_currentStance = _currentStance == Stance.Crouch ? Stance.Stand : Stance.Crouch;
		}

		// Determine target height
		switch (_currentStance)
		{
			case Stance.Stand: _targetHeight = _standHeight; break;
			case Stance.Crouch: _targetHeight = _crouchHeight; break;
			case Stance.Prone: _targetHeight = _proneHeight; break;
		}

		// Interpolate Capsule Height
		_capsule.Height = Mathf.MoveToward(_capsule.Height, _targetHeight, _stanceTransitionSpeed * delta);
		
		// Offset collision shape to keep feet on the ground
		_collisionShape.Position = new Vector3(0, _capsule.Height / 2f, 0);

		// Interpolate Camera Height (Eye level is slightly below top of capsule)
		Vector3 stancePos = _stancePivot.Position;
		stancePos.Y = Mathf.Lerp(stancePos.Y, _capsule.Height * 0.9f, _stanceTransitionSpeed * delta);
		_stancePivot.Position = stancePos;
	}

	private void HandleLeaning(float delta)
	{
		// Leaning is disabled while prone
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
			
			// TODO (Architecture Note): Introduce a RayCast3D here extending left/right. 
			// If it hits a wall, clamp _targetLeanOffset to prevent clipping the camera through level geometry.
		}

		// Interpolate Lean Pivot
		Vector3 leanRot = _leanPivot.Rotation;
		leanRot.Z = Mathf.Lerp(leanRot.Z, _targetLeanRotation, _leanSpeed * delta);
		_leanPivot.Rotation = leanRot;

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
		else if (Input.IsActionJustPressed("jump") && _currentStance == Stance.Stand)
		{
			velocity.Y = 4.5f; // Base jump velocity
		}

		// Determine target speed based on stance and sprint state
		_isSprinting = Input.IsActionPressed("sprint") && _currentStance == Stance.Stand && !Input.IsActionPressed("move_backward");
		float targetSpeed = _walkSpeed;
		
		if (_currentStance == Stance.Crouch) targetSpeed = _crouchSpeed;
		else if (_currentStance == Stance.Prone) targetSpeed = _proneSpeed;
		else if (_isSprinting) targetSpeed = _sprintSpeed;

		// Get Input Direction relative to the YAW PIVOT (where the player is looking)
		Vector2 inputDir = Input.GetVector("move_left", "move_right", "move_forward", "move_backward");
		Vector3 direction = (_yawPivot.Transform.Basis * new Vector3(inputDir.X, 0, inputDir.Y)).Normalized();

		// Apply Acceleration and Friction (Inertia modeling)
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
}
