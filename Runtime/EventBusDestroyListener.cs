using UnityEngine;

namespace ShrinkEventBus
{
    [DisallowMultipleComponent]
    public class EventBusDestroyListener : MonoBehaviour
    {
        public object Target { get; set; }

        private void OnDestroy()
        {
            if (Target != null)
            {
                EventBus.UnregisterInstance(Target);
            }
        }
    }
}