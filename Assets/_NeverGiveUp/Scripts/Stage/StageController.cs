using UnityEngine;

namespace NeverGiveUp
{
    public sealed class StageController : MonoBehaviour
    {
        [SerializeField] private CharacterBrain character;
        [SerializeField] private GameManager game;
        [SerializeField] private Transform safeOrigin;
        [SerializeField] private StageWaypoint[] waypoints;
        [SerializeField] private float fallThreshold = -5f;
        private CharacterMotor motor;
        private CharacterRecovery recovery;
        private float respawnTimer;
        public int Index { get; private set; }
        public bool Completed { get; private set; }
        public bool IsRespawning { get; private set; }
        public float FallThreshold => fallThreshold;
        public StageWaypoint Current => waypoints != null && Index < waypoints.Length ? waypoints[Index] : null;
        public StageWaypoint Next => waypoints != null && Index + 1 < waypoints.Length ? waypoints[Index + 1] : null;

        public void Configure(CharacterBrain actor, GameManager manager, Transform origin,
            StageWaypoint[] route, float lowerLimit)
        { character = actor; game = manager; safeOrigin = origin; waypoints = route; fallThreshold = lowerLimit; }
        private void Start()
        {
            if (!character || !safeOrigin || waypoints == null || waypoints.Length == 0 || !game)
            { Debug.LogError("Stage requires a character, manager, safe origin and route.", this); enabled = false; return; }
            motor = character.GetComponent<CharacterMotor>();
            recovery = character.GetComponent<CharacterRecovery>();
            character.Initialize(this);
        }
        public void Advance()
        {
            if (Completed || IsRespawning) return;
            Index++;
            if (Index < waypoints.Length) return;
            Completed = true;
            game.CompleteStage();
        }
        public void RequestRespawn()
        {
            if (IsRespawning) return;
            IsRespawning = true;
            respawnTimer = 0f;
            character.BeginRespawn();
        }
        private void FixedUpdate()
        {
            if (!IsRespawning || !motor) return;
            respawnTimer += Time.fixedDeltaTime;
            if (respawnTimer < 0.55f) return;
            // Search clear positions above the authored safe platform. Do not spawn inside a box.
            for (int height = 0; height < 10; height++)
            for (int side = 0; side < 3; side++)
            {
                float x = side == 0 ? 0f : (side == 1 ? 0.8f : -0.8f);
                Vector3 center = safeOrigin.position + new Vector3(x, motor.HalfHeight + 0.08f + height * 0.5f, 0f);
                if (!motor.IsClear(center)) continue;
                recovery.ResetAt(center);
                Index = 0;
                IsRespawning = false;
                character.RestartRoute();
                return;
            }
        }
    }
}
