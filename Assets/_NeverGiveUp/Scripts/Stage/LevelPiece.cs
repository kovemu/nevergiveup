using UnityEngine;

namespace NeverGiveUp
{
    public enum LevelPieceKind { Platform, Wall, Pillar, PhysicsBox, Decoration }
    public sealed class LevelPiece : MonoBehaviour
    {
        public LevelPieceKind kind;
    }
}
