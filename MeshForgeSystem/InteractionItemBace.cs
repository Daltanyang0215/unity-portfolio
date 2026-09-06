using DG.Tweening;
using UnityEngine;

public class InteractionItemBace : MonoBehaviour
{
    // ===== 상태 관리 =====
    protected EnvironmentState _environment = EnvironmentState.Air;
    [SerializeField] protected Collider _collider;
    [SerializeField] protected Rigidbody _rb;
    private bool _isKinematic;

    public void SetParent(Vector3 pos, Vector3 rotate, float duration = 0.25f)
    {
        transform.SetParent(null);
        transform.DOMove(pos, duration);
        transform.DORotate(rotate, duration);
    }

    public void SetParent(Transform partent, float duration = 0.25f)
    {
        transform.SetParent(partent);
        transform.DOLocalMove(Vector3.zero, duration);
        transform.DOLocalRotateQuaternion(Quaternion.identity, duration);
    }

    public void SetColliderEnable(bool enable)
    {
        _collider.enabled = enable;

        if (_rb)
        {
            if (enable)
            {
                _rb.isKinematic = _isKinematic;
            }
            else
            {
                _isKinematic = _rb.isKinematic;
                _rb.isKinematic = true;
            }
        }

    }

    // ===== 환경 설정 =====
    public void SetEnvironment(EnvironmentState state)
    {
        _environment = state;
    }
}
