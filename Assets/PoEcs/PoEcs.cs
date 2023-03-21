using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.UI;
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
		// IndexTable 是一个二维表，一行表示一个 entity，一列表示一个 data type，一个元素表示这个 entity 的指定 type 的 data 在 data table 中的索引位置。
		// 它的 element 中的索引，可以用于去 data table 中查询实际的 data。
		private class IndexTable
		{
			// 行的结构
			private class Row
			{
				// 这一行是否启用
				public bool Valid;

				// 这一行中，每个 element 是一个 index，通过这个 index 可以从 data table 中拿到对应的数据。
				public int[] DataTableElementIndexArr;
			}

			// 一行表示一个 entity
			private readonly List<Row> _rowList;
			
			// 用于复用行
			private readonly Queue<int> _recycleIndexQueue;

			private int _columnCount;
			
			public IndexTable()
			{
				_rowList = new List<Row>();
				_recycleIndexQueue = new Queue<int>();
			}

			// 增加列
			public void AddColumn()
			{
				_columnCount++;
			}

			// 增行。返回新行的 id。
			public int AddRow()
			{
				Row newRow;
				int rowId;
				if (_recycleIndexQueue.Count > 0)
				{
					var inValidRowIndex = _recycleIndexQueue.Dequeue();
					newRow = _rowList[inValidRowIndex];
					Debug.Assert(!newRow.Valid);

					// entity id 就是行号，就是这一个 row 在 grid 中的位置
					rowId = inValidRowIndex;
				}
				else
				{
					// 添加一行
					newRow = new Row
					{
						// 之前注册了多少个 type，这个数组就多长。一列对应一个 type。
						DataTableElementIndexArr = new int[_columnCount]
					};

					// IndexTableElement 是结构体。
					// 新的一行中，生成所有 element。 
					for (var i = 0; i < newRow.DataTableElementIndexArr.Length; i++)
					{
						newRow.DataTableElementIndexArr[i] = -1;
					}

					_rowList.Add(newRow);

					// id 就是行号，就是这一个 row 在 row list中的位置
					rowId = _rowList.Count - 1;
				}

				// 不论是复用的，还是新创建的，都刷新这一行的内容。
				// row 的 valid 设置 true，并清空所有 element。
				newRow.Valid = true;

#if UNITY_EDITOR
				foreach (var index in newRow.DataTableElementIndexArr)
				{
					Debug.Assert(index == -1);
				}
#endif
				return rowId;
			}

			// 删行。并重置这一行中每个 element
			public void DeleteRow(int rowId)
			{
				_recycleIndexQueue.Enqueue(rowId);

				var row = _rowList[rowId];
				row.Valid = false;
				for (var i = 0; i < row.DataTableElementIndexArr.Length; i++)
				{
					// 所有 index 都清空。
					row.DataTableElementIndexArr[i] = -1;
				}
			}

			public int[] GetRowIndexArr(int rowId)
			{
				Debug.Assert(_rowList[rowId].Valid);
				return _rowList[rowId].DataTableElementIndexArr;
			}

			// 改行里的内容。修改指定的 element。
			public void UpdateElement(int entityId, int dataTypeId, int elementData)
			{
				var row = _rowList[entityId];
				Debug.Assert(row.Valid);
				row.DataTableElementIndexArr[dataTypeId] = elementData;
			}

			// 查行里的内容。查这一行，查指定的 element。返回 -1 则表示 invalid
			public int GetElement(int entityId, int dataTypeId)
			{
				var row = _rowList[entityId];
				Debug.Assert(row.Valid);
				return row.DataTableElementIndexArr[dataTypeId];
			}

			public void ForEachRow(Action<int, int[]> action)
			{
				for (var i = 0; i < _rowList.Count; i++)
				{
					var entityId = i;
					var row = _rowList[i];
					if (row.Valid)
					{
						action.Invoke(entityId, row.DataTableElementIndexArr);
					}
				}
			}
		}

		// DataTable 一行是一个 list，每个 list 表示一类 component data 的集合，是变长的。
		// 外部传入类型 T，和 index，能够获得对应 list 中的 data。
		private class DataTable
		{
			private class Row
			{
				// 每行不一样长
				public List<IComponentData> Elements;

				// 用于回收
				public Queue<int> RecycleElementIndexQueue;

				public Action OnChanged;
			}

			private readonly List<Row> _rowList;
			
			public DataTable()
			{
				_rowList = new List<Row>();
				// _typeToIndexDic = new Dictionary<Type, int>();
			}

			// 增行。增加数据类型的 list，应该在系统最开始调用。
			public void AddRow<T>()
				where T : struct, IComponentData
			{
				var newRow = new Row
				{
					Elements = new List<IComponentData>(),
					RecycleElementIndexQueue = new Queue<int>()
				};
				_rowList.Add(newRow);
			}

			// 增加行里的内容
			public int AddElement(int dataTypeId, IComponentData data)
				// where T : struct, IComponentData
			{
				if (data is IDynamicBuffer buffer)
				{
					buffer.Init();
					data = (IComponentData)buffer;
				}

				Row row = GetRow(dataTypeId);

				int elementIndex;
				if (row.RecycleElementIndexQueue.Count > 0)
				{
					elementIndex = row.RecycleElementIndexQueue.Dequeue();
					row.Elements[elementIndex] = data;
				}
				else
				{
					row.Elements.Add(data);
					elementIndex = row.Elements.Count - 1;
				}

				// 这一行的结构变化了，必然某个 entity 关联的组件发生了变化，通知外部。
				row.OnChanged?.Invoke();
				
				return elementIndex;
			}
			
			public void DeleteElement(int dataTypeId, int elementIndex)
			{
				Row row = _rowList[dataTypeId];
				
				// 需要回收的 index，肯定在 row.Elements 范围内。
				Debug.Assert(elementIndex >= 0 && elementIndex < row.Elements.Count);
				
				var data = row.Elements[elementIndex];

				if (data is IDynamicBuffer buffer)
				{
					buffer.Dispose();
				}
				
				// 放到 recycle index queue 中就是删除，下一次 add 的时候，会被分走。
				row.RecycleElementIndexQueue.Enqueue(elementIndex);

				// 这一行的结构变化了，必然某个 entity 关联的组件发生了变化，通知外部。
				row.OnChanged?.Invoke();
			}
			
			// 改行里的内容
			public void UpdateElement(int dataTypeId, int elementIndex, IComponentData data)
			{
				// 根据 T 拿到指定的行，改第 index 个元素。
				Row row = GetRow(dataTypeId);
#if UNITY_EDITOR
				// 这个 index 不在回收站里，是 valid 的。
				Debug.Assert(!row.RecycleElementIndexQueue.Contains(elementIndex));
#endif
				row.Elements[elementIndex] = data;
			}

			// 查行里的内容
			public T GetElement<T>(int dataTypeId, int elementIndex)
				where T : struct, IComponentData
			{
				// 根据 T 拿到指定的行，改第 index 个元素。
				Row row = GetRow(dataTypeId);
#if UNITY_EDITOR
				// 这个 index 不在回收站里，是 valid 的。
				Debug.Assert(!row.RecycleElementIndexQueue.Contains(elementIndex));
#endif
				return (T)row.Elements[elementIndex];
			}

			private Row GetRow(int dataTypeId)
			{
				Row row = _rowList[dataTypeId];
				return row;
			}

			public void AddListenerToChanged(int dataTypeId, Action action)
			{
				Row row = GetRow(dataTypeId);
				row.OnChanged += action;
			}

			public void RemoveListenerToChanged(int dataTypeId, Action action)
			{
				Row row = GetRow(dataTypeId);
				row.OnChanged -= action;
			}
		}

		private readonly IndexTable _indexTable;

		// 一个元素，是一个
		private readonly DataTable _dataTable;

		private readonly Dictionary<Type, int> _dataTypeToDataTypeIdDic;

		public EntityManager()
		{
			_indexTable = new IndexTable();
			_dataTable = new DataTable();
			
			_dataTypeToDataTypeIdDic = new Dictionary<Type, int>();
		}

		// 给定 type，转换出 type id
		private int GetDataTypeId<T>()
			where T : struct, IComponentData
		{
			Type type = typeof(T);
			return _dataTypeToDataTypeIdDic[type];
		}

		// 在创建第一个 Entity 之前调用。
		public void RegisterComponentData<T>()
			where T : struct, IComponentData
		{
			// 创建这个新的 T 的 type id
			int newId = _dataTypeToDataTypeIdDic.Count;
			_dataTypeToDataTypeIdDic.Add(typeof(T), newId);
			
			// index 增加一列
			_indexTable.AddColumn();
			
			// data 增加一行。
			_dataTable.AddRow<T>();
		}
		
		// 创建一个新的
		public int CreateEntity()
		{
			int entityId = _indexTable.AddRow();
			// create entity 时候，不需要操作 data table。
			return entityId;
		}

		public void DestroyEntity(int entityId)
		{
			// 操作 data table
			// 先把这个 entity 身上的 component 都给卸了。
			int[] rowIndexArr = _indexTable.GetRowIndexArr(entityId);
			for (var i = 0; i < rowIndexArr.Length; i++)
			{
				var dataTypeId = i;
				var indexInTableData = rowIndexArr[i];
				if (indexInTableData == -1)
				{
					continue;
				}

				// 告诉 data table，回收这个 element
				_dataTable.DeleteElement(dataTypeId, indexInTableData);
			}
				
			// 操作 index table
			_indexTable.DeleteRow(entityId);
		}

		public void AddComponent<T>(int entityId, T data)
			where T : struct, IComponentData
		{
			int dataTypeId = GetDataTypeId<T>();
			
			// 操作 _dataTable，添加 data，获得索引
			var elementIndex = _dataTable.AddElement(dataTypeId, data);
			
			// 操作 _indexTable, 索引放到 table 中。
			_indexTable.UpdateElement(entityId, dataTypeId, elementIndex);
		}

		// 覆盖 Entity 身上的指定类型的 ComponentData
		public void SetComponentData<T>(int entityId, T data)
			where T : struct, IComponentData
		{
			// 操作 _indexTable, 拿到索引
			var dataTypeId = GetDataTypeId<T>();

			int elementIndex = _indexTable.GetElement(entityId, dataTypeId);

			// 操作 _dataTable, 
			_dataTable.UpdateElement(dataTypeId, elementIndex, data);
		}

		// 获得 Entity 身上的指定类型的 ComponentData
		public T GetComponentData<T>(int entityId)
			where T : struct, IComponentData
		{
			// 操作 _indexTable, 拿到索引
			var dataTypeId = GetDataTypeId<T>();

			int elementIndex = _indexTable.GetElement(entityId, dataTypeId);

			// 操作 _dataTable, 拿到数据
			return _dataTable.GetElement<T>(dataTypeId, elementIndex);
		}

		// 删除 Entity 身上的指定类型的 ComponentData
		public void RemoveComponent<T>(int entityId)
			where T : struct, IComponentData
		{
			var dataTypeId = GetDataTypeId<T>();
			int elementIndex = _indexTable.GetElement(entityId, dataTypeId);
			
			// 告诉 data table，回收这个 element
			_dataTable.DeleteElement(dataTypeId, elementIndex);
		}

		// 查询 Entity 身上是否有指定类型的
		// public bool HasDynamicBuffer<T>(int entityId)
		// 	where T : struct, IBufferElementData
		// {
		// 	var element = GetElement<DynamicBuffer<T>>(entityId);
		// 	return element.Valid;
		// }

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
		// public void ForEach<T>(Action<int, T> action)
		// 	where T : struct, IComponentData
		// {
		// 	var dataTypeId = GetdataTypeId<T>();
		// 	// 遍历所有 valid 的行
		// 	IterateAllValidRow((entityId, row) =>
		// 	{
		// 		var element = row.Element[dataTypeId];
		// 		if (element.Valid)
		// 		{
		// 			action.Invoke(entityId, (T)element.Data);
		// 		}
		// 	});
		// }
		//
		// public void ForEach<T, T1>(Action<int, T, T1> action)
		// 	where T : struct, IComponentData
		// 	where T1 : struct, IComponentData
		// {
		// 	var dataTypeId = GetdataTypeId<T>();
		// 	var dataTypeId1 = GetdataTypeId<T1>();
		// 	IterateAllValidRow((entityId, row) =>
		// 	{
		// 		var element = row.Element[dataTypeId];
		// 		var element1 = row.Element[dataTypeId1];
		// 		if (element.Valid
		// 		    && element1.Valid)
		// 		{
		// 			// 任何一个 tye 不存在都忽略。
		// 			return;
		// 		}
		//
		// 		action.Invoke(entityId, (T)element.Data, (T1)element1.Data);
		// 	});
		// }
		//
		// public void ForEach<T, T1, T2>(Action<int, T, T1, T2> action)
		// 	where T : struct, IComponentData
		// 	where T1 : struct, IComponentData
		// 	where T2 : struct, IComponentData
		// {
		// 	var dataTypeId = GetdataTypeId<T>();
		// 	var dataTypeId1 = GetdataTypeId<T1>();
		// 	var dataTypeId2 = GetdataTypeId<T2>();
		// 	IterateAllValidRow((entityId, row) =>
		// 	{
		// 		var element = row.Element[dataTypeId];
		// 		var element1 = row.Element[dataTypeId1];
		// 		var element2 = row.Element[dataTypeId2];
		// 		if (element.Valid
		// 		    && element1.Valid
		// 		    && element2.Valid)
		// 		{
		// 			action.Invoke(entityId,
		// 				(T)element.Data,
		// 				(T1)element1.Data,
		// 				(T2)element2.Data);
		// 		}
		// 	});
		// }
		//
		// public void ForEach<T, T1, T2, T3>(Action<int, T, T1, T2, T3> action)
		// 	where T : struct, IComponentData
		// 	where T1 : struct, IComponentData
		// 	where T2 : struct, IComponentData
		// 	where T3 : struct, IComponentData
		// {
		// 	var dataTypeId = GetdataTypeId<T>();
		// 	var dataTypeId1 = GetdataTypeId<T1>();
		// 	var dataTypeId2 = GetdataTypeId<T2>();
		// 	var dataTypeId3 = GetdataTypeId<T3>();
		// 	IterateAllValidRow((entityId, row) =>
		// 	{
		// 		var element = row.Element[dataTypeId];
		// 		var element1 = row.Element[dataTypeId1];
		// 		var element2 = row.Element[dataTypeId2];
		// 		var element3 = row.Element[dataTypeId3];
		// 		if (element.Valid
		// 		    && element1.Valid
		// 		    && element2.Valid
		// 		    && element3.Valid)
		// 		{
		// 			action.Invoke(
		// 				entityId,
		// 				(T)element.Data,
		// 				(T1)element1.Data,
		// 				(T2)element2.Data,
		// 				(T3)element3.Data);
		// 		}
		// 	});
		// }

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
			// private readonly List<Type> _withInTypeList;
			private readonly List<int> _withInDataTypeIdList;

			// 在下一次 iterate 之前，是否需要刷新。
			private bool _needRefreshBeforeNextIterate;

			// protected EntityManager Manager => _manager;
			public Query(EntityManager manager)
			{
				_manager = manager;
				_entityIdList = new List<int>();
				// _withInTypeList = new List<Type>();
				_withInDataTypeIdList = new List<int>();
			}

			// ~Query()
			// {
			// 	Debug.Log("1");
			// }

			public void DeregisterAll()
			{
				// 挨个 deregister
				for (var i = 0; i < _withInDataTypeIdList.Count; i++)
				{
					var withInDataTypeId = _withInDataTypeIdList[i];
					_manager._dataTable.RemoveListenerToChanged(withInDataTypeId, TurnOnNeedRefreshBeforeNextIterate);
				}
			}

			// 每次调用 register 一个 T
			public void Register<T>()
				where T : struct, IComponentData
			{
				var dataTypeId = _manager.GetDataTypeId<T>();
#if UNITY_EDITOR
				Debug.Assert(!_withInDataTypeIdList.Contains(dataTypeId));
#endif
				_withInDataTypeIdList.Add(dataTypeId);

				_needRefreshBeforeNextIterate = true;

				// 监听 manager 中的 type 那一列的变化
				_manager._dataTable.AddListenerToChanged(dataTypeId, TurnOnNeedRefreshBeforeNextIterate);
			}

			// 打开标记 RefreshBeforeNextIterate
			private void TurnOnNeedRefreshBeforeNextIterate()
			{
				_needRefreshBeforeNextIterate = true;
			}

			private void IterateAllRow(Action<int, int[]> action)
			{
				// 查看是否需要 Refresh
				if (_needRefreshBeforeNextIterate)
				{
					// 下此不刷新了，除非开关再次亮起。
					_needRefreshBeforeNextIterate = false;

					// 遍历所有，找到 valid 的 row，顺便更新 _entityIdList，触发 action。
					_entityIdList.Clear();
					
					_manager._indexTable.ForEachRow((entityId, indexArr) =>
					{
						bool rowValid = CheckRowValid(indexArr, _withInDataTypeIdList);
						if (rowValid)
						{
							_entityIdList.Add(entityId);
							action.Invoke(entityId, indexArr);
						}
					});
				}
				else
				{
					// 不需要刷新，直接使用 _entityIdList 进行遍历，触发 action。
					for (var i = 0; i < _entityIdList.Count; i++)
					{
						int entityId = _entityIdList[i];

						var row = _manager._indexTable.GetRowIndexArr(entityId);
						action.Invoke(entityId, row);
					}
				}
			}

			public void ForEach<T>(Action<int, T> action)
				where T : struct, IComponentData
			{
				var dataTypeId = _manager.GetDataTypeId<T>();
				IterateAllRow((entityId, indexArr) =>
				{
					var elementIndex = indexArr[dataTypeId];
					T data = _manager._dataTable.GetElement<T>(dataTypeId, elementIndex);
					action.Invoke(entityId, data);
				});
			}

			public void ForEach<T, T1>(Action<int, T, T1> action)
				where T : struct, IComponentData
				where T1 : struct, IComponentData
			{
				var dataTypeId = _manager.GetDataTypeId<T>();
				var dataTypeId1 = _manager.GetDataTypeId<T1>();
				IterateAllRow((entityId, indexArr) =>
				{
					var elementIndex = indexArr[dataTypeId];
					var elementIndex1 = indexArr[dataTypeId1];
					T data = _manager._dataTable.GetElement<T>(dataTypeId, elementIndex);
					T1 data1 = _manager._dataTable.GetElement<T1>(dataTypeId, elementIndex1);
					action.Invoke(entityId, data, data1);
				});
			}

			// 三个泛型版本
			public void ForEach<T, T1, T2>(Action<int, T, T1, T2> action)
				where T : struct, IComponentData
				where T1 : struct, IComponentData
				where T2 : struct, IComponentData
			{
				var dataTypeId = _manager.GetDataTypeId<T>();
				var dataTypeId1 = _manager.GetDataTypeId<T1>();
				var dataTypeId2 = _manager.GetDataTypeId<T2>();
				IterateAllRow((entityId, indexArr) =>
				{
					var elementIndex = indexArr[dataTypeId];
					var elementIndex1 = indexArr[dataTypeId1];
					var elementIndex2 = indexArr[dataTypeId2];
					T data = _manager._dataTable.GetElement<T>(dataTypeId, elementIndex);
					T1 data1 = _manager._dataTable.GetElement<T1>(dataTypeId1, elementIndex1);
					T2 data2 = _manager._dataTable.GetElement<T2>(dataTypeId2, elementIndex2);
					action.Invoke(entityId, data, data1, data2);
				});
			}

			// 四个泛型版本
			public void ForEach<T, T1, T2, T3>(Action<int, T, T1, T2, T3> action)
				where T : struct, IComponentData
				where T1 : struct, IComponentData
				where T2 : struct, IComponentData
				where T3 : struct, IComponentData
			{
				var dataTypeId = _manager.GetDataTypeId<T>();
				var dataTypeId1 = _manager.GetDataTypeId<T1>();
				var dataTypeId2 = _manager.GetDataTypeId<T2>();
				var dataTypeId3 = _manager.GetDataTypeId<T3>();
				IterateAllRow((entityId, indexArr) =>
				{
					var elementIndex = indexArr[dataTypeId];
					var elementIndex1 = indexArr[dataTypeId1];
					var elementIndex2 = indexArr[dataTypeId2];
					var elementIndex3 = indexArr[dataTypeId3];
					T data = _manager._dataTable.GetElement<T>(dataTypeId, elementIndex);
					T1 data1 = _manager._dataTable.GetElement<T1>(dataTypeId1, elementIndex1);
					T2 data2 = _manager._dataTable.GetElement<T2>(dataTypeId2, elementIndex2);
					T3 data3 = _manager._dataTable.GetElement<T3>(dataTypeId3, elementIndex3);
					action.Invoke(entityId, data, data1, data2, data3);
				});
			}

			// 查看某个 row，指定的 type 类型，是不是全是 valid。
			private static bool CheckRowValid(int[] indexArr, List<int> withInDataTypeIdList)
			{
				// 只有包含所有 type 的，才是 right row。
				bool result = true;
				for (var i = 0; i < withInDataTypeIdList.Count; i++)
				{
					var withInDataTypeId = withInDataTypeIdList[i];
					var element = indexArr[withInDataTypeId];
					if (element == -1)
					{
						// 这一行，不包含这个 type 的 data。
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
		protected World World => _world;

		private EntityManager _entityManager;
		
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

		// public BridgeManager BridgeManager => _bridgeManager;

		public World()
		{
			_entityManager = new EntityManager();
			_systemList = new List<System>();
			_bridgeManager = new BridgeManager();
		}

		public void RegisterComponentData<T>()
			where T : struct, IComponentData
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
			// return _entityManager.HasEntity(entityId);
			throw new NotImplementedException();
		}

		public void AddComponent<T>(int entityId, T data = default)
			where T : struct, IComponentData
		{
			_entityManager.AddComponent(entityId, data);
		}

		[Obsolete("暂时设定不允许对 world 进行直接的 get/set data，外部需要和 world 沟通应当使用 bridge")]
		public T GetComponentData<T>(int entityId)
			where T : struct, IComponentData
		{
			return _entityManager.GetComponentData<T>(entityId);
		}

		[Obsolete("暂时设定不允许对 world 直接 get/set data，外部需要和 world 沟通应当使用 bridge")]
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