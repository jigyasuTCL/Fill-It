using UnityEngine;

namespace Game.SpinWheel
{
    /// <summary>
    /// Fires the pointer tick exactly once each time this collider physically passes another
    /// collider - e.g. one small trigger collider per wheel segment sweeping past a matching
    /// collider on the pointer as the wheel spins. OnTriggerEnter2D already only raises once per
    /// overlap (not repeatedly while the colliders stay inside each other), so no extra
    /// debouncing/"already ticked" state is needed here - just wire it up and it fires once per pass.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public sealed class SpinWheelPointerTickTrigger : MonoBehaviour
    {
        [SerializeField] private SpinWheelUIController controller;

        // Lets SpinWheelUIController wire all of its child triggers from one list in its own
        // Inspector instead of having to assign the controller reference separately on every
        // trigger GameObject.
        public void SetController(SpinWheelUIController value)
        {
            controller = value;
        }

        private void Reset()
        {
            Collider2D col = GetComponent<Collider2D>();
            if (col != null)
            {
                col.isTrigger = true;
            }
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (controller == null)
            {
                Debug.LogWarning(
                    $"[SpinWheel] {name} triggered but has no SpinWheelUIController assigned - " +
                    "add this object to the controller's Pointer Tick Triggers list.",
                    this);

                return;
            }

            controller.NotifyPointerTick();
        }
    }
}
