namespace Content.Shared._White.Contractors;

[RegisterComponent]
public sealed partial class ContractorObjectiveComponent : Component
{
    [DataField]
    public int TelecrystalsGranted;

    [DataField]
    public float ThreatIncrease;

    [DataField]
    public float DiscardCost;
}
