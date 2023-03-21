
namespace PoEcs.Example
{
	public class SysOutputToBridge : System
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
			// output bridge 使用前先清空，向里面写。
			var brData = World.GetBridgeData<BrWorldSnapshot>();

			brData.AllPawnInfo.Clear();

			_query.ForEach<CdPawnState>((entityId, pawnStateData) =>
			{
				var pawnInfo = new BrWorldSnapshot.PawnInfo
				{
					EntityId = entityId,
					Pos = pawnStateData.Pos,
					Rot = pawnStateData.Rot,
					HoldingWeapon = pawnStateData.HoldingWeapon
				};
				brData.AllPawnInfo.Add(pawnInfo);
			});

			World.SetBridgeData(brData);
		}
	}
}