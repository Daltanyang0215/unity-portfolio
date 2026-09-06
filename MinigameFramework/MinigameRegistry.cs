using System.Collections.Generic;
using UnityEngine;

namespace NightView
{
    /// <summary>
    /// MinigameDataSO 정적 조회 및 인스턴스 생성 전용.
    /// 런타임 상태 보유 금지.
    /// </summary>
    public class MinigameRegistry : SingletonBase<MinigameRegistry>
    {
        #region SerializeField

        [SerializeField] private List<MinigameDataSO> _dataSet;

        #endregion

        #region private 필드

        private Dictionary<MinigameType, MinigameDataSO> _dataMap;

        #endregion

        #region Unity 이벤트

        protected override void Awake()
        {
            base.Awake();
            Init();
        }

        #endregion

        #region Public 메서드

        /// <summary>_dataSet 순회해서 _dataMap 구성. null 항목은 에러 로그.</summary>
        public void Init()
        {
            _dataMap = new Dictionary<MinigameType, MinigameDataSO>();
            for (int i = 0; i < _dataSet.Count; i++)
            {
                MinigameDataSO data = _dataSet[i];
                if (data == null)
                {
                    Debug.LogError($"[MinigameRegistry] _dataSet[{i}]가 null입니다.");
                    continue;
                }
                _dataMap.TryAdd(data.Type, data);
            }
        }

        /// <summary>타입으로 데이터 조회. 없으면 에러 로그 후 null 반환.</summary>
        public MinigameDataSO GetData(MinigameType type)
        {
            if (_dataMap.TryGetValue(type, out MinigameDataSO data))
                return data;

            Debug.LogError($"[MinigameRegistry] MinigameType.{type} 데이터를 찾을 수 없습니다.");
            return null;
        }

        /// <summary>프리팹 인스턴스화 후 Initialize 호출. 실패 시 null 반환.</summary>
        public BaseMinigame Instantiate(MinigameType type, int stage, Transform parent, int index)
        {
            MinigameDataSO data = GetData(type);
            if (data == null) return null;
            if (string.IsNullOrEmpty(data.PrefabName))
            {
                Debug.LogError($"[MinigameRegistry] MinigameType.{type}의 Prefab이 지정되어있지 않습니다.");
                return null;
            }

            BaseMinigame miniGame = PoolManager.Instance.Get(data.PrefabName, parent.position, parent.rotation, parent) as BaseMinigame;
            if (miniGame == null)
            {
                Debug.LogError($"[MinigameRegistry] {data.PrefabName}에 BaseMinigame 컴포넌트가 없습니다.");
                return null;
            }

            miniGame.Initialize(data, stage, index);
            return miniGame;
        }

        #endregion
    }
}
