using Content.Server.Antag;
using Content.Server.Chat.Systems;
using Content.Server.IdentityManagement;
using Content.Server.Inventory;
using Content.Server.Polymorph.Systems;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Roles;
using Content.Shared._Goobstation.Actions;
using Content.Shared._White.Wizard;
using Content.Shared._White.Wizard.BindSoul;
using Content.Shared.Chat;
using Content.Shared.Construction.Components;
using Content.Shared.Gibbing.Events;
using Content.Shared.Humanoid;
using Content.Shared.IdentityManagement;
using Content.Shared.Item;
using Content.Shared.Magic.Components;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Speech.Components;
using Content.Shared.Tag;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects.Components.Localization;
using Robust.Shared.Player;
using Robust.Shared.Timing;


namespace Content.Server._White.Wizard.Systems;


public sealed class SpellsSystem : SharedSpellsSystem
{
    [Dependency] private readonly BatterySystem _battery = default!;
    [Dependency] private readonly PolymorphSystem _polymorph = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly IdentitySystem _identity = default!;
    [Dependency] private readonly SharedItemSystem _item = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly AntagSelectionSystem _antag = default!;
    [Dependency] private readonly ServerInventorySystem _inventory = default!;

    public override void Initialize()
    {
        base.Initialize();
    }

    public override void CreateChargeEffect(EntityUid uid, ChargeSpellRaysEffectEvent ev)
    {
        RaiseNetworkEvent(ev, Filter.PvsExcept(uid));
    }

    protected override bool ChargeItem(EntityUid uid, ChargeMagicEvent ev)
    {
        if (!TryComp(uid, out BatteryComponent? battery) || battery.CurrentCharge >= battery.MaxCharge)
            return false;

        if (Tag.HasTag(uid, ev.WandTag))
        {
            var difference = battery.MaxCharge - battery.CurrentCharge;
            var charge = MathF.Min(difference, ev.WandChargeRate);
            var degrade = charge * ev.WandDegradePercentagePerCharge;
            var afterDegrade = MathF.Max(ev.MinWandDegradeCharge, battery.MaxCharge - degrade);
            if (battery.MaxCharge > ev.MinWandDegradeCharge)
                _battery.SetMaxCharge(uid, afterDegrade, battery);
            _battery.AddCharge(uid, charge, battery);
        }
        else
            _battery.SetCharge(uid, battery.MaxCharge, battery);

        PopupCharged(uid, ev.Performer, false);
        return true;
    }

        protected override void BindSoul(BindSoulEvent ev, EntityUid item, EntityUid mind, MindComponent mindComponent)
    {
        base.BindSoul(ev, item, mind, mindComponent);

        var oldEnt = ev.Performer;
        var xform = Transform(oldEnt);
        var meta = MetaData(oldEnt);

        var mapId = xform.MapUid;

        var newEntity = Spawn(ev.Entity,
            TransformSystem.GetMapCoordinates(oldEnt, xform),
            rotation: TransformSystem.GetWorldRotation(oldEnt));

        if (Container.TryGetContainingContainer((oldEnt, xform, meta), out var cont))
            Container.Insert(newEntity, cont);

        var name = meta.EntityName;

        Meta.SetEntityName(newEntity, name);

        int? age = null;
        Gender? gender = null;
        Sex? sex = null;
        if (TryComp(oldEnt, out HumanoidAppearanceComponent? humanoid))
        {
            age = humanoid.Age;
            gender = humanoid.Gender;
            sex = humanoid.Sex;
            if (TryComp(newEntity, out HumanoidAppearanceComponent? newHumanoid))
            {
                newHumanoid.Age = age.Value;
                newHumanoid.Gender = gender.Value;
                newHumanoid.Sex = sex.Value;
                Dirty(newEntity, newHumanoid);
                if (TryComp(newEntity, out GrammarComponent? grammar))
                    Grammar.SetGender((newEntity, grammar), gender.Value);
                var identity = Identity.Entity(newEntity, EntityManager);
                if (TryComp(identity, out GrammarComponent? identityGrammar))
                    Grammar.SetGender((identity, identityGrammar), gender.Value);
            }
        }

        _identity.QueueIdentityUpdate(newEntity);

        Mind.TransferTo(mind, newEntity, mind: mindComponent);

        Faction.ClearFactions(newEntity, false);
        Faction.AddFaction(newEntity, WizardRuleSystem.Faction);
        RemCompDeferred<TransferMindOnGibComponent>(newEntity);
        EnsureComp<WizardComponent>(newEntity);
        if (!Role.MindHasRole<WizardRoleComponent>(mind, out _))
            Role.MindAddRole(mind, WizardRuleSystem.Role.Id, mindComponent, true);

        EnsureComp<PhylacteryComponent>(item);
        _item.SetSize(item, ev.PhylacterySize);
        RemCompDeferred<TagComponent>(item);
        RemCompDeferred<AnchorableComponent>(item);

        var soulBound = EntityManager.ComponentFactory.GetComponent<SoulBoundComponent>();
        soulBound.Name = name;
        soulBound.Item = item;
        soulBound.MapId = mapId;
        soulBound.Age = age;
        soulBound.Gender = gender;
        soulBound.Sex = sex;
        AddComp(mind, soulBound, true);

        _inventory.TransferEntityInventories(oldEnt, newEntity);
        foreach (var hand in Hands.EnumerateHeld(oldEnt))
        {
            Hands.TryDrop(oldEnt, hand, checkActionBlocker: false);
            Hands.TryPickupAnyHand(newEntity, hand);
        }

        SetGear(newEntity, ev.Gear, false, false);

        if (TryComp(ev.Action.Owner, out SpeakOnActionComponent? speak))
        {
            DelayedSpeech(speak.Sentence == null ? null : Loc.GetString(speak.Sentence.Value),
                newEntity,
                oldEnt,
                MagicSchool.Necromancy);
        }

        Body.GibBody(oldEnt, contents: GibContentsOption.Gib);

        if (!_player.TryGetSessionById(mindComponent.UserId, out var session))
            return;

        _antag.SendBriefing(session, Loc.GetString("lich-greeting"), Color.DarkRed, ev.Sound);
    }

    protected override bool Polymorph(PolymorphSpellEvent ev)
    {
        if (ev.ProtoId == null)
            return false;

        var newEnt = _polymorph.PolymorphEntity(ev.Performer, ev.ProtoId.Value);

        if (newEnt == null)
            return false;

        if (ev.MakeWizard)
        {
            if (HasComp<WizardComponent>(ev.Performer))
                EnsureComp<WizardComponent>(newEnt.Value);
            if (HasComp<ApprenticeComponent>(ev.Performer))
                EnsureComp<ApprenticeComponent>(newEnt.Value);
        }

        Audio.PlayPvs(ev.Sound, newEnt.Value);

        var school = MagicSchool.Transmutation;
        if (TryComp(ev.Action.Owner, out MagicComponent? magic))
            school = magic.School;

        if (ev.LoadActions)
            RaiseNetworkEvent(new LoadActionsEvent(GetNetEntity(ev.Performer)), newEnt.Value);

        if (TryComp(ev.Action.Owner, out SpeakOnActionComponent? speak))
        {
            DelayedSpeech(speak.Sentence == null ? null : Loc.GetString(speak.Sentence.Value),
                newEnt.Value,
                ev.Performer,
                school);
        }

        return true;
    }

    private void DelayedSpeech(string? speech, EntityUid speaker, EntityUid caster, MagicSchool school)
    {
        Timer.Spawn(200,
            () =>
            {
                var toSpeak = speech == null ? string.Empty : Loc.GetString(speech);
                SpeakSpell(speaker, caster, toSpeak, school);
            });
    }

    public override void SpeakSpell(EntityUid speakerUid, EntityUid casterUid, string speech, MagicSchool school)
    {
        base.SpeakSpell(speakerUid, casterUid, speech, school);

        if (!Exists(speakerUid))
            return;

        Color? color = null;

        if (Exists(casterUid))
        {
            // var invocationEv = new GetSpellInvocationEvent(school, casterUid);    WD edit - uncomment once Chuuni invocations get ported
            // RaiseLocalEvent(casterUid, invocationEv);
            // if (invocationEv.Invocation != null)
            //     speech = Loc.GetString(invocationEv.Invocation);
            // if (invocationEv.ToHeal.GetTotal() > FixedPoint2.Zero)
            // {
            //     // Heal both caster and speaker
            //     Damageable.TryChangeDamage(casterUid,
            //         -invocationEv.ToHeal,
            //         true,
            //         false,
            //         targetPart: TargetBodyPart.All,
            //         splitDamage: SplitDamageBehavior.SplitEnsureAll);
            //
            //     if (speakerUid != casterUid)
            //     {
            //         Damageable.TryChangeDamage(speakerUid,
            //             -invocationEv.ToHeal,
            //             true,
            //             false,
            //             targetPart: TargetBodyPart.All,
            //             splitDamage: SplitDamageBehavior.SplitEnsureAll);
            //     }
            // }
            //
            // if (speakerUid != casterUid)
            // {
            //     var colorEv = new GetMessageColorOverrideEvent();
            //     RaiseLocalEvent(casterUid, colorEv);
            //     color = colorEv.Color;
            // }
        }

        _chat.TrySendInGameICMessage(speakerUid,
            speech,
            InGameICChatType.Speak,
            false);
    }
}
