using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RedDot
{
    // 메타데이터 별도 생성
    [Serializable]
    public class RedDotMetaData : MetaData
    {
        public long parentId;
        public long unlockId;
    }

    public class RedDotData
    {
        public long id = 0;
        public long parentId = -1;
        public List<long> childrenIdList = new List<long>();
        public long unlockId = 0;
        public bool isActive = false;

        public RedDotData()
        {
        }
        
        public RedDotData(long id, long unlockId)
        {
            this.id = id;
            this.unlockId = unlockId;
        }

        public void SetParent(long parent) { this.parentId = parent; }

        public void AddChild(long addId)
        {
            if (childrenIdList.Contains(addId))
            {
                Debug.Log($"동일한 ID의 레드닷이 자식으로 등록되어있습니다. \n 현재 : {id} / 추가하려는 자식 : {addId}");
                return;
            }
            childrenIdList.Add(addId);
        }

        public void RemoveChild(long removeId)
        {
            if (!childrenIdList.Contains(removeId))
            {
                Debug.Log($"보유하지 않는 자식 입니다. \n 현재 : {id} / 제거하려는 자식 : {removeId}");
                return;
            }
            childrenIdList.Remove(removeId);
        }

        public void SetActive(bool active)
        {
            isActive = active;
        }
    }

    public class RedDotManager : MonoBehaviourSingleton<RedDotManager>
    {
        private Dictionary<long, RedDotData> _redDotDataTable = new Dictionary<long, RedDotData>();
        private Dictionary<long, List<long>> _waitParentRedDotsTable = new Dictionary<long, List<long>>();
        public event Action<long, bool> OnChangedRedDot;

        private Dictionary<long, List<long>> _waitUnlockContentRedDotTable = new Dictionary<long, List<long>>();
        private Dictionary<long, bool> _waitUnlockRedDotActive = new Dictionary<long, bool>();
        private HashSet<long> _waitUnlockContentRedDotList = new HashSet<long>();

        public void Init()
        {
            List<RedDotMetaData> redDotList = new List<RedDotMetaData>();

            // 스펙데이터 매니져등 미리 구성한 레드닷 리스트 추가
            redDotList.AddRange(SpecDataManager.Instance.ChartData.RedDot);
            // 아아템의 아이디나 컨텐츠등을 기반한 반복적인 레드닷 같은 경우에 스펙데이터의 용량과 데이터 작업량을 줄이기위해 능동작으로 생성
            // 아아템의 종류나 컨텐츠에 따라 추가로 호출
            redDotList.AddRange(Create__RedDotList());
            
            _redDotDataTable.Clear();
            foreach (var item in redDotList) // 레드닷 전체 생성
            {
                if (!_redDotDataTable.TryAdd(item.rid, new RedDotData(item.rid, item.unlockId)))
                {
                    Debug.Log($"중복된 레드닷 입니다. {item.rid}");
                }
            }
            foreach (var item in redDotList) // 레드닷 부모 연결
            {
                ConnectRedDot(item.rid, item.parentId);
            }

            // 게임 시작등 레드닷의 처음 상태를 확인
            // 컨텐츠에 따라 추가로 호출
            Check__RedDot();
 
            // 컨텐츠별 레드닷 갱신 이벤트 추가
            // 컨텐츠별로 추가로 호출
            DataManager.Instance.LevelUpDelegate += OnLevelUp;
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();

            if (DataManager.Instance)
            {
                DataManager.Instance.LevelUpDelegate -= OnLevelUp;
            }
        }

        public void ConnectRedDot(long id, long newParent)
        {
            if (newParent <= 0) return; // 0 은 노드 초기 값 , -1 은 부모가 없는 루트레드닷
            if (!_redDotDataTable.ContainsKey(id))
            {
                //Debug.Log($"존재하지 않는 레드닷 입니다. \n 현재 :{id}");
                return;
            }
            DisconnectRedDot(id); // 혹시나 있을 연결 해재
            _redDotDataTable[id].SetParent(newParent);

            if (_redDotDataTable.TryGetValue(newParent, out var parent))
            {
                parent.AddChild(id);
            }
            else
            {
                Debug.Log($"존재하지 않는 부모 입니다. \n 현재 :{id} / 부모 : {newParent}");
                if (_waitParentRedDotsTable.ContainsKey(newParent))
                {
                    if (!_waitParentRedDotsTable[newParent].Contains(id))
                    {
                        _waitParentRedDotsTable[newParent].Add(id);
                    }
                }
                else
                {
                    _waitParentRedDotsTable.Add(newParent, new List<long> { id });
                }
            }
        }

        public void DisconnectRedDot(long id)
        {
            if (!_redDotDataTable.TryGetValue(id, out var redDot))
            {
                if (id > 0) // 0 => 초기값, -1 => null
                    Debug.Log($"존재하지 않는 레드닷 입니다. \n 현재 :{id}");
                return;
            }

            if (!_redDotDataTable.TryGetValue(redDot.parentId, out var parent))
            {
                if (redDot.parentId > 0) // 0 => 초기값, -1 => null
                    Debug.Log($"존재하지 않는 부모 레드닷 입니다. \n 부모 :{redDot.parentId}");
                return;
            }

            redDot.SetParent(-1);
            parent.RemoveChild(id);
        }

        /// 레드닷 활성화. 외부 혹은 이벤트에 따라 호출
        public void SetActiveRedDot(long id, bool isActive, bool needCheckUnlock = true, bool forced = false)
        {
            if (!_redDotDataTable.TryGetValue(id, out var redDot))
            {
                if (id != -1) // -1 => null
                    Debug.Log($"존재하지 않는 레드닷 입니다. \n 현재 :{id}");
                return;
            }

            if (needCheckUnlock)
            {
                if (CheckRedDotLock(id))
                {
                    //Debug.Log($"락이 걸렸거나 락이 걸린 부모가 존재 합니다 \n 현재 :{id}");
                    _waitUnlockRedDotActive.TryAdd(id, isActive); // 언락 될 때 활용하기위해 데이터 저장
                    return;
                }
            }

            ApplyParentRedDot(id, isActive);
        }

        // 내부 전파용 (부모를 찾아 순차적으로 전파)
        private void ApplyParentRedDot(long id, bool isActive)
        {
            if (!_redDotDataTable.TryGetValue(id, out var redDot))
            {
                if (id != -1) // -1 => Root
                {
                    Debug.Log($"존재하지 않는 레드닷 입니다. \n 현재 :{id}");
                }

                return;
            }

            if (redDot.isActive == isActive)
            {
                return;// 변화 없음으로 리턴
            }

            if (redDot.childrenIdList.Count != 0)
            {
                isActive = false;

                foreach (var childrenId in redDot.childrenIdList)
                {
                    if (_redDotDataTable.TryGetValue(childrenId, out var child))
                    {
                        if (child.isActive)
                        {
                            isActive = true;
                            break;
                        }
                    }
                }
            }

            redDot.SetActive(isActive);
            OnChangedRedDot?.Invoke(id, isActive);

            ApplyParentRedDot(redDot.parentId, isActive);
        }

        public bool HasRedDot(long id)
        {
            return _redDotDataTable.ContainsKey(id);
        }

        public RedDotData GetRedDotData(long id)
        {
            _redDotDataTable.TryGetValue(id, out var redDot);
            return redDot;
        }

        public void AddRedDot(long id, long parent = -1, long unlockId = 0)
        {
            if (!_redDotDataTable.TryAdd(id, new RedDotData(id, unlockId)))
            {
                Debug.Log($"중복된 레드닷 입니다. {id}");
            }
            ConnectRedDot(id, parent);

            // 기존에 대기하고 있던 자식이 있는지 확인 및 추가
            if (_waitParentRedDotsTable.TryGetValue(id, out var children))
            {
                foreach (var child in children)
                {
                    GetRedDotData(id).AddChild(child);
                }
                _waitParentRedDotsTable.Remove(id);

                // 자식이 있다면 레드닷 업데이트
                SetActiveRedDot(id, false);
            }
        }

        public void RemoveRedDot(long id)
        {
            DisconnectRedDot(id);

            if (!_redDotDataTable.ContainsKey(id))
            {
                return;
            }
            _redDotDataTable.Remove(id);
        }

        /// <summary>
        /// 자신 또는 부모 레드닷 중 락이 결려 있다면 true
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public bool CheckRedDotLock(long id)
        {
            if(_waitUnlockContentRedDotList.Contains(id)) 
                return true;

            long checkRedDotId = id;
            int tryCount = 0;
            while (tryCount < 20)
            {
                RedDotData data = GetRedDotData(checkRedDotId);
                if (data == null) break;

                if (UnlockContentManager.Instance.IsLocked(data.unlockId))
                {
                    // 언락시 업데이트 해줄 리스트 테이블 작성
                    _waitUnlockContentRedDotList.Add(data.unlockId);
                    _waitUnlockContentRedDotTable.TryAdd(data.unlockId, new List<long>());
                    _waitUnlockContentRedDotTable[data.unlockId].Add(id);

                    return true;
                }

                if (data.parentId > 0)
                {
                    checkRedDotId = data.parentId;
                }
                else
                {
                    break;
                }

                tryCount++;
            }

            return false;
        }

        /// <summary>
        /// 특정 컨텐츠가 언락 되었을때 기존 락 상태로 인해 업데이트 되지 못한 레드닷을 업데이트
        /// </summary>
        /// <param name="unlockMetaData"></param>
        public void OnUnlockContent(SqliteUnlockData unlockMetaData)
        {
            if (_waitUnlockContentRedDotTable.TryGetValue(unlockMetaData.Rid, out List<long> redDotList))
            {
                foreach (long id in redDotList)
                {
                    bool isActive = _waitUnlockRedDotActive[id];
                    ApplyParentRedDot(id, isActive);
                    _waitUnlockRedDotActive.Remove(id);
                }
                _waitUnlockContentRedDotList.Remove(unlockMetaData.Rid);
                _waitUnlockContentRedDotTable.Remove(unlockMetaData.Rid);
            }
        }

        /////////////////////// 레드닷 생성 //////////////////////////////////////////////////////////////////
        // 컨텐츠별 반복적인 레드닷 생성
        private List<RedDotMetaData> Create__RedDotList()
        {
            List<RedDotMetaData> result = new List<RedDotMetaData>();
            
            var metaDataList = SpecDataManager.Instance.ChartData.__;
            // 생성 코드
            foreach (var metaData in metaDataList)
            {
                RedDotMetaData addMetaData = new RedDotMetaData();
                addMetaData.parentId = metaData.parentId;
                addMetaData.rid = addMetaData.parentId + metaData.rid;
                result.Add(addMetaData);
            }

            return result;
        }

        ////////////////////  레드닷 체크  ///////////////////////////////////////////////////////////////////////////

        // 초기 상태 확인
        private void Check__RedDot()
        {
            // 각 레드뎃 컨텐츠에 필요한 데이터를 바탕으로 레드닷 적용
        }

        //////////  레드닷 이벤트  ///////////////////////////////////////////////////////////////////////

        private void OnLevelUp(int lv)
        {
        }
    }
}
