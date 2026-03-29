using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;

namespace ChooseTheAncient.ChooseTheAncientCode;
[GlobalClass]
public sealed partial class AncientBanSelectionScreen : Control, IOverlayScreen, IScreenContext
{
    private readonly TaskCompletionSource<int> _voteSubmitted = new();
    private readonly List<Button> _choiceButtons = new();
    private IReadOnlyList<AncientEventModel> _pool = Array.Empty<AncientEventModel>();
    private int _nextActIndex;
    private bool _resolved;

    private Label? _titleLabel;
    private Label? _subtitleLabel;
    private HBoxContainer? _choicesRow;

    public NetScreenType ScreenType => NetScreenType.Rewards;
    public bool UseSharedBackstop => true;
    public Control? DefaultFocusedControl { get; private set; }

    private const string ScreenScenePath =
        "res://scenes/mod/choose_the_ancient/ancient_ban_selection_screen.tscn";
    
    private const string CardScenePath =
        "res://scenes/mod/choose_the_ancient/ancient_ban_choice_card.tscn";
    
    public static AncientBanSelectionScreen Show(IReadOnlyList<AncientEventModel> pool, int nextActIndex)
    {
        GD.Print($"[ChooseTheAncient] Trying to load scene: {ScreenScenePath}");
        GD.Print($"[ChooseTheAncient] Global path: {ProjectSettings.GlobalizePath(ScreenScenePath)}");
        GD.Print($"[ChooseTheAncient] Resource exists: {ResourceLoader.Exists(ScreenScenePath)}");

        PackedScene? scene = GD.Load<PackedScene>(ScreenScenePath);
        if (scene == null)
        {
            throw new InvalidOperationException($"Could not load scene: {ScreenScenePath}");
        }

        AncientBanSelectionScreen screen = scene.Instantiate<AncientBanSelectionScreen>();
        screen.Initialize(pool, nextActIndex);
        NOverlayStack.Instance.Push(screen);
        return screen;
    }

    public void Initialize(IReadOnlyList<AncientEventModel> pool, int nextActIndex)
    {
        _pool = pool;
        _nextActIndex = nextActIndex;
    }

    public Task<int> WaitForVoteAsync() => _voteSubmitted.Task;

    public override void _Ready()
    {
        _titleLabel = GetNode<Label>("OuterMargin/Panel/Padding/Root/TitleLabel");
        _subtitleLabel = GetNode<Label>("OuterMargin/Panel/Padding/Root/SubtitleLabel");
        _choicesRow = GetNode<HBoxContainer>("OuterMargin/Panel/Padding/Root/ChoicesRow");

        _titleLabel.Text = _nextActIndex == 1
            ? "Choose 1 Ancient to Remove Before Act 2"
            : "Choose 1 Ancient to Remove Before Act 3";

        _subtitleLabel.Text = "Each player votes for 1 ancient to remove. Majority bans it; ties are broken randomly.";

        PopulateChoices();
        GrabInitialFocus();
    }

    private void PopulateChoices()
    {
        if (_choicesRow == null)
        {
            return;
        }

        foreach (Node child in _choicesRow.GetChildren())
        {
            child.QueueFree();
        }
        GD.Print($"[ChooseTheAncient] Trying to load scene: {CardScenePath}");
        GD.Print($"[ChooseTheAncient] Global path: {ProjectSettings.GlobalizePath(CardScenePath)}");
        GD.Print($"[ChooseTheAncient] Resource exists: {ResourceLoader.Exists(CardScenePath)}");

        PackedScene cardScene = GD.Load<PackedScene>(CardScenePath);

        for (int i = 0; i < _pool.Count; i++)
        {
            int capturedIndex = i;
            AncientBanChoiceCard card = cardScene.Instantiate<AncientBanChoiceCard>();
            _choicesRow.AddChild(card);
            card.Setup(_pool[i], () => Select(capturedIndex));

            card.CallDeferred(nameof(RegisterChoiceButton), card);
        }
    }
    
    private void RegisterChoiceButton(AncientBanChoiceCard card)
    {
        _choiceButtons.Add(card.ChooseButton);
        DefaultFocusedControl ??= card.ChooseButton;
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