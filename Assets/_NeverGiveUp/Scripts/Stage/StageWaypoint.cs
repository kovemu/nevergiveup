using UnityEngine;

namespace NeverGiveUp
{
    public sealed class StageWaypoint : MonoBehaviour
    {
        [Tooltip("Feet position. A launch point jumps toward the next waypoint.")]
        [SerializeField] private bool jumpToNext;
        [SerializeField, Min(0.1f)] private float arrivalRadius = 0.3f;
        public bool JumpToNext => jumpToNext;
        public float ArrivalRadius => arrivalRadius;
        public Vector3 Position => transform.position;
        public void Configure(bool launch)
        { jumpToNext = launch; arrivalRadius = launch ? 0.1f : 0.3f; }

        private void OnDrawGizmos()
        {
            Gizmos.color = jumpToNext ? new Color(1f, 0.65f, 0.1f) : Color.cyan;
            Gizmos.DrawWireSphere(transform.position, arrivalRadius);
            Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 1.5f);
        }
    }
}
