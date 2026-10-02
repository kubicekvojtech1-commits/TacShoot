using Godot;

namespace TacShoot.Core;

public interface IInteractable
{
	string GetInteractText();
	void Interact(Node3D interactor);
}
