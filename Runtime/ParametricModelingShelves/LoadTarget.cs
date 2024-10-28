using System;
using System.Collections;
using System.Collections.Generic;
using NonsensicalKit.Core;
using UnityEngine;

public class LoadTarget : MonoBehaviour
{
    public Vector3Int Pos;
    private void OnMouseUpAsButton()
    {
        IOCC.Publish("ClickLoad",Pos);
    }
}
