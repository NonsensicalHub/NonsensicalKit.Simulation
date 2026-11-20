using HighlightPlus;
using NonsensicalKit.Tools.InteractionTool;
using UnityEngine;

public class InteractableHighlightEffect : MonoBehaviour,IInteractable3DHighlight
{
    [SerializeField] private HighlightEffect m_highlightComponent;

    public void ApplyInteractable3DPhase(Interactable3DVisualPhase phase)
    {
        m_highlightComponent.highlighted = phase!=Interactable3DVisualPhase.Normal;
    }
}
