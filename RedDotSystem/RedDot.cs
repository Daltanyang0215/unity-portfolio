using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RedDot
{
    public class RedDot : MonoBehaviour
    {
        public bool IsActive => _isActive;
        public long Id => _redDotId;

        // 인스펙터 입력용 (미리 생상된 고정된 UI에 사용)
        [SerializeField] private long _redDotId;

        private bool _isActive;
        private bool _isLocked;
        private bool _addedCallback = false;
        
        protected void Start()
        {
            if (_redDotId == 0)
            {
                ApplyRedDotState(false);
                return;
            }

            AddCallback();
            UpdateRedDot();
        }

        protected void OnDestroy()
        {
            if (RedDotManager.Instance)
            {
                _redDotId = 0;
                RedDotManager.Instance.OnChangedRedDot -= OnChangeRedDotStat;
            }
        }

        protected void AddCallback()
        {
            if (!_addedCallback)
            {
                _addedCallback = true;
                RedDotManager.Instance.OnChangedRedDot += OnChangeRedDotStat;
            }
        }

        public void UpdateRedDot()
        {
            RedDotData cur = RedDotManager.Instance.GetRedDotData(Id);
            if (cur != null)
            {
                ApplyRedDotState(cur.isActive);
            }
            else
            {
                ApplyRedDotState(false);
            }
        }

        public void OnChangeRedDotStat(long id, bool isActive)
        {
            if (id == Id)
            {
                ApplyRedDotState(isActive);
            }
        }

        private void ApplyRedDotState(bool isActive)
        {
            _isActive = isActive;

            bool shouldShow = _isActive && !_isLocked;

            if (this.gameObject.activeSelf != shouldShow)
                this.gameObject.SetActive(shouldShow);
        }
        
        // 동적 생상한 UI 혹은 재사용 UI 에 사용
        public void SetRedDotId(long id)
        {
            _redDotId = id;
            AddCallback();
            UpdateRedDot();
        }
       
        // 미개방 컨텐츠에 레드닷이 활성화 되는 것을 방지
        public void SetLock(bool isLock)
        {
            _isLocked = isLock;

            ApplyRedDotState(_isActive);
        }
    }
}