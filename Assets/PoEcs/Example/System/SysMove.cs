using UnityEngine;

namespace PoEcs.Example
{
	public class SysMove : System
	{
		private EntityManager.Query _query;

		public override void OnCreated()
		{
			
		}

		public override void OnInit()
		{
			_query = CreateQuery();
			_query.Register<CdPawnState>();
		}

		public override void OnTick(float dt)
		{
			_query.ForEach<CdPawnState>((entityId, pawnState) =>
			{
				float moveSpeed;
				float rotateSpeed;

				pawnState.HoldingWeapon = pawnState.PendingInputWeapon;
				if (pawnState.PendingInputWeapon)
				{
					// TODO 先写数字，之后做成配置文件系统
					moveSpeed = 3;
					rotateSpeed = 360;
				}
				else
				{
					moveSpeed = 5;
					rotateSpeed = 1080;
				}

				pawnState.Pos += pawnState.PendingInputMove * (moveSpeed * dt);
				if (pawnState.PendingInputMove != Vector2.zero)
				{
					float targetAngle = Vec2ToAngle(pawnState.PendingInputMove);
					pawnState.Rot = Mathf.MoveTowardsAngle(pawnState.Rot, targetAngle, rotateSpeed * dt);
				}

				// 设置 input data
				SetComponentData(entityId, pawnState);

				// 测试创建 entity 的功能
				if (pawnState.PendingShoot)
				{
					var tempId = World.CreateEntity();
					World.AddComponent<CdPawnState>(tempId, new CdPawnState
					{
						Pos = Random.insideUnitCircle * 4,
					});
					World.AddComponent<DynamicBuffer<BfBuff>>(tempId);
					Debug.Log("创建 entity " + tempId);
				}
			});
		}
		
		public static float Vec2ToAngle(Vector2 dir)
		{
			return Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
		}
	}
}