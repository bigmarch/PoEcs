using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PoEcs.Example
{
	public class ExampleInputer : MonoBehaviour
	{
		public ExampleCore Core;
		private PlayerInput _playerInput;

		private bool _shoot;

		void Awake()
		{
			_playerInput = GetComponent<PlayerInput>();
		}

		public void Input()
		{
			BrLocalInput brLocalInput = default;
			brLocalInput.LocalPlayerEntityId = Core.LocalPlayerId;
			brLocalInput.Move = _playerInput.actions["Move"].ReadValue<Vector2>();
			brLocalInput.Weapon = _playerInput.actions["Weapon"].ReadValue<float>() > 0;
			brLocalInput.Shoot = _shoot;
			Core.World.SetBridgeData(brLocalInput);

			_shoot = false;
		}

		void Update()
		{
			if (Keyboard.current.spaceKey.wasPressedThisFrame)
			{
				// TODO 需要单独再写一个 local 的 input 来处理 update consume 的问题。
				_shoot = true;
			}
		}
	}
}