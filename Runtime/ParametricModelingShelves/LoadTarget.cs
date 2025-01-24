using NonsensicalKit.Core;
using UnityEngine;
using UnityEngine.Serialization;

public class LoadTarget : MonoBehaviour
{
    [FormerlySerializedAs("Pos")] public Vector3Int m_Pos;

    private void OnMouseUpAsButton()
    {
        IOCC.Publish("ClickLoad", m_Pos);
    }
}
