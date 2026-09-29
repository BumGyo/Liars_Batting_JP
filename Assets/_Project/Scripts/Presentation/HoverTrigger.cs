using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace LiarsBatting.Presentation
{
    // uGUI's pointer-enter/exit events only fire on a MonoBehaviour implementing
    // these interfaces, so a plain C# view class (like everything else in this
    // codebase) can't listen for them directly -- this is the minimal adapter.
    public class HoverTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public Action OnEnter;
        public Action OnExit;

        public void OnPointerEnter(PointerEventData eventData) => OnEnter?.Invoke();
        public void OnPointerExit(PointerEventData eventData) => OnExit?.Invoke();
    }
}
