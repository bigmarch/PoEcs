using System.Collections.Generic;
using UnityEngine;
using NotImplementedException = System.NotImplementedException;

namespace PoEcs.Example
{
	// BridgeData 使用 Br 开头命名
	public struct BrLocalInput : IBridgeData
	{
		public int LocalPlayerEntityId;
		public Vector2 Move;
		public bool Weapon;
		public bool Shoot;
		
		public void OnRegistered()
		{
			
		}
	}

	public struct BrWorldSnapshot : IBridgeData
	{
		public List<PawnInfo> AllPawnInfo;

		public struct PawnInfo
		{
			public int EntityId;
			public bool HoldingWeapon;
			public Vector2 Pos;
			public float Rot;
		}

		public void OnRegistered()
		{
			AllPawnInfo = new List<PawnInfo>();
		}
	}
}