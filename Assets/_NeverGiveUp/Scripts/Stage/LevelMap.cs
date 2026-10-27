using UnityEngine;

namespace NeverGiveUp
{
    // Authoring references only. Gameplay continues to use StageController.
    public sealed class LevelMap : MonoBehaviour
    {
        public StageController stage;
        public CharacterMotor actor;
        public Transform start;
        public StageWaypoint goal;
    }
}
