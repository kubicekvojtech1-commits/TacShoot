using Godot;
using TacShoot.Core;

[GlobalClass]
public partial class PlayerInteractor : RayCast3D
{
	[Signal] public delegate void InteractTargetChangedEventHandler(string interactText);

	private IInteractable _currentTarget;

	public override void _PhysicsProcess(double delta)
	{
		IInteractable hitInteractable = null;

		if (IsColliding())
		{
			var collider = GetCollider();
			// Duck-type check for the interface
			if (collider is IInteractable interactable)
			{
				hitInteractable = interactable;
			}
		}

		// Only emit signals/update state if the target actually changes
		if (hitInteractable != _currentTarget)
		{
			_currentTarget = hitInteractable;
			EmitSignal(SignalName.InteractTargetChanged, _currentTarget?.GetInteractText() ?? "");
		}
	}

	public void TryInteract(Node3D interactorRoot)
	{
		_currentTarget?.Interact(interactorRoot);
	}
}
