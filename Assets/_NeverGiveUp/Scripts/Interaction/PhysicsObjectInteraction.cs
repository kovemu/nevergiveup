using UnityEngine;

namespace NeverGiveUp
{
    public sealed class PhysicsObjectInteraction : MonoBehaviour
    {
        [SerializeField, Min(1f)] private float spring = 60f;
        [SerializeField, Min(1f)] private float damping = 14f;
        [SerializeField, Min(1f)] private float maxAcceleration = 85f;
        [SerializeField, Min(0.1f)] private float maxTargetSpeed = 8f;
        [SerializeField, Min(0.1f)] private float maxReach = 3f;
        private DraggableBody held;
        private Vector3 localAnchor;
        private Vector3 desired;
        private Vector3 target;
        public bool IsDragging => held;

        public void Grab(DraggableBody item, Vector3 point)
        {
            Release();
            held = item;
            held.Held = true;
            point.z = item.Body.position.z;
            localAnchor = item.transform.InverseTransformPoint(point);
            desired = target = point;
            held.Body.WakeUp();
        }

        public void SetTarget(Vector3 point) { point.z = 0f; desired = point; }

        private void FixedUpdate()
        {
            if (!held) return;
            Rigidbody body = held.Body;
            Vector3 anchor = held.transform.TransformPoint(localAnchor);
            Vector3 limited = anchor + Vector3.ClampMagnitude(desired - anchor, maxReach);
            target = Vector3.MoveTowards(target, limited, maxTargetSpeed * Time.fixedDeltaTime);
            Vector3 force = (target - anchor) * spring - body.GetPointVelocity(anchor) * damping;
            force = Vector3.ClampMagnitude(force - Physics.gravity, maxAcceleration);
            force.z = 0f;
            // Never set Transform/position while dragging: the solver and CCD own collisions.
            body.AddForceAtPosition(force * body.mass, anchor, ForceMode.Force);
        }

        public void Release()
        {
            if (held) held.Held = false;
            held = null;
        }
        private void OnDisable() => Release();
        private void OnApplicationFocus(bool focused) { if (!focused) Release(); }
    }
}
