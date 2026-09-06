using UnityEngine;
using UnityEngine.InputSystem;

public class AnvilInteraction : ForgingInteractionBase
{
    public bool isDetail = false; // 정밀 가공
    public Mesh targetMesh;

    private void Update()
    {
        if(_currentItem == false) return;

        if (_currentItem is IngotMeshItem ingot)
        {
            if (Keyboard.current.rKey.wasPressedThisFrame)
            {
                ingot.RotateIngot(Vector3.up);
            }
            else if (Keyboard.current.qKey.wasPressedThisFrame)
            {
                ingot.RotateIngot(Vector3.forward);
            }
        }
    }

    public override void SetForgingItem(InteractionItemBace item)
    {
        base.SetForgingItem(item);

        if (item is IngotMeshItem ingot)
        {
            ingot.SetTargetMesh(targetMesh);
        }
    }

    public override InteractionItemBace GetForgingItem()
    {
        InteractionItemBace item = base.GetForgingItem();

        if (item is IngotMeshItem ingot)
        {
            ingot.isShapeDefined = false;
            ingot.targetMesh = null;
        }

        return item;
    }

    #region playerInupt
    protected override void OnMoveMouse(InputAction.CallbackContext value)
    {
        Vector3 _moveVec = value.ReadValue<Vector2>();
        _moveVec = Quaternion.Euler(0, _camera.transform.eulerAngles.y, 0) * new Vector3(_moveVec.x, 0, _moveVec.y);

        _currentItem.transform.position += 0.01f * _moveVec.normalized;
    }

    protected override void OnMoveLeftClick(InputAction.CallbackContext value)
    {
        if (value.performed)
        {
            if (_currentItem is IngotMeshItem ingot)
            {
                Physics.Raycast(_camera.transform.position, _camera.transform.forward, out RaycastHit hit, 10);
                ingot.ApplyHit(hit.point, 10);
            }
        }
    }

    protected override void OnMoveRightClick(InputAction.CallbackContext value)
    {
        if (value.performed)
        {
            if (_currentItem is IngotMeshItem ingot)
            {
                isDetail = !isDetail;
                ingot.isShapeDefined = isDetail;
            }
        }
    }

    #endregion
}
