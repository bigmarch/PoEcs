using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PoEcs.Example
{
	public class ExamplePresenter : MonoBehaviour
	{
		public ExampleCore Core;

		public GameObject PawnPrefab;

		private GameObject _localPawn;

		private List<int> _createList;
		private List<int> _destroyList;

		private Dictionary<int, Pawn> _runningDic;

		private void Awake()
		{
			_createList = new List<int>();
			_destroyList = new List<int>();
			_runningDic = new Dictionary<int, Pawn>();
		}

		public void Present()
		{
			var brOutput = Core.World.GetBridgeData<BrWorldSnapshot>();

			// _createList.Clear();
			// _destroyList.Clear();

			List<int> inputIdList = brOutput.AllPawnInfo.Select(info => info.EntityId).ToList();
			// 差分比较
			Compare(inputIdList, _runningDic.Keys, out _createList,out _destroyList);

			// 新增
			foreach (var id in _createList)
			{
				var pawn = Instantiate(PawnPrefab).GetComponent<Pawn>();
				pawn.gameObject.name = "Pawn " + id;
				_runningDic.Add(id, pawn);
			}

			// 删除
			foreach (var id in _destroyList)
			{
				var go = _runningDic[id];
				Destroy(go);
				_runningDic.Remove(id);
			}
			
			foreach (var pawnInfo in brOutput.AllPawnInfo)
			{
				var pawn = _runningDic[pawnInfo.EntityId];
				pawn.transform.position = pawnInfo.Pos;
				pawn.transform.rotation = Quaternion.Euler(0, 0, pawnInfo.Rot);
				// 武器
				pawn.Weapon.gameObject.SetActive(pawnInfo.HoldingWeapon);
			}
		}

		private static void Compare(
			List<int> inList,
			Dictionary<int, Pawn>.KeyCollection runningDic,
			out List<int> createList,
			out List<int> destroyList)
		{
			var listOnly = inList.Except(runningDic).ToList(); // List<int> 中有，Dictionary<int> 中没有的元素
			var dictOnly = runningDic.Except(inList).ToList(); // Dictionary<int> 中有，List<int> 中没有的元素

			createList = listOnly;
			destroyList = dictOnly;
		}
	}
}