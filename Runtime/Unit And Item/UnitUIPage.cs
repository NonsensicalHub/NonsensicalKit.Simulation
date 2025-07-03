using NonsensicalKit.Simulation;
using NonsensicalKit.UGUI;

public abstract class UnitUIPage : NonsensicalUI
{
    protected override void Awake()
    {
        base.Awake();
        Subscribe("HideUnitUI",OnClosePage);
    }

    private void OnClosePage()
    {
        CloseSelf();
    }

    public abstract void ShowUnitUI(UnitBase unit);
}
