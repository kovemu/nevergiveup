using System;
using UnityEngine;

namespace NeverGiveUp
{
    public enum JumpMode { Short, Long }

    [RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
    public sealed class CharacterMotor : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float moveSpeed = 4f;
        [SerializeField, Min(1f)] private float acceleration = 24f;
        [SerializeField, Min(1f)] private float jumpSpeed = 7.8f; // Hard upper bound retained for older scenes.
        [SerializeField, Min(0.2f)] private float shortJumpHeight = 1.55f;
        [SerializeField, Min(0.2f)] private float longJumpHeight = 3.0f;
        [SerializeField] private LayerMask groundMask = ~0;
        private readonly RaycastHit[] hits = new RaycastHit[16];
        private static readonly float[] ObstacleScanHeights = { 0.33f, 0.9f, 1.43f };
        private readonly Collider[] overlaps = new Collider[16];
        private Rigidbody body;
        private CapsuleCollider capsule;
        private float groundIgnoreUntil;
        private PhysicsMaterial standingMaterial;
        private PhysicsMaterial fallenMaterial;
        [SerializeField, Min(0.1f)] private float airAcceleration = 3f;
        public Collider Support { get; private set; }
        public Vector3 SupportPoint { get; private set; }
        public int ExternalVersion { get; private set; }
        public float LastExternalTime { get; private set; } = -100f;
        public string ExternalSource { get; private set; } = "none";
        public event Action<Collision> Contact;
        private float controlSuppressedUntil;
        public bool ControlSuppressed => Time.time < controlSuppressedUntil;
        public void SuppressControl(float seconds)
        { controlSuppressedUntil = Mathf.Max(controlSuppressedUntil, Time.time + seconds); }
        public float MaxJumpHorizontal => moveSpeed * 1.6f;

        public Rigidbody Body => body;
        public CapsuleCollider Capsule => capsule;
        public float MoveSpeed => moveSpeed;
        public float JumpSpeed => jumpSpeed;
        public float ShortJumpHeight => Mathf.Min(shortJumpHeight, longJumpHeight, MaxJumpHeight);
        public float LongJumpHeight => Mathf.Min(Mathf.Max(longJumpHeight, shortJumpHeight), MaxJumpHeight);
        private float MaxJumpHeight => Physics.gravity.y < -0.001f ?
            jumpSpeed * jumpSpeed / (-2f * Physics.gravity.y) : 0f;
        public float JumpVelocity(JumpMode mode)
        {
            float height = mode == JumpMode.Short ? ShortJumpHeight : LongJumpHeight;
            return Physics.gravity.y < -0.001f ? Mathf.Sqrt(-2f * Physics.gravity.y * height) : 0f;
        }

        public bool CanReach(Vector3 from, Vector3 to, JumpMode mode)
        {
            float g = -Physics.gravity.y;
            float vertical = JumpVelocity(mode);
            float discriminant = vertical * vertical - 2f * g * (to.y - from.y);
            if (g < 0.001f || discriminant <= 0.0001f) return false;
            float duration = (vertical + Mathf.Sqrt(discriminant)) / g;
            return Mathf.Abs(to.x - from.x) <= MaxJumpHorizontal * duration + 0.01f;
        }
        public bool CanReach(Vector3 from, Vector3 to)
        {
            return CanReach(from, to, JumpMode.Short) || CanReach(from, to, JumpMode.Long);
        }
        public float HalfHeight => capsule.height * 0.5f;
        public Vector3 Feet => body.position + Vector3.down * HalfHeight;
        public bool Grounded { get; private set; }
        public float Facing { get; private set; } = 1f;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            capsule = GetComponent<CapsuleCollider>();
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            standingMaterial = new PhysicsMaterial("Character standing (runtime)");
            PhysicsMaterial original = capsule.sharedMaterial;
            standingMaterial.dynamicFriction = original && original.dynamicFriction > 0f ? original.dynamicFriction : 0.08f;
            standingMaterial.staticFriction = original && original.staticFriction > 0f ? original.staticFriction : 0.12f;
            standingMaterial.frictionCombine = PhysicsMaterialCombine.Minimum;
            fallenMaterial = new PhysicsMaterial("Character fallen (runtime)")
            { dynamicFriction = 0.12f, staticFriction = 0.2f, frictionCombine = PhysicsMaterialCombine.Minimum };
            SetUpright(true);
        }

        public void SampleGround()
        {
            Grounded = false;
            Support = null;
            float nearest = float.PositiveInfinity;
            if (Time.time < groundIgnoreUntil) return;
            // A sphere at the lowest point also works while the capsule is lying down.
            Bounds bounds = capsule.bounds;
            Vector3 origin = new Vector3(body.position.x, bounds.min.y + 0.25f, body.position.z);
            int count = Physics.SphereCastNonAlloc(origin, 0.22f, Vector3.down, hits,
                0.16f, groundMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                if (hits[i].rigidbody == body || hits[i].normal.y < 0.45f) continue;
                if (hits[i].distance >= nearest) continue;
                nearest = hits[i].distance;
                Grounded = true;
                Support = hits[i].collider;
                SupportPoint = hits[i].point;
            }
        }

        public void Drive(float desiredVelocity, float authority = 1f)
        {
            if (ControlSuppressed) return;
            if (Mathf.Abs(desiredVelocity) > 0.05f) Facing = Mathf.Sign(desiredVelocity);
            float delta = Mathf.Clamp(desiredVelocity - body.linearVelocity.x,
                -(Grounded ? acceleration : airAcceleration) * authority * Time.fixedDeltaTime,
                (Grounded ? acceleration : airAcceleration) * authority * Time.fixedDeltaTime);
            body.AddForce(Vector3.right * delta, ForceMode.VelocityChange);
        }

        public void Stop() => Drive(0f);

        public void Jump(float horizontalSpeed, float verticalSpeed)
        {
            if (!Grounded || ControlSuppressed) return;
            horizontalSpeed = Mathf.Clamp(horizontalSpeed, -MaxJumpHorizontal, MaxJumpHorizontal);
            verticalSpeed = Mathf.Clamp(verticalSpeed, 0f, jumpSpeed);
            body.AddForce(new Vector3(horizontalSpeed - body.linearVelocity.x,
                verticalSpeed - body.linearVelocity.y, 0f), ForceMode.VelocityChange);
            groundIgnoreUntil = Time.time + 0.18f;
            Grounded = false;
        }

        // Side-on capsule-height scan; the old single low ray was blind to overhangs
        // and often mistook an ordinary sloped floor for a blocking wall.
        public bool ObstacleAhead(float direction, out RaycastHit obstacle)
        {
            obstacle = default;
            if (Mathf.Abs(direction) < 0.1f) return false;
            float closest = float.PositiveInfinity;
            for (int level = 0; level < ObstacleScanHeights.Length; level++)
            {
                Vector3 origin = Feet + Vector3.up * ObstacleScanHeights[level];
                int count = Physics.SphereCastNonAlloc(origin, 0.12f,
                    Vector3.right * Mathf.Sign(direction), hits, 0.9f,
                    groundMask, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < count; i++)
                {
                    RaycastHit hit = hits[i];
                    if (!hit.collider || hit.rigidbody == body || hit.collider == Support ||
                        hit.distance >= closest || Mathf.Abs(hit.normal.x) < 0.45f) continue;
                    closest = hit.distance;
                    obstacle = hit;
                }
            }
            return closest < float.PositiveInfinity;
        }

        public bool GroundAhead(float direction)
        {
            Vector3 origin = new Vector3(body.position.x + direction * 0.65f, capsule.bounds.min.y + 0.5f, 0f);
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, hits, 1.1f,
                groundMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (hits[i].rigidbody != body && hits[i].normal.y > 0.45f) return true;
            return false;
        }

        public void SetUpright(bool upright)
        {
            body.constraints = RigidbodyConstraints.FreezePositionZ |
                RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationY;
            if (upright) body.constraints |= RigidbodyConstraints.FreezeRotationZ;
            if (standingMaterial) capsule.sharedMaterial = upright ? standingMaterial : fallenMaterial;
        }

        public bool IsClear(Vector3 center)
        {
            float radius = capsule.radius * 0.95f;
            float segment = HalfHeight - capsule.radius;
            int count = Physics.OverlapCapsuleNonAlloc(center + Vector3.up * segment,
                center - Vector3.up * segment, radius, overlaps, groundMask,
                QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (overlaps[i].attachedRigidbody != body) return false;
            return count < overlaps.Length;
        }

        public void NotifyExternal(string source)
        {
            ExternalVersion++;
            LastExternalTime = Time.time;
            ExternalSource = source;
        }

        public void GroundDrag(float rate)
        {
            if (Grounded) body.AddForce(Vector3.right * (-body.linearVelocity.x * rate), ForceMode.Acceleration);
        }

        private void OnCollisionEnter(Collision collision) => ObserveContact(collision);
        private void OnCollisionStay(Collision collision) => ObserveContact(collision);
        private void ObserveContact(Collision collision)
        {
            DraggableBody item = collision.collider.GetComponentInParent<DraggableBody>();
            if (item && item.PlayerInfluenced)
                NotifyExternal("player-moved object");
            Contact?.Invoke(collision);
        }
        private void OnDestroy()
        {
            if (standingMaterial) Destroy(standingMaterial);
            if (fallenMaterial) Destroy(fallenMaterial);
        }

        // Technical recovery API only. Normal falling/learning never calls this method.
        public void Teleport(Vector3 center)
        {
            body.position = new Vector3(center.x, center.y, 0f);
            body.rotation = Quaternion.identity;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            SetUpright(true);
            groundIgnoreUntil = Time.time + 0.1f;
            Grounded = false;
        }
    }
}
