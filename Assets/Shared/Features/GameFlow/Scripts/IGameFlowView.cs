namespace Shared.GameFlow
{
    public interface IGameFlowView
    {
        void ShowMainMenu();
        void ShowGameplay();
        void ShowWin();
        void ShowFail();
        void ShowState(GameFlowState state);
    }
}
