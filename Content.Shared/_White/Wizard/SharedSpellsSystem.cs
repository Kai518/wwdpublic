using System.Linq;
using Content.Shared._White.Wizard.BindSoul;
using Content.Shared.Access.Components;
using Content.Shared.Actions;
using Content.Shared.Body.Systems;
using Content.Shared.Carrying;
using Content.Shared.Charges.Components;
using Content.Shared.Charges.Systems;
using Content.Shared.Clothing.Components;
using Content.Shared.Ghost;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction.Components;
using Content.Shared.Inventory;
using Content.Shared.Item;
using Content.Shared.Magic;
using Content.Shared.Magic.Components;
using Content.Shared.Mind;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.PDA;
using Content.Shared.Popups;
using Content.Shared.Roles;
using Content.Shared.Silicon.Components;
using Content.Shared.Silicons.Borgs.Components;
using Content.Shared.Tag;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Whitelist;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;


namespace Content.Shared._White.Wizard;


public abstract class SharedSpellsSystem : EntitySystem
{
    #region Dependencies

    [Dependency] protected readonly IPrototypeManager ProtoMan = default!;
    [Dependency] protected readonly IGameTiming Timing = default!;
    [Dependency] protected readonly SharedActionsSystem Actions = default!;
    [Dependency] protected readonly SharedHandsSystem Hands = default!;
    [Dependency] protected readonly TagSystem Tag = default!;
    [Dependency] protected readonly SharedAudioSystem Audio = default!;
    [Dependency] protected readonly SharedContainerSystem Container = default!;
    [Dependency] protected readonly SharedTransformSystem TransformSystem = default!;
    [Dependency] protected readonly SharedMindSystem Mind = default!;
    [Dependency] protected readonly MetaDataSystem Meta = default!;
    [Dependency] protected readonly GrammarSystem Grammar = default!;
    [Dependency] protected readonly NpcFactionSystem Faction = default!;
    [Dependency] protected readonly SharedRoleSystem Role = default!;
    [Dependency] protected readonly SharedBodySystem Body = default!;
    [Dependency] private   readonly SharedChargesSystem _charges = default!;
    [Dependency] private   readonly SharedMagicSystem _magic = default!;
    [Dependency] private   readonly SharedGunSystem _gunSystem = default!;
    [Dependency] private   readonly SharedPopupSystem _popup = default!;
    [Dependency] private   readonly InventorySystem _inventory = default!;
    [Dependency] private   readonly INetManager _net = default!;
    [Dependency] private   readonly MobStateSystem _mobState = default!;
    [Dependency] private   readonly EntityWhitelistSystem _whitelist = default!;
    [Dependency] private   readonly SharedBindSoulSystem _bindSoul = default!;

    #endregion

    public override void Initialize()
    {
        SubscribeLocalEvent<PolymorphSpellEvent>(OnPolymorph);
        SubscribeLocalEvent<ChargeMagicEvent>(OnCharge);
        SubscribeLocalEvent<BindSoulEvent>(OnBindSoul);
    }

        private void OnBindSoul(BindSoulEvent ev)
    {
        if (ev.Handled)
            return;

        if (_mobState.IsCritical(ev.Performer))
            return;

        if (!Mind.TryGetMind(ev.Performer, out var mind, out var mindComponent))
            return;

        TryComp<SoulBoundComponent>(mind, out var soulBound);

        if (Mind.IsCharacterDeadIc(mindComponent))
        {
            if (soulBound == null)
            {
                Popup(ev.Performer, "spell-fail-soul-not-bound");
                return;
            }

            if (!HasComp<PhylacteryComponent>(soulBound.Item))
            {
                Popup(ev.Performer, "spell-fail-item-destroyed");
                return;
            }

            if (!TryComp(soulBound.Item, out TransformComponent? xform) || xform.MapUid == null ||
                xform.MapUid != soulBound.MapId)
            {
                Popup(ev.Performer, "spell-fail-item-on-another-plane");
                return;
            }

            _bindSoul.Resurrect(mind, soulBound.Item.Value, mindComponent, soulBound);
            ev.Handled = true;
            return;
        }

        if (HasComp<GhostComponent>(ev.Performer))
            return;

        if (soulBound != null)
        {
            Popup(ev.Performer, "spell-fail-no-soul");
            return;
        }

        if (!_magic.PassesSpellPrerequisites(ev.Action, ev.Performer))
            return;

        if (HasComp<SiliconComponent>(ev.Performer) || HasComp<BorgChassisComponent>(ev.Performer))
        {
            Popup(ev.Performer, "spell-fail-bind-soul-silicon");
            return;
        }

        if (!Hands.TryGetActiveItem(ev.Performer, out var item))
        {
            Popup(ev.Performer, "spell-fail-no-held-entity");
            return;
        }

        if (HasComp<UnremoveableComponent>(item) || !HasComp<ItemComponent>(item))
        {
            PopupLoc(ev.Performer, Loc.GetString("spell-fail-unremoveable", ("item", item)));
            return;
        }

        if (_whitelist.IsValid(ev.Blacklist, item))
        {
            PopupLoc(ev.Performer, Loc.GetString("spell-fail-soul-item-not-suitable", ("item", item)));
            return;
        }

        BindSoul(ev, item.Value, mind, mindComponent);
        ev.Handled = true;
    }

    private void OnCharge(ChargeMagicEvent ev)
    {
        if (ev.Handled || !_magic.PassesSpellPrerequisites(ev.Action, ev.Performer))
            return;

        ev.Handled = true;

        var raysEv = new ChargeSpellRaysEffectEvent(GetNetEntity(ev.Performer));
        CreateChargeEffect(ev.Performer, raysEv);

        if (TryComp<PullerComponent>(ev.Performer, out var puller) && HasComp<PullableComponent>(puller.Pulling) &&
            RechargePerson(puller.Pulling.Value))
            return;

        if (TryComp(ev.Performer, out CarryingComponent? carrying) && RechargePerson(carrying.Carried))
            return;

        if (!TryComp(ev.Performer, out HandsComponent? hands))
            return;

        foreach (var item in Hands.EnumerateHeld(ev.Performer, hands))
        {
            if (Tag.HasAnyTag(item, ev.RechargeTags))
            {
                if (TryComp<LimitedChargesComponent>(item, out var limitedCharges))
                {
                    _charges.SetCharges(item, limitedCharges.MaxCharges, limitedCharges.MaxCharges);
                    PopupCharged(item, ev.Performer);
                    break;
                }

                if (TryComp<BasicEntityAmmoProviderComponent>(item, out var basicAmmoComp) &&
                    basicAmmoComp is { Count: not null, Capacity: not null } &&
                    basicAmmoComp.Count < basicAmmoComp.Capacity)
                {
                    _gunSystem.UpdateBasicEntityAmmoCount(item, basicAmmoComp.Capacity.Value, basicAmmoComp);
                    PopupCharged(item, ev.Performer);
                    break;
                }
            }

            if (ChargeItem(item, ev))
                break;
        }

        return;

        bool RechargePerson(EntityUid uid)
        {
            if (RechargeAllSpells(uid))
            {
                PopupCharged(uid, ev.Performer, false);
                _popup.PopupEntity(Loc.GetString("spell-charge-spells-charged-pulled"), uid, uid, PopupType.Medium);
                ev.Handled = true;
                return true;
            }

            _popup.PopupEntity(Loc.GetString("spell-charge-no-spells-to-charge-pulled"), uid, uid, PopupType.Medium);
            return false;
        }
    }

    private void OnPolymorph(PolymorphSpellEvent ev)
    {
        if (ev.Handled || !_magic.PassesSpellPrerequisites(ev.Action, ev.Performer))
            return;

        ev.Handled = Polymorph(ev);
    }

    #region Helpers

    public abstract void CreateChargeEffect(EntityUid uid, ChargeSpellRaysEffectEvent ev);

    protected void PopupCharged(EntityUid uid, EntityUid performer, bool client = true)
    {
        var message = Loc.GetString("spell-charge-spells-charged-entity",
            ("entity", Identity.Entity(uid, EntityManager)));
        if (client)
            PopupLoc(performer, message, PopupType.Medium);
        else
            _popup.PopupEntity(message, performer, performer, PopupType.Medium);
    }

    private bool RechargeAllSpells(EntityUid uid, EntityUid? except = null)
    {
        var magicQuery = GetEntityQuery<MagicComponent>();
        var ents = except != null
            ? Actions.GetActions(uid).Where(x => x.Id != except.Value && magicQuery.HasComp((EntityUid)x.Id))
            : Actions.GetActions(uid).Where(x => magicQuery.HasComp(x.Id));
        var hasSpells = false;
        foreach (var (ent, _) in ents)
        {
            hasSpells = true;
            Actions.SetCooldown(ent, TimeSpan.Zero);
        }

        return hasSpells;
    }

    protected void SetGear(EntityUid uid,
        Dictionary<string, EntProtoId> gear,
        bool force = true,
        bool makeUnremoveable = true,
        InventoryComponent? inventoryComponent = null)
    {
        if (_net.IsClient)
            return;

        if (!Resolve(uid, ref inventoryComponent, false))
            return;

        foreach (var (slot, item) in gear)
        {
            _inventory.TryUnequip(uid, slot, true, force, false, inventoryComponent);

            var ent = Spawn(item, Transform(uid).Coordinates);
            if (!_inventory.TryEquip(uid, ent, slot, true, force, false, inventoryComponent))
            {
                Del(ent);
                continue;
            }

            if (slot == "id" &&
                TryComp(ent, out PdaComponent? pdaComponent) &&
                TryComp<IdCardComponent>(pdaComponent.ContainedId, out var id))
                id.FullName = MetaData(uid).EntityName;

            if (makeUnremoveable && HasComp<ClothingComponent>(ent))
                EnsureComp<UnremoveableComponent>(ent);
        }
    }

    #endregion

    #region ServerMethods

    protected virtual bool ChargeItem(EntityUid uid, ChargeMagicEvent ev)
    {
        return true;
    }

    protected virtual bool Polymorph(PolymorphSpellEvent ev)
    {
        return true;
    }

    protected virtual void BindSoul(BindSoulEvent ev, EntityUid item, EntityUid mind, MindComponent mindComponent) { }
    public virtual void SpeakSpell(EntityUid speakerUid, EntityUid casterUid, string speech, MagicSchool school) { }

    #endregion
    #region Helpers
    private void PopupLoc(EntityUid uid, string locMessage, PopupType type = PopupType.Small)
    {
        _popup.PopupClient(locMessage, uid, uid, type);
    }

    private void Popup(EntityUid uid, string message, PopupType type = PopupType.Small)
    {
        _popup.PopupClient(Loc.GetString(message), uid, uid, type);
    }
    #endregion
}

[Serializable, NetSerializable]
public sealed class ChargeSpellRaysEffectEvent(NetEntity uid) : EntityEventArgs
{
    public NetEntity Uid = uid;
}
