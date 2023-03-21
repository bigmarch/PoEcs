using UnityEngine;

namespace PoEcs.Example
{
	// 例子，负责 esc 层面的所有操作。
	public class ExampleCore : MonoBehaviour
	{
		// public static ExampleCore Instance { get; private set; }

		public ExampleInputer Inputer;
		public ExamplePresenter Presenter;

		private World _world;
		public World World => _world;

		private int _localPlayerId;
		public int LocalPlayerId => _localPlayerId;

		void Awake()
		{
			// Instance = this;

			_world = new World();

			// 配置这个 world，内含那些东西。
			_world.RegisterComponentData<CdPawnState>();
			_world.RegisterComponentData<CdLocalPlayer>();
			_world.RegisterComponentData<DynamicBuffer<BfBuff>>();

			// 按照顺序
			_world.CreateSystem<SysInputFromBridge>();
			_world.CreateSystem<SysMove>();
			_world.CreateSystem<SysBuff>();
			_world.CreateSystem<SysOutputToBridge>();

			_world.RegisterBridge<BrLocalInput>();
			_world.RegisterBridge<BrWorldSnapshot>();

			_world.Init();

			Init();
		}

		// 初始化
		private void Init()
		{
			CreateLocalPlayer();
		}

		// Update is called once per frame
		void FixedUpdate()
		{
			Inputer.Input();

			float dt = Time.fixedDeltaTime;
			_world.Tick(dt);

			Presenter.Present();
		}

		private void CreateLocalPlayer()
		{
			_localPlayerId = _world.CreateEntity();
			_world.AddComponent<CdPawnState>(_localPlayerId);
			_world.AddComponent<CdLocalPlayer>(_localPlayerId);
			_world.AddComponent<DynamicBuffer<BfBuff>>(_localPlayerId);
		}
	}
}