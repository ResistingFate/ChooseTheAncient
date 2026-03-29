using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;

namespace ChooseTheAncient.ChooseTheAncientCode;

public sealed partial class AncientBanSelectionScreen : Control, IOverlayScreen, IScreenContext
{
    private const string LayoutScenePath =
        "res://scenes/mod/choose_the_ancient/ancient_ban_selection_screen.tscn";

    private const string CardScenePath =
        "res://scenes/mod/choose_the_ancient/ancient_ban_choice_card.tscn";

    private readonly TaskCompletionSource<int> _voteSubmitted = new();
    private readonly List<Button> _choiceButtons = new();

    private IReadOnlyList<AncientEventModel> _pool = Array.Empty<AncientEventModel>();
    private int _nextActIndex;
    private bool _resolved;
    private bool _closing;

    private Control? _layoutRoot;
    private Label? _titleLabel;
    private Label? _subtitleLabel;
    private HBoxContainer? _choicesRow;

    public NetScreenType ScreenType => NetScreenType.Rewards;
    public bool UseSharedBackstop => true;
    public Control? DefaultFocusedControl { get; private set; }

    public AncientBanSelectionScreen()
    {
        Name = "AncientBanSelectionScreen";
        ProcessMode = ProcessModeEnum.Always;
        MouseFilter = MouseFilterEnum.Stop;
        FocusMode = FocusModeEnum.All;

        SetAnchorsPreset(LayoutPreset.FullRect);
        OffsetLeft = 0;
        OffsetTop = 0;
        OffsetRight = 0;
        OffsetBottom = 0;
    }

    public static AncientBanSelectionScreen Show(IReadOnlyList<AncientEventModel> pool, int nextActIndex)
    {
        AncientBanSelectionScreen screen = new();
        screen.Initialize(pool, nextActIndex);
        NOverlayStack.Instance.Push(screen);
        return screen;
    }

    public void Initialize(IReadOnlyList<AncientEventModel> pool, int nextActIndex)
    {
        _pool = pool;
        _nextActIndex = nextActIndex;
    }

    public Task<int> WaitForVoteAsync()
    {
        return _voteSubmitted.Task;
    }

    public override void _Ready()
    {
        PackedScene? layoutScene = GD.Load<PackedScene>(LayoutScenePath);
        if (layoutScene == null)
        {
            throw new InvalidOperationException($"Could not load card scene: {{CardScenePath}}");
        }

        _layoutRoot = layoutScene.Instantiate<Control>();
        _layoutRoot.SetAnchorsPreset(LayoutPreset.FullRect);
        _layoutRoot.OffsetLeft = 0;
        _layoutRoot.OffsetTop = 0;
        _layoutRoot.OffsetRight = 0;
        _layoutRoot.OffsetBottom = 0;
        AddChild(_layoutRoot);

        _titleLabel = _layoutRoot.GetNode<Label>("OuterMargin/Panel/Padding/Root/TitleLabel");
        _subtitleLabel = _layoutRoot.GetNode<Label>("OuterMargin/Panel/Padding/Root/SubtitleLabel");
        _choicesRow = _layoutRoot.GetNode<HBoxContainer>("OuterMargin/Panel/Padding/Root/ChoicesRow");

        _titleLabel.Text = _nextActIndex == 1
            ? "Choose 1 Ancient to Remove Before Act 2"
            : "Choose 1 Ancient to Remove Before Act 3";

        _subtitleLabel.Text = "Each player votes for 1 ancient to remove. Majority bans it; ties are broken randomly.";

        PopulateChoices();
        GrabInitialFocus();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_resolved)
        {
            return;
        }

        if (@event.IsActionPressed("ui_cancel"))
        {
            AcceptEvent();
        }
    }

    private void PopulateChoices()
    {
        if (_choicesRow == null)
        {
            throw new InvalidOperationException("ChoicesRow was not found in the layout scene.");
        }

        foreach (Node child in _choicesRow.GetChildren())
        {
            child.QueueFree();
        }

        _choiceButtons.Clear();
        DefaultFocusedControl = null;

        PackedScene? cardScene = GD.Load<PackedScene>(CardScenePath);
        if (cardScene == null)
        {
            throw new InvalidOperationException($"Could not load card scene: {CardScenePath}");
        }

        for (int i = 0; i < _pool.Count; i++)
        {
            int capturedIndex = i;
            AncientEventModel ancient = _pool[i];

            Control card = cardScene.Instantiate<Control>();
            _choicesRow.AddChild(card);

            TextureRect icon = card.GetNode<TextureRect>("Padding/VBox/Icon");
            Label nameLabel = card.GetNode<Label>("Padding/VBox/NameLabel");
            Label epithetLabel = card.GetNode<Label>("Padding/VBox/EpithetLabel");
            Button chooseButton = card.GetNode<Button>("Padding/VBox/ChooseButton");

            icon.Texture = ancient.MapIcon;
            nameLabel.Text = ancient.Title.GetFormattedText();

            try
            {
                epithetLabel.Text = ancient.Epithet.GetFormattedText();
                epithetLabel.Visible = true;
            }
            catch
            {
                epithetLabel.Text = "";
                epithetLabel.Visible = false;
            }

            chooseButton.Text = "Ban This Ancient";
            chooseButton.Pressed += () => Select(capturedIndex);

            _choiceButtons.Add(chooseButton);
            DefaultFocusedControl ??= chooseButton;
        }
    }

    private void GrabInitialFocus()
    {
        DefaultFocusedControl?.CallDeferred(Control.MethodName.GrabFocus);
    }

    private void Select(int bannedIndex)
    {
        if (_resolved)
        {
            return;
        }

        _resolved = true;

        foreach (Button button in _choiceButtons)
        {
            button.Disabled = true;
        }

        if (_subtitleLabel != null)
        {
            _subtitleLabel.Text = "Vote submitted. Waiting for other players...";
        }

        _voteSubmitted.TrySetResult(bannedIndex);
    }

    public void CloseScreen()
    {
        if (_closing)
        {
            return;
        }

        _closing = true;

        if (IsInsideTree())
        {
            NOverlayStack.Instance.Remove(this);
        }
        else
        {
            QueueFree();
        }
    }

    public override void _ExitTree()
    {
        if (!_voteSubmitted.Task.IsCompleted)
        {
            _voteSubmitted.TrySetCanceled();
        }

        base._ExitTree();
    }

    public void AfterOverlayOpened()
    {
        Visible = true;
        GrabInitialFocus();
    }

    public void AfterOverlayClosed()
    {
        QueueFree();
    }

    public void AfterOverlayShown()
    {
        Visible = true;
        GrabInitialFocus();
    }

    public void AfterOverlayHidden()
    {
        Visible = false;
    }
}