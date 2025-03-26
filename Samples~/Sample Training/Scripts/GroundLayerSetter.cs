using UnityEngine;

public class GroundLayerSetter : MonoBehaviour
{
    private void Awake()
    {
        gameObject.layer = LayerMask.NameToLayer("Ground");
    }
}
