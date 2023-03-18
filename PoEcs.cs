using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.Pool;
using UnityEngine;

namespace PoEcs
{
	// 普通的 ComponentData 实现这个接口。
	public interface IComponentData
	{
	}

	// 数组类型的 ComponentData 实现这个接口。
	public interface IBufferElementData
	{
	}

	// Dynamic Buffer 是一种特殊的 ComponentData，其中包含一个指定类型的 List
	public struct DynamicBuffer<T> : IDynamicBuffer, IComponentData where T : IBufferElementData
	{
		private List<T> _dataList;
		public List<T> DataList => _dataList;

		public void Init()
		{
			Debug.Assert(_dataList == null);
			
			// list 从池里拿
			_dataList = ListPool<T>.Get();
		}

		public void Dispose()
		{
			Debug.Assert(_dataList != null);
			
			// list 回池
			ListPool<T>.Release(_dataList);
			_dataList = null;
		}
	}

	// 这个接口，是为了让 DynamicBuffer 能够运行 Dispose 方法，减少 Dynamic Buffer 的 gc。
	// 如果 AddDynamicBuffer 时，new List 的 Gc 可以忽略，则可以删除这个接口。
	public interface IDynamicBuffer
	{
		void Init();
		
		void Dispose();
	}

	// 
	public interface IBridgeData
	{
		void OnRegistered();
	}

	// EntityManager 都需要提供哪写方法：
	// https://docs.unity3d.com/Packages/com.unity.entities@0.1/manual/entity_manager.html
	public class EntityManager
	{
		// 一个元素，是一个
		private readonly List<GridRow> _grid;

		// 存着所有的 component data 的 type
		private readonly List<Type> _typeList;

		// 通过 component data type 得到这个 type 在 typeList 中的索引位置。
		private readonly Dictionary<Type, int> _typeToIndex;

		// 每个 component data 的 type 对应一个 action，在这一列发生变化时，调用
		private readonly Dictionary<Type, Action> _typeToAction;

		// 用于复用已经 destroy 的 entity。
		private readonly Queue<int> _invalidRowIndexQueue;

		private class GridRow
		{
			public bool Valid;
			public GridElement[] Element;
		}

		private class GridElement
		{
			public bool Valid;
			public IComponentData Data;
		}

		public EntityManager()
		{
			_grid = new List<GridRow>();

			_typeList = new List<Type>();

			_typeToIndex = new Dictionary<Type, int>();

			_typeToAction = new Dictionary<Type, Action>();

			_invalidRowIndexQueue = new Queue<int>();
		}

		// 通过类型，从 _typeToIndex 中拿到索引，加快访问。
		private int GetTypeIndex<T>()
			where T : struct, IComponentData
		{
			var type = typeof(T);
			return _typeToIndex[type];
		}
		//
		// private T GetElement<T>(GridRow row)
		// 	where T : struct, IComponentData
		// {
		// 	var typeIndex3 = GetTypeIndex<T>();
		// 	var element = row.Element[typeIndex3];
		// 	
		// 	return element;
		// }

		private void AddListenerToTypeAction<T>(Action callback)
			where T : struct, IComponentData
		{
			var type = typeof(T);
			_typeToAction[type] += callback;
		}

		private void RemoveListenerToTypeAction(Type type, Action callback)
		{
			// var type = typeof(T);
			Debug.Assert(_typeToAction.ContainsKey(type));
			_typeToAction[type] -= callback;
		}

		// 遍历所有 valid 的行
		private void IterateAllValidRow(Action<int, GridRow> action)
		{
			for (var entityId = 0; entityId < _grid.Count; entityId++)
			{
				var row = _grid[entityId];
				if (!row.Valid)
				{
					// 忽略未使用的行
					continue;
				}

				action.Invoke(entityId, row);
			}
		}

		// 给定 entity id 和 type，找到对应的 grid element
		private GridElement GetElement<T>(int entityId)
			where T : struct, IComponentData
		{
			var row = _grid[entityId];
			Debug.Assert(row.Valid);

			var typeIndex = GetTypeIndex<T>();
			var element = row.Element[typeIndex];

			return element;
		}

		// 在创建第一个 Entity 之前调用。
		public void RegisterComponentData<T>()
			where T : IComponentData
		{
			var type = typeof(T);

			Debug.Assert(_grid.Count == 0);
			Debug.Assert(!_typeList.Contains(type));

			_typeList.Add(type);
			// 记录这个 type 在 type list 中的 index，方便快速访问。
			_typeToIndex.Add(type, _typeList.Count - 1);
			_typeToAction.Add(type, () => { });
		}

		public bool HasEntity(int entityId)
		{
			if (entityId < 0 || entityId >= _grid.Count)
			{
				// 数字超过 grid 中的内容了
				return false;
			}

			return _grid[entityId].Valid;
		}

		// 创建一个新的
		public int CreateEntity()
		{
			GridRow row;
			int entityId;
			// 在回收站 queue 中找到 valid 为 false 的 row，复用它。
			if (_invalidRowIndexQueue.Count > 0)
			{
				var inValidRowIndex = _invalidRowIndexQueue.Dequeue();
				row = _grid[inValidRowIndex];
				Debug.Assert(!row.Valid);

				// entity id 就是行号，就是这一个 row 在 grid 中的位置
				entityId = inValidRowIndex;
			}
			else
			{
				// 添加一行
				row = new GridRow
				{
					// 之前注册了多少个 type，这个数组就多长。一列对应一个 type。
					Element = new GridElement[_typeList.Count]
				};

				// 新的一行中，生成所有 element。 
				for (var i = 0; i < row.Element.Length; i++)
				{
					row.Element[i] = new GridElement();
				}

				_grid.Add(row);

				// entity id 就是行号，就是这一个 row 在 grid 中的位置
				entityId = _grid.Count - 1;
			}

			// 不论是复用的，还是新创建的，都刷新这一行的内容。
			// row 的 valid 设置 true，并清空所有 element。
			row.Valid = true;

#if UNITY_EDITOR
			foreach (var e in row.Element)
			{
				Debug.Assert(!e.Valid);
			}
#endif
			return entityId;
		}

		public void DestroyEntity(int entityId)
		{
			_invalidRowIndexQueue.Enqueue(entityId);

			var row = _grid[entityId];
			row.Valid = false;
			for (var i = 0; i < row.Element.Length; i++)
			{
				var element = row.Element[i];

				element.Valid = false;

				// 设置成 default 之前，如果这是个 dynamic buffer，则运行它的 Dispose 方法。
				if (element.Data is IDynamicBuffer dynamicBuffer)
				{
					// 注意 DynamicBuffer 是值类型。
					dynamicBuffer.Dispose();
				}

				element.Data = default;

				// 拿到 type，触发对应的 action
				var type = _typeList[i];
				_typeToAction[type].Invoke();
			}
		}

		// 查询 Entity 身上是否有指定类型的 ComponentData
		public bool HasComponent<T>(int entityId)
			where T : struct, IComponentData
		{
			var element = GetElement<T>(entityId);
			return element.Valid;
		}

		public void AddComponent<T>(int entityId, T data)
			where T : struct, IComponentData
		{
			var element = GetElement<T>(entityId);
			Debug.Assert(!element.Valid);

			element.Valid = true;

			if (data is IDynamicBuffer dynamicBuffer)
			{
				dynamicBuffer.Init();
				// dynamicBuffer 是结构体，要赋值回去。
				data = (T)dynamicBuffer;
			}

			element.Data = data;

			// 类型为 T 的 component data 发生了变化，触发 action
			_typeToAction[typeof(T)].Invoke();
		}

		// 覆盖 Entity 身上的指定类型的 ComponentData
		public void SetComponentData<T>(int entityId, T data)
			where T : struct, IComponentData
		{
			var element = GetElement<T>(entityId);
			Debug.Assert(element.Valid);
			element.Data = data;
		}

		// 获得 Entity 身上的指定类型的 ComponentData
		public T GetComponentData<T>(int entityId)
			where T : struct, IComponentData
		{
			var element = GetElement<T>(entityId);
			Debug.Assert(element.Valid);
			return (T)element.Data;
		}

		// 删除 Entity 身上的指定类型的 ComponentData
		public void RemoveComponent<T>(int entityId)
			where T : struct, IComponentData
		{
			var element = GetElement<T>(entityId);
			Debug.Assert(element.Valid);
			
			element.Valid = false;

			// 如果是 Dynamic data
			if (element.Data is IDynamicBuffer dynamicBuffer)
			{
				dynamicBuffer.Dispose();
			}
			
			element.Data = default;

			// 类型为 T 的 component data 发生了变化，触发 action
			_typeToAction[typeof(T)].Invoke();
		}

		// 查询 Entity 身上是否有指定类型的
		public bool HasDynamicBuffer<T>(int entityId)
			where T : struct, IBufferElementData
		{
			var element = GetElement<DynamicBuffer<T>>(entityId);
			return element.Valid;
		}

		// 获得 Entity 身上的指定类型的 DynamicBuffer
		// public void AddDynamicBuffer<T>(int entityId)
		// 	where T : struct, IBufferElementData
		// {
		// 	var element = GetElement<DynamicBuffer<T>>(entityId);
		// 	Debug.Assert(!element.Valid);
		// 	element.Valid = true;
		//
		// 	DynamicBuffer<T> newBuffer = default;
		// 	// dynamic buffer 中有 list，这里调用初始化方法，开辟内存。
		// 	newBuffer.InitData();
		// 	element.Data = newBuffer;
		//
		// 	// 类型为 T 的 component data 发生了变化，触发 action
		// 	_typeToAction[typeof(DynamicBuffer<T>)].Invoke();
		// }
		//
		// public DynamicBuffer<T> GetDynamicBuffer<T>(int entityId)
		// 	where T : struct, IBufferElementData
		// {
		// 	var element = GetElement<DynamicBuffer<T>>(entityId);
		// 	return (DynamicBuffer<T>)element.Data;
		// }
		//
		// public void RemoveDynamicBuffer<T>(int entityId)
		// 	where T : struct, IBufferElementData
		// {
		// 	var element = GetElement<DynamicBuffer<T>>(entityId);
		// 	Debug.Assert(element.Valid);
		// 	element.Valid = false;
		//
		// 	// 设置成 default 之前，清空 buffer 里的东西，注意 DynamicBuffer 是值类型。
		// 	DynamicBuffer<T> buffer = (DynamicBuffer<T>)element.Data;
		// 	buffer.DisposeData();
		//
		// 	element.Data = default;
		//
		// 	// 类型为 T 的 component data 发生了变化，触发 action
		// 	_typeToAction[typeof(DynamicBuffer<T>)].Invoke();
		// }

		// 遍历指定类型的 ComponentData
		public void ForEach<T>(Action<int, T> action)
			where T : struct, IComponentData
		{
			var typeIndex = GetTypeIndex<T>();
			// 遍历所有 valid 的行
			IterateAllValidRow((entityId, row) =>
			{
				var element = row.Element[typeIndex];
				if (element.Valid)
				{
					action.Invoke(entityId, (T)element.Data);
				}
			});
		}

		public void ForEach<T, T1>(Action<int, T, T1> action)
			where T : struct, IComponentData
			where T1 : struct, IComponentData
		{
			var typeIndex = GetTypeIndex<T>();
			var typeIndex1 = GetTypeIndex<T1>();
			IterateAllValidRow((entityId, row) =>
			{
				var element = row.Element[typeIndex];
				var element1 = row.Element[typeIndex1];
				if (element.Valid
				    && element1.Valid)
				{
					// 任何一个 tye 不存在都忽略。
					return;
				}

				action.Invoke(entityId, (T)element.Data, (T1)element1.Data);
			});
		}

		public void ForEach<T, T1, T2>(Action<int, T, T1, T2> action)
			where T : struct, IComponentData
			where T1 : struct, IComponentData
			where T2 : struct, IComponentData
		{
			var typeIndex = GetTypeIndex<T>();
			var typeIndex1 = GetTypeIndex<T1>();
			var typeIndex2 = GetTypeIndex<T2>();
			IterateAllValidRow((entityId, row) =>
			{
				var element = row.Element[typeIndex];
				var element1 = row.Element[typeIndex1];
				var element2 = row.Element[typeIndex2];
				if (element.Valid
				    && element1.Valid
				    && element2.Valid)
				{
					action.Invoke(entityId,
						(T)element.Data,
						(T1)element1.Data,
						(T2)element2.Data);
				}
			});
		}

		public void ForEach<T, T1, T2, T3>(Action<int, T, T1, T2, T3> action)
			where T : struct, IComponentData
			where T1 : struct, IComponentData
			where T2 : struct, IComponentData
			where T3 : struct, IComponentData
		{
			var typeIndex = GetTypeIndex<T>();
			var typeIndex1 = GetTypeIndex<T1>();
			var typeIndex2 = GetTypeIndex<T2>();
			var typeIndex3 = GetTypeIndex<T3>();
			IterateAllValidRow((entityId, row) =>
			{
				var element = row.Element[typeIndex];
				var element1 = row.Element[typeIndex1];
				var element2 = row.Element[typeIndex2];
				var element3 = row.Element[typeIndex3];
				if (element.Valid
				    && element1.Valid
				    && element2.Valid
				    && element3.Valid)
				{
					action.Invoke(
						entityId,
						(T)element.Data,
						(T1)element1.Data,
						(T2)element2.Data,
						(T3)element3.Data);
				}
			});
		}

		public Query CreateQuery()
		{
			return new Query(this);
		}

		public class Query
		{
			// 所有符合条件的 entity id，仅在需要时更新
			private readonly List<int> _entityIdList;

			private readonly EntityManager _manager;

			// 必须同时包含这几个 type
			private readonly List<Type> _withInTypeList;
			private readonly List<int> _withInTypeIndexList;

			// 在下一次 iterate 之前，是否需要刷新。
			private bool _needRefreshBeforeNextIterate;

			// protected EntityManager Manager => _manager;
			public Query(EntityManager manager)
			{
				_manager = manager;
				_entityIdList = new List<int>();
				_withInTypeList = new List<Type>();
				_withInTypeIndexList = new List<int>();
			}

			// ~Query()
			// {
			// 	Debug.Log("1");
			// }

			// public void Dispose()
			public void DeregisterAll()
			{
				// 挨个 deregister
				for (var i = 0; i < _withInTypeList.Count; i++)
				{
					var type = _withInTypeList[i];
					_manager.RemoveListenerToTypeAction(type, TurnOnNeedRefreshBeforeNextIterate);
				}
			}

			// 每次调用 register 一个 T
			public void Register<T>()
				where T : struct, IComponentData
			{
				var type = typeof(T);
				Debug.Assert(!_withInTypeList.Contains(type));
				_withInTypeList.Add(type);
				_withInTypeIndexList.Add(_manager.GetTypeIndex<T>());

				_needRefreshBeforeNextIterate = true;

				// 监听 manager 中的 type 那一列的变化
				_manager.AddListenerToTypeAction<T>(TurnOnNeedRefreshBeforeNextIterate);
			}

			// 打开标记 RefreshBeforeNextIterate
			private void TurnOnNeedRefreshBeforeNextIterate()
			{
				_needRefreshBeforeNextIterate = true;
			}

			// 根据记录的 type，刷新 entityIdList。
			// private void Refresh()
			// {
			// 	_entityIdList.Clear();
			// 	_manager.IterateAllValidRow((entityId, row) =>
			// 	{
			// 		bool rowValid = CheckRowValid(row, _withInTypeIndexList);
			// 		if (rowValid)
			// 		{
			// 			_entityIdList.Add(entityId);
			// 		}
			// 	});
			// }

			// TODO HasChanged 方法，关心的 component data 是否有变化。提供给 system。
			// PoEcs 可能不需要提供响应式的方法，目前没有这个需求。
			// 对于 System 来说，如果 query 没有 change，则跳过 foreach，倒是一种优化。
			// 对于 buff system 就没用，change 不 change 都需要 tick。
			// public bool HasChanged()
			// {
			// 	if (_needRefreshBeforeNextIterate)
			// 	{
			// 		return true;
			// 	}
			//
			// 	// TODO query 关心的 type 列中，任何一列中有 element 发生变化，则 return true。
			// 	return true;
			// }

			private void IterateAllRow(Action<int, GridRow> action)
			{
				// 查看是否需要 Refresh
				if (_needRefreshBeforeNextIterate)
				{
					// 下此不刷新了，除非开关再次亮起。
					_needRefreshBeforeNextIterate = false;

					// 遍历所有，找到 valid 的 row，顺便更新 _entityIdList，触发 action。
					_entityIdList.Clear();
					_manager.IterateAllValidRow((entityId, row) =>
					{
						bool rowValid = CheckRowValid(row, _withInTypeIndexList);
						if (rowValid)
						{
							_entityIdList.Add(entityId);
							action.Invoke(entityId, row);
						}
					});
				}
				else
				{
					// 不需要刷新，直接使用 _entityIdList 进行遍历，触发 action。
					for (var i = 0; i < _entityIdList.Count; i++)
					{
						int entityId = _entityIdList[i];

						var row = _manager._grid[entityId];
						Debug.Assert(row.Valid);
						action.Invoke(entityId, row);
					}
				}
			}

			// public void ForEach(Action<ComponentDataGroup> action)
			// {
			// 	IterateAllRow((entityId, row) =>
			// 	{
			// 		ComponentDataGroup cdf = new ComponentDataGroup();
			// 		
			// 		RowToComponentDataGroup(row);
			// 		// var typeIndex = _manager.GetTypeIndex<T>();
			// 		// var element = row.Element[typeIndex];
			// 		action.Invoke(RowToComponentDataGroup);
			// 	});
			// }

			public void ForEach<T>(Action<int, T> action)
				where T : struct, IComponentData
			{
				var typeIndex = _manager.GetTypeIndex<T>();
				IterateAllRow((entityId, row) =>
				{
					var element = row.Element[typeIndex];
					action.Invoke(entityId, (T)element.Data);
				});
			}

			public void ForEach<T, T1>(Action<int, T, T1> action)
				where T : struct, IComponentData
				where T1 : struct, IComponentData
			{
				var typeIndex = _manager.GetTypeIndex<T>();
				var typeIndex1 = _manager.GetTypeIndex<T1>();
				IterateAllRow((entityId, row) =>
				{
					var element = row.Element[typeIndex];
					var element1 = row.Element[typeIndex1];

					action.Invoke(entityId, (T)element.Data, (T1)element1.Data);
				});
			}

			public void ForEach<T, T1, T2>(Action<int, T, T1, T2> action)
				where T : struct, IComponentData
				where T1 : struct, IComponentData
				where T2 : struct, IComponentData
			{
				var typeIndex = _manager.GetTypeIndex<T>();
				var typeIndex1 = _manager.GetTypeIndex<T1>();
				var typeIndex2 = _manager.GetTypeIndex<T2>();
				IterateAllRow((entityId, row) =>
				{
					var element = row.Element[typeIndex];
					var element1 = row.Element[typeIndex1];
					var element2 = row.Element[typeIndex2];

					action.Invoke(entityId, (T)element.Data, (T1)element1.Data, (T2)element2.Data);
				});
			}

			public void ForEach<T, T1, T2, T3>(Action<int, T, T1, T2, T3> action)
				where T : struct, IComponentData
				where T1 : struct, IComponentData
				where T2 : struct, IComponentData
				where T3 : struct, IComponentData
			{
				var typeIndex = _manager.GetTypeIndex<T>();
				var typeIndex1 = _manager.GetTypeIndex<T1>();
				var typeIndex2 = _manager.GetTypeIndex<T2>();
				var typeIndex3 = _manager.GetTypeIndex<T3>();
				IterateAllRow((entityId, row) =>
				{
					var element = row.Element[typeIndex];
					var element1 = row.Element[typeIndex1];
					var element2 = row.Element[typeIndex2];
					var element3 = row.Element[typeIndex3];

					action.Invoke(entityId, (T)element.Data, (T1)element1.Data, (T2)element2.Data, (T3)element3.Data);
				});
			}

			// 查看某个 row，指定的 type 类型，是不是全是 valid。
			private static bool CheckRowValid(GridRow row, List<int> withInTypeIndexList)
			{
				// 只有包含所有 type 的，才是 right row。
				bool result = true;
				for (var i = 0; i < withInTypeIndexList.Count; i++)
				{
					var typeIndex = withInTypeIndexList[i];
					var element = row.Element[typeIndex];
					if (!element.Valid)
					{
						result = false;
						break;
					}
				}

				return result;
			}
		}
	}

	public abstract class System
	{
		private World _world;

		private EntityManager _entityManager;
		
		protected T GetBridgeData<T>()
			where T : struct, IBridgeData
		{
			return _world.BridgeManager.GetBridgeData<T>();
		}

		protected void SetBridgeData<T>(T data)
			where T : struct, IBridgeData
		{
			_world.BridgeManager.SetBridgeData(data);
		}

		public void SetWorld(World world)
		{
			_world = world;
		}

		public void SetEntityManager(EntityManager entityManager)
		{
			_entityManager = entityManager;
		}

		protected EntityManager.Query CreateQuery()
		{
			return _entityManager.CreateQuery();
		}

		protected void SetComponentData<T>(int entityId, T data)
			where T : struct, IComponentData
		{
			_entityManager.SetComponentData(entityId, data);
		}

		public abstract void OnCreated();
		
		public abstract void OnInit();
		public abstract void OnTick(float dt);
	}

	public class World
	{
		private readonly EntityManager _entityManager;

		// public EntityManager EntityManager => _entityManager;

		private readonly List<System> _systemList;

		private readonly BridgeManager _bridgeManager;

		public BridgeManager BridgeManager => _bridgeManager;

		public World()
		{
			_entityManager = new EntityManager();
			_systemList = new List<System>();
			_bridgeManager = new BridgeManager();
		}

		public void RegisterComponentData<T>()
			where T : IComponentData
		{
			_entityManager.RegisterComponentData<T>();
		}

		public void CreateSystem<T>()
			where T : System, new()
		{
#if UNITY_EDITOR
			Debug.Assert(_systemList.All(system => system is not T));
#endif
			T newSystem = new T();
			newSystem.SetWorld(this);
			newSystem.SetEntityManager(_entityManager);
			_systemList.Add(newSystem);
		}

		public void Init()
		{
			// 顺序，遍历。
			for (var i = 0; i < _systemList.Count; i++)
			{
				_systemList[i].OnInit();
			}
		}

		public void Tick(float deltaTime)
		{
			// 顺序，遍历。
			for (var i = 0; i < _systemList.Count; i++)
			{
				_systemList[i].OnTick(deltaTime);
			}
		}
		
		public int CreateEntity()
		{
			return _entityManager.CreateEntity();
		}

		public void DestroyEntity(int entityId)
		{
			_entityManager.DestroyEntity(entityId);
		}

		public bool HasEntity(int entityId)
		{
			return _entityManager.HasEntity(entityId);
		}
		
		public void AddComponent<T>(int entityId, T data = default)
			where T : struct, IComponentData
		{
			_entityManager.AddComponent(entityId, data);
		}
		
		public T GetComponentData<T>(int entityId)
			where T : struct, IComponentData
		{
			return _entityManager.GetComponentData<T>(entityId);
		}
		
		public void SetComponentData<T>(int entityId, T data)
			where T : struct, IComponentData
		{
			_entityManager.SetComponentData<T>(entityId, data);
		}

		public void SetBridgeData<T>(T data)
			where T : struct, IBridgeData
		{
			_bridgeManager.SetBridgeData(data);
		}

		public T GetBridgeData<T>()
			where T : struct, IBridgeData
		{
			return _bridgeManager.GetBridgeData<T>();
		}

		public void RegisterBridge<T>()
			where T : struct, IBridgeData
		{
			_bridgeManager.Register<T>();
		}
	}

	public class BridgeManager
	{
		private readonly Dictionary<Type, IBridgeData> _dic;

		public BridgeManager()
		{
			_dic = new Dictionary<Type, IBridgeData>();
		}

		public void Register<T>()
			where T : struct, IBridgeData
		{
			Type key = typeof(T);
			T newData = default(T);
			newData.OnRegistered();
			_dic.Add(key, newData);
		}
		
		public void SetBridgeData<T>(T data)
			where T : struct, IBridgeData
		{
			Type key = typeof(T);
			_dic[key] = data;
		}

		public T GetBridgeData<T>()
			where T : struct, IBridgeData
		{
			Type key = typeof(T);
			if (_dic.TryGetValue(key, out IBridgeData data))
			{
				return (T)data;
			}

			Debug.LogError("Bridge 没有这个类型的 Bridge Data: " + key);
			return default;
		}
	}

	public static class Extensions
	{
		// public static World AddComponent<T>(this World world, int entityId, T data = default)
		// {
		// 	return	world.AddComponent(entityId, data);
		// }
	}
}