using Content.Client.UserInterface.Systems.Chat.Controls;
using Content.Shared._White;
using Content.Shared.CCVar;
using Content.Shared.Chat;
using Content.Shared.Input;
using Robust.Client.Audio;
using Robust.Client.AutoGenerated;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.XAML;
using Robust.Shared;
using Robust.Shared.Audio;
using Robust.Shared.Configuration;
using Robust.Shared.Input;
using Robust.Shared.Player;
using Robust.Shared.Utility;
using static Robust.Client.UserInterface.Controls.LineEdit;

namespace Content.Client.UserInterface.Systems.Chat.Widgets;

[GenerateTypedNameReferences]
#pragma warning disable RA0003
public partial class ChatBox : UIWidget
#pragma warning restore RA0003
{
    private readonly ChatUIController _controller;
    private readonly IEntityManager _entManager;
    private readonly IConfigurationManager _cfg;
    private readonly ILocalizationManager _loc;

    public bool Main { get; set; }

    public ChatSelectChannel SelectedChannel => ChatInput.ChannelSelector.SelectedChannel;
   
    private int _chatStackAmount = 0;
    private bool _chatStackEnabled => _chatStackAmount > 0;
    private List<ChatStackData> _chatStackList;

    private bool _chatFontEnabled; // WWDP EDIT
   

    public ChatBox()
    {
        RobustXamlLoader.Load(this);
        _loc = IoCManager.Resolve<ILocalizationManager>();
        _entManager = IoCManager.Resolve<IEntityManager>();

        ChatInput.Input.OnTextEntered += OnTextEntered;
        ChatInput.Input.OnKeyBindDown += OnInputKeyBindDown;
        ChatInput.Input.OnTextChanged += OnTextChanged;
        ChatInput.ChannelSelector.OnChannelSelect += OnChannelSelect;
        ChatInput.FilterButton.Popup.OnChannelFilter += OnChannelFilter;

        _controller = UserInterfaceManager.GetUIController<ChatUIController>();
        _controller.MessageAdded += OnMessageAdded;
        _controller.RegisterChat(this);

       
        _cfg = IoCManager.Resolve<IConfigurationManager>();
        //_chatStackAmount = _cfg.GetCVar(CCVars.ChatStackLastLines);
        //if (_chatStackAmount < 0) // anti-idiot protection
        //    _chatStackAmount = 0;
        _chatStackList = new(_chatStackAmount);
        _cfg.OnValueChanged(CCVars.ChatStackLastLines, UpdateChatStack, true);
        _cfg.OnValueChanged(WhiteCVars.ChatFancyFont, value => { _chatFontEnabled = value; Repopulate(); }, true); // WWDP EDIT
       
    }

   
    private void UpdateChatStack(int value)
    {
        _chatStackAmount = value >= 0 ? value : 0;
        Repopulate();
    }

    private void OnTextEntered(LineEditEventArgs args)
    {
        _controller.SendMessage(this, SelectedChannel);
    }

    private void OnMessageAdded(ChatMessage msg)
    {
        Logger.DebugS("chat", $"{msg.Channel}: {msg.Message}");
        if (!ChatInput.FilterButton.Popup.IsActive(msg.Channel))
        {
            return;
        }

        if (msg is { Read: false, AudioPath: { } })
            _entManager.System<AudioSystem>().PlayGlobal(msg.AudioPath, Filter.Local(), false, AudioParams.Default.WithVolume(msg.AudioVolume));

        msg.Read = true;

        var color = msg.MessageColorOverride ?? msg.Channel.TextColor();

       
        if (msg.IgnoreChatStack)
        {
            TrackNewMessage(msg.WrappedMessage, color, true);
            AddLine(msg.WrappedMessage, color);
            return;
        }

        int index = _chatStackList.FindIndex(data => data.WrappedMessage == msg.WrappedMessage && !data.IgnoresChatstack);

        if (index == -1) // this also handles chatstack being disabled, since FindIndex won't find anything in an empty array
        {
            TrackNewMessage(msg.WrappedMessage, color);
            AddLine(msg.WrappedMessage, color);
            return;
        }

        UpdateRepeatingLine(index);
    }

    /// <summary>
    /// Removing and then adding insantly nudges the chat window up before slowly dragging it back down, which makes the whole chat log shake.
    /// With rapid enough updates, the whole chat becomes unreadable.
    /// Adding first and then removing does not produce any visual effects.
    /// The other option is to dublicate OutputPanel functionality and everything internal to the engine it relies on.
    /// But OutputPanel relies on directly setting Control.Position for control embedding. (which is not exposed to Content.)
    /// Thanks robustengine, very cool.
    /// </summary>
    /// <remarks>
    /// zero index is the very last line in chat, 1 is the line before the last one, 2 is the line before that, etc.
    /// </remarks>
    private void UpdateRepeatingLine(int index)
    {
        _chatStackList[index].RepeatCount++;
        for (int i = index; i >= 0; i--)
        {
            var data = _chatStackList[i];
            AddLine(data.WrappedMessage, data.ColorOverride, data.RepeatCount);
            Contents.RemoveEntry(Index.FromEnd(index + 2));
        }
    }

    private void TrackNewMessage(string wrappedMessage, Color colorOverride, bool ignoresChatstack = false)
    {
        if (!_chatStackEnabled)
            return;

        if(_chatStackList.Count == _chatStackList.Capacity)
            _chatStackList.RemoveAt(_chatStackList.Capacity - 1);

        _chatStackList.Insert(0, new ChatStackData(wrappedMessage, colorOverride, ignoresChatstack)); 
    }

    private void OnChannelSelect(ChatSelectChannel channel)
    {
        _controller.UpdateSelectedChannel(this);
    }

    public void Repopulate()
    {
        Contents.Clear();
        _chatStackList = new List<ChatStackData>(_chatStackAmount);
        foreach (var message in _controller.History)
        {
            OnMessageAdded(message.Item2);
        }
    }

    private void OnChannelFilter(ChatChannel channel, bool active)
    {
        Contents.Clear();

        foreach (var message in _controller.History)
        {
            OnMessageAdded(message.Item2);
        }

        if (active)
        {
            _controller.ClearUnfilteredUnreads(channel);
        }
    }

    public void AddLine(string message, Color color, int repeat = 0)
    {
        // WWDP EDIT START // I FUCKING HATE THIS ENGINE
        if (_chatFontEnabled)
        {
            message = $"[font=\"Chat\"]{message}[/font]";
            message = message.Replace("[font size=", "[font=\"Chat\" size="); // AAAAAAAAAAAAAAAA
            message = message.Replace("[font=\"Default\"", "[font=\"Chat\""); // AAAAAAAAAAAAAAAA
            message = message.Replace("[bold]", "[cb]");
            message = message.Replace("[/bold]", "[/cb]");
            message = message.Replace("[italic]", "[ci]");
            message = message.Replace("[/italic]", "[/ci]");
        }
        // WWDP EDIT END
        var formatted = new FormattedMessage(4); 
        formatted.PushColor(color);
        formatted.AddMarkup(message);
        formatted.Pop();
        if (repeat != 0)
        {
            int displayRepeat = repeat + 1;
            int sizeIncrease = Math.Min(displayRepeat / 6, 5);
            formatted.AddMarkup(_loc.GetString("chat-system-repeated-message-counter",
                                ("count", displayRepeat),
                                ("size", 8 + sizeIncrease)
                                ));
        }
        Contents.AddMessage(formatted);
    }

    public void Focus(ChatSelectChannel? channel = null)
    {
        var input = ChatInput.Input;
        var selectStart = Index.End;

        if (channel != null)
            ChatInput.ChannelSelector.Select(channel.Value);

        input.IgnoreNext = true;
        input.GrabKeyboardFocus();

        input.CursorPosition = input.Text.Length;
        input.SelectionStart = selectStart.GetOffset(input.Text.Length);
    }

    public void CycleChatChannel(bool forward)
    {
        var idx = Array.IndexOf(ChannelSelectorPopup.ChannelSelectorOrder, SelectedChannel);
        do
        {
            // go over every channel until we find one we can actually select.
            idx += forward ? 1 : -1;
            idx = MathHelper.Mod(idx, ChannelSelectorPopup.ChannelSelectorOrder.Length);
        } while ((_controller.SelectableChannels & ChannelSelectorPopup.ChannelSelectorOrder[idx]) == 0);

        SafelySelectChannel(ChannelSelectorPopup.ChannelSelectorOrder[idx]);
    }

    public void SafelySelectChannel(ChatSelectChannel toSelect)
    {
        toSelect = _controller.MapLocalIfGhost(toSelect);
        if ((_controller.SelectableChannels & toSelect) == 0)
            return;

        ChatInput.ChannelSelector.Select(toSelect);
    }

    private void OnInputKeyBindDown(GUIBoundKeyEventArgs args)
    {
        if (args.Function == EngineKeyFunctions.TextReleaseFocus)
        {
            ChatInput.Input.ReleaseKeyboardFocus();
            ChatInput.Input.Clear();
            args.Handle();
            return;
        }

        if (args.Function == ContentKeyFunctions.CycleChatChannelForward)
        {
            CycleChatChannel(true);
            args.Handle();
            return;
        }

        if (args.Function == ContentKeyFunctions.CycleChatChannelBackward)
        {
            CycleChatChannel(false);
            args.Handle();
        }
    }

    private void OnTextChanged(LineEditEventArgs args)
    {
        // Update channel select button to correct channel if we have a prefix.
        _controller.UpdateSelectedChannel(this);

        // Warn typing indicator about change
        _controller.NotifyChatTextChange();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing) return;
        _controller.UnregisterChat(this);
        ChatInput.Input.OnTextEntered -= OnTextEntered;
        ChatInput.Input.OnKeyBindDown -= OnInputKeyBindDown;
        ChatInput.Input.OnTextChanged -= OnTextChanged;
        ChatInput.ChannelSelector.OnChannelSelect -= OnChannelSelect;
        _cfg.UnsubValueChanged(CCVars.ChatStackLastLines, UpdateChatStack);
    }

    private class ChatStackData
    {
        public string WrappedMessage;
        public Color ColorOverride;
        public int RepeatCount = 0;
        public bool IgnoresChatstack;
        public ChatStackData(string wrappedMessage, Color colorOverride, bool ignoresChatstack = false)
        {
            WrappedMessage = wrappedMessage;
            ColorOverride = colorOverride;
            IgnoresChatstack = ignoresChatstack;
        }
    }
}
