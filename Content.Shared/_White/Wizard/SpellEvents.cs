using Content.Shared.Actions;
using Content.Shared.Atmos;
using Content.Shared.Polymorph;
using Content.Shared.Tag;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;


namespace Content.Shared._White.Wizard;


public sealed partial class ChargeMagicEvent : InstantActionEvent
{
    [DataField]
    public ProtoId<TagPrototype> WandTag = "WizardWand";

    [DataField]
    public float WandChargeRate = 1000f;

    [DataField]
    public float MinWandDegradeCharge = 1000f;

    [DataField]
    public float WandDegradePercentagePerCharge = 0.5f;

    [DataField]
    public List<ProtoId<TagPrototype>> RechargeTags = new()
    {
        "WizardWand",
        "WizardStaff",
    };
}

public sealed partial class PolymorphSpellEvent : InstantActionEvent
{
    [DataField]
    public ProtoId<PolymorphPrototype>? ProtoId;

    [DataField]
    public bool MakeWizard = true;

    [DataField]
    public SoundSpecifier? Sound;

    [DataField]
    public bool LoadActions;
}

[DataDefinition]
public sealed partial class DimensionShiftEvent : EntityEventArgs
{
    [DataField]
    public SoundSpecifier? Sound = new SoundPathSpecifier("/Audio/_Goobstation/Wizard/ghost.ogg");

    [DataField]
    public float OxygenMoles = 10f;

    [DataField]
    public float NitrogenMoles = 10f;

    [DataField]
    public float CarbonDioxideMoles = 10f;

    [DataField]
    public float Temperature = Atmospherics.T0C - 5f;

    [DataField]
    public string? Parallax = "Wizard";
}
