using DG.Tweening;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

public class ForgingInteractionBase : MonoBehaviour
{
    [SerializeField] protected CinemachineCamera _camera;
    [SerializeField] private Transform _playerPos;
    [SerializeField] private Transform _itemRoot;
    [SerializeField] private EnvironmentState _environmentState;

    protected InteractionItemBace _currentItem;

    public virtual bool CheckForgingItem() => _currentItem;

    public virtual void SetForgingItem(InteractionItemBace item) // 나중에 단조 품에 대한 포관적인 클래스로 변경 필요
    {
        item.SetParent(_itemRoot.position, _itemRoot.eulerAngles);
        item.SetEnvironment(_environmentState);
        _currentItem = item;
    }

    public virtual InteractionItemBace GetForgingItem()
    {
        if (_currentItem == null) return null;

        InteractionItemBace item = _currentItem;
        _currentItem = null;

        item.SetEnvironment(EnvironmentState.Air);

        return item;
    }

    public virtual void OnInteraction()
    {
        MainGameManager.intance.OnEnterForgeInteraction(true);
        OnBeginInteraction();

        if (_camera) _camera.enabled = true;
        DOVirtual.DelayedCall(0.5f, () =>
        {
            MainGameManager.intance.playerController.transform.SetPositionAndRotation(_playerPos.position, _playerPos.rotation);
        });
    }

    protected virtual void OnBeginInteraction()
    {
        MainGameManager.intance.PlayerInputActionMap.FindAction("Look").performed += OnMoveMouse;
        MainGameManager.intance.PlayerInputActionMap.FindAction("Attack").performed += OnMoveLeftClick;
        MainGameManager.intance.PlayerInputActionMap.FindAction("Action").performed += OnMoveRightClick;
    }

    public virtual void OnExit()
    {
        OnEndnteraction();

        if (_camera) _camera.enabled = false;
        DOVirtual.DelayedCall(0.5f, () =>
        {
            MainGameManager.intance.OnEnterForgeInteraction(false);
        });
    }

    protected virtual void OnEndnteraction()
    {
        MainGameManager.intance.PlayerInputActionMap.FindAction("Look").performed -= OnMoveMouse;
        MainGameManager.intance.PlayerInputActionMap.FindAction("Attack").performed -= OnMoveLeftClick;
        MainGameManager.intance.PlayerInputActionMap.FindAction("Action").performed -= OnMoveRightClick;
    }

    #region playerInupt
    protected virtual void OnMoveMouse(InputAction.CallbackContext value)
    {
    }

    protected virtual void OnMoveLeftClick(InputAction.CallbackContext value)
    {
    }

    protected virtual void OnMoveRightClick(InputAction.CallbackContext value)
    {
    }

    #endregion
}
