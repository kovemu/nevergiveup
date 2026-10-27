using UnityEngine;

namespace NeverGiveUp
{
    public sealed class LevelSection : MonoBehaviour
    {
        [Tooltip("An authoring note, not a claim that the route is solvable.")]
        public bool requiresPlayerBox;
        [TextArea] public string designerNote;
    }
}
