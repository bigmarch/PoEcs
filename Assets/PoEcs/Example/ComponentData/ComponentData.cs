using UnityEngine;

namespace PoEcs.Example
{
	public struct CdPawnState : IComponentData
	{
		public Vector2 PendingInputMove;
		public bool PendingInputWeapon;

		public Vector2 Pos;
		public float Rot;
		public float Height;
		public bool HoldingWeapon;
		public bool PendingShoot;
	}

	// 继承于 IComponentData 的使用 Cd 开头。 
	public struct CdLocalPlayer : IComponentData
	{
	}

	// 继承于 IBufferElementData 的使用 Bf 开头
	public struct BfBuff : IBufferElementData
	{
		public int ElapsedFrame;
	}
}