using UnityEngine;

namespace Frame.Core.UI
{
public class LockMouse : MonoBehaviour
{
    [SerializeField] private bool m_enableLock = true;

    public void LockedMouse()
    {
        if (m_enableLock)
            Cursor.lockState = CursorLockMode.Locked;
    }

    public void FreeMouse()
    {
        Cursor.lockState = CursorLockMode.None;
    }

    private void Update()
    {
        if (Input.GetKeyUp(KeyCode.Escape))
        {
            FreeMouse();
        }
    }
}
}
