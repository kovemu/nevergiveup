namespace NeverGiveUp
{
    public interface ICharacterActionState
    {
        CharacterState DisplayState { get; }
        float Preparation { get; }
        JumpMode CurrentJump { get; }
    }
}
