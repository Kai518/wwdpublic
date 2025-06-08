using Robust.Client.UserInterface;
using SDL3;


namespace Content.Client._White.Contractors;


public sealed class ContractsListBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private ContractsList? _window;

    public ContractsListBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey) { }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<ContractsList>();
        _window.AddContract();
        _window.AddContract();
        _window.AddContract();
        _window.AddContract();
        _window.AddContract();
        _window.AddContract();
    }
}
