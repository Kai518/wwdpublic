using Robust.Shared.Serialization;


namespace Content.Shared._White.Contractors;

[Serializable, NetSerializable]
public enum ContractsListUiKey : byte
{
    Key
}

[Serializable, NetSerializable]
public sealed class ContractsListState : BoundUserInterfaceState
{

    public ContractsListState()
    {

    }
}
