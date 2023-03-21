
namespace PoEcs.Example
{
	public class SysBuff : System
	{
		private EntityManager.Query _query;

		public override void OnCreated()
		{
			
		}

		public override void OnInit()
		{
			_query = CreateQuery();
			_query.Register<DynamicBuffer<BfBuff>>();
		}

		public override void OnTick(float dt)
		{
			_query.ForEach<DynamicBuffer<BfBuff>>((entityId, buffer) =>
			{
				if (buffer.DataList.Count == 0)
				{
					buffer.DataList.Add(new BfBuff());
				}
				else
				{
					for (var i = 0; i < buffer.DataList.Count; i++)
					{
						var buffData = buffer.DataList[i];
						buffData.ElapsedFrame++;
						buffer.DataList[i] = buffData;
					}
				}
			});
		}
	}
}