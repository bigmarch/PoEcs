
namespace PoEcs.Example
{
	public class SysInputFromBridge : System
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
			// 读 bridge 数据
			var brData = World.GetBridgeData<BrLocalInput>();
			
			_query.ForEach<CdPawnState>((entityId, pawnStateData) =>
			{
				// 设置主玩家的操作。
				if (entityId == brData.LocalPlayerEntityId)
				{
					pawnStateData.PendingInputMove = brData.Move;
					pawnStateData.PendingInputWeapon = brData.Weapon;
					pawnStateData.PendingShoot = brData.Shoot;

					SetComponentData(entityId, pawnStateData);
				}
			});
		}
	}
}