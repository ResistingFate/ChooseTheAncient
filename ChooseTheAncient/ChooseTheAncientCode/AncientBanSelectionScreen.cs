using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Audio.Debug;
using MegaCrit.Sts2.Core.Context;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.RichTextTags;
using MegaCrit.Sts2.Core.Nodes;

namespace ChooseTheAncient.ChooseTheAncientCode;

public sealed partial class AncientBanSelectionScreen : Control, IOverlayScreen, IScreenContext
{
    private const string LayoutScenePath =
        "res://scenes/mod/choose_the_ancient/ancient_ban_selection_screen.tscn";

    private const string CardScenePath =
        "res://scenes/mod/choose_the_ancient/ancient_ban_choice_card.tscn";

    private const float HoverSceneScaleMultiplier = 1.028f;
    private const float CardBottomInset = 18f;
    private const float CardHeightRatio = 0.275f;
    private const float PortalRimThickness = 6f;
    private const float ReactionBubbleHeight = 96f;
    private const float ReactionBubbleGap = 10f;
    private static readonly string DialogueBubbleTexturePath = "res://images/ui/dialogue_nine_patch.png";
    private static readonly string DialogueTailTexturePath = "res://images/ui/dialogue_tail.png";
    private static readonly string DialogueRegularFontPath = "res://themes/kreon_regular_glyph_space_one.tres";
    private static readonly string DialogueBoldFontPath = "res://themes/kreon_bold_glyph_space_one.tres";
    private static readonly string DialogueItalicFontPath = "res://themes/bitter_medium_italic_glyph_space_one.tres";
    // Animation tuning:
    // Increase the duration values or offsets below if you want a slower, floatier final-vote entrance.
    private const float ReactionEntranceOffset = 40f;
    private const float PreviewEntranceOffset = 44f;
    private const double ReactionEntranceDuration = 0.82;
    private const double PreviewEntranceDuration = 0.76;
    private const double ReactionTextDuration = 0.58;
    private const double FinalRoundStagger = 0.22;
    private const float PreviewHoverScaleMultiplier = 1.008f;
    private const float VoteIconSize = 28;
    private const float VoteIconOverlap = 10f;
    private const double VoteResolutionSpinDuration = 1.20;
    private const float VoteResolutionSettleDelayMin = 0.05f;
    private const float VoteResolutionSettleDelayMax = 0.30f;
    // Slot visual timing:
    // Increase CardOutlineFadeDuration for a slower outline fade when focus/emphasis changes.
    private const double SlotVisualFadeDuration = 0.12;
    private const double CardOutlineFadeDuration = 0.20;
    // Second-round initial winner emphasis fade tuning:
    // Raise the duration or change the transition/ease below for a slower, softer fade after focus moves away.
    private const double InitialSecondRoundWinnerEmphasisFadeDuration = 0.58;
    private const Tween.TransitionType InitialSecondRoundWinnerEmphasisFadeTransition = Tween.TransitionType.Quint;
    private const Tween.EaseType InitialSecondRoundWinnerEmphasisFadeEase = Tween.EaseType.InOut;
    
    public enum VoteRoundType
    {
        InitialKeepVote,
        FinalRevealVote,
    }

    public readonly record struct RoundDefinition(
        IReadOnlyList<AncientEventModel> Pool,
        VoteRoundType RoundType,
        IReadOnlyDictionary<string, AncientBanHelpers.AncientPreviewData>? PreviewDataByAncientId,
        string? SuppressedPreviewAncientId,
        string? ReactionAncientId);

    private readonly record struct AncientSceneConfig(
        Vector2 BaseSize,
        float Scale,
        Vector2 SourceAnchor01,
        Vector2 ExtraOffset01);

    private readonly record struct SceneTransform(
        Vector2 Size,
        Vector2 Position,
        Vector2 Scale);

    private readonly record struct PortalShape(
        Rect2 PortalRect,
        Rect2 CardRect,
        Vector2 TopLeft,
        Vector2 TopRight,
        Vector2 BottomRight,
        Vector2 BottomLeft,
        int ZIndex);

    private sealed class SlotRefs
    {
        public required int PoolIndex { get; init; }
        public required AncientEventModel Ancient { get; init; }
        public required Control SlotRoot { get; init; }
        public required SubViewport SceneViewport { get; init; }
        public required Control SceneMount { get; init; }
        public required Polygon2D ScenePolygon { get; init; }
        public required Polygon2D GlowPolygon { get; init; }
        public required Polygon2D HoverFlashPolygon { get; init; }
        public required Polygon2D LeftRim { get; init; }
        public required Polygon2D RightRim { get; init; }
        public required Control CardRoot { get; init; }
        public required ColorRect CardShade { get; init; }
        public required ColorRect TopAccent { get; init; }
        public required Panel CardOutline { get; init; }
        public required TextureRect Icon { get; init; }
        public required Label NameLabel { get; init; }
        public required Label EpithetLabel { get; init; }
        public required Control ChooseButtonWrap { get; init; }
        public required Button ChooseButton { get; init; }
        public required Control CardClickTarget { get; init; }
        public required Control SlotClickTarget { get; init; }
        public required TextureRect? ChooseButtonControllerIcon { get; init; }
        public required Control VoteIconsAnchor { get; init; }
        public required Control PreviewAnchor { get; init; }
        public required Control ReactionAnchor { get; init; }
        public required Color AccentColor { get; init; }

        public Node? SceneRoot { get; set; }
        public NMultiplayerVoteContainer? VoteContainer { get; set; }
        public Control? ReactionBubble { get; set; }
        public Vector2 BaseSize { get; set; }
        public Vector2 CardBasePosition { get; set; }
        public PortalShape Shape { get; set; }
        public List<PreviewWidgetRefs> PreviewWidgets { get; } = new();
        public NinePatchRect ChooseButtonOutline { get; set; }
    }


    private sealed class PreviewWidgetRefs
    {
        /* For hover over ancient options in final preview vote */
        public required Control Wrapper { get; init; }
        public required NEventOptionButton Button { get; init; }
        public required EventOption Option { get; init; }
        public required HoverTipAlignment HoverAlignment { get; init; }
        public NinePatchRect? Outline { get; init; }
        public ShaderMaterial? HsvMaterial { get; init; }
        public Vector2 BasePosition { get; set; }
        public Vector2 BaseScale { get; set; }
    }

    private static readonly Dictionary<string, string> AncientScenePaths = new()
    {
        ["DARV"] = "res://scenes/events/background_scenes/darv.tscn",
        ["OROBAS"] = "res://scenes/events/background_scenes/orobas.tscn",
        ["PAEL"] = "res://scenes/events/background_scenes/pael.tscn",
        ["TEZCATARA"] = "res://scenes/events/background_scenes/tezcatara.tscn",
        ["NONUPEIPE"] = "res://scenes/events/background_scenes/nonupeipe.tscn",
        ["TANX"] = "res://scenes/events/background_scenes/tanx.tscn",
        ["VAKUU"] = "res://scenes/events/background_scenes/vakuu.tscn",
        ["NEOW"] = "res://scenes/events/background_scenes/neow.tscn",
    };

    private static readonly AncientSceneConfig DefaultAncientSceneConfig =
        new(Vector2.Zero, 1.18f, new Vector2(0.5f, 0.06f), new Vector2(0f, -0.02f));

    private static readonly Dictionary<string, AncientSceneConfig> AncientSceneConfigs = new()
    {
        ["DARV"] = DefaultAncientSceneConfig,
        ["OROBAS"] = new AncientSceneConfig(Vector2.Zero, 1.24f, new Vector2(0.39f, 0.08f), new Vector2(-0.06f, -0.01f)),
        ["PAEL"] = new AncientSceneConfig(Vector2.Zero, 1.52f, new Vector2(0.50f, 0.03f), new Vector2(0f, -0.01f)),
        ["TEZCATARA"] = new AncientSceneConfig(Vector2.Zero, 1.22f, new Vector2(0.58f, 0.06f), new Vector2(0.06f, -0.01f)),
        ["NONUPEIPE"] = DefaultAncientSceneConfig,
        ["TANX"] = DefaultAncientSceneConfig,
        ["VAKUU"] = DefaultAncientSceneConfig,
        ["NEOW"] = DefaultAncientSceneConfig,
    };

    private static readonly string VoteButtonTexturePath = "res://images/packed/common_ui/event_button.png";
    private static readonly string VoteButtonOutlineTexturePath = "res://images/packed/common_ui/event_button_outline.png";
    private static readonly string VoteButtonFontPath = "res://themes/kreon_bold_glyph_space_one.tres";
    
    private static AudioStreamWav? _generatedHoverStream;
    private static AudioStreamWav? _generatedClickStream;
    private static Shader? _dialogueWaveShader;

    private readonly List<SlotRefs> _slots = new();
    private readonly List<Player> _orderedPlayers = new();
    private readonly Dictionary<ulong, int> _votesByPlayerNetId = new();
    private readonly TaskCompletionSource<bool> _readyCompletion = new();

    private TaskCompletionSource<int> _voteSubmitted = new();

    private IReadOnlyList<AncientEventModel> _pool = Array.Empty<AncientEventModel>();
    private VoteRoundType _roundType = VoteRoundType.InitialKeepVote;
    private Dictionary<string, AncientBanHelpers.AncientPreviewData> _previewDataByAncientId = new();
    private string? _suppressedPreviewAncientId; // ancient that does not reveal options, the initial vote
    private string? _reactionAncientId; // ancient that reacts with dialogue on the final preview vote
    private int _nextActIndex;
    private int? _pendingPoolIndex;
    private int? _selectedPoolIndex;
    private bool _resolved;
    private bool _closing;
    private bool _uiReady;
    private bool _hasLoadedRound;
    private Player? _localPlayer;
    private SlotRefs? _hoveredSlot;
    private int? _lastHoveredPoolIndex;
    private PreviewWidgetRefs? _hoveredPreviewWidget;
    private Player? _currentlyHighlightedVotePlayer;
    private int? _finalChosenPoolIndex;
    private int? _initialSecondRoundFocusPoolIndex;
    private float _initialSecondRoundWinnerEmphasisAmount;
    private Tween? _initialSecondRoundWinnerEmphasisFadeTween;

    private Control? _layoutRoot;
    private Control? _roundIntroAnchor;
    private MegaRichTextLabel? _roundIntroLabel;
    private Tween? _roundIntroTween;
    private Vector2 _roundIntroBasePosition;
    private RichTextAncientBanner? _roundIntroBannerEffect;

    public double RoundIntroDuration { get; set; } = 1.70;

    private const double RoundIntroFadeInDuration = 0.20;
    private const double RoundIntroFadeOutDuration = 0.45;
    private Control? _stageArea;
    private Control? _slotsCanvas;
    private AudioStreamPlayer? _hoverSfx;
    private AudioStreamPlayer? _clickSfx;
    private bool _lastShowOnlyButtonOutline;
    private bool _lastShowControllerHotkeys;
    private ChooseTheAncientConfig.VoteClickTargetMode _lastVoteClickTarget;

    public NetScreenType ScreenType => NetScreenType.Rewards;
    public bool UseSharedBackstop => true;

    // ModConfig variables
    public bool ShowControllerHotkeys { get; set; } = ChooseTheAncientConfig.ShowControllerHotkeys;
    public bool ShowOnlyButtonOutline { get; set; } = ChooseTheAncientConfig.ShowOnlyButtonOutline;
    private ChooseTheAncientConfig.VoteClickTargetMode VoteClickTarget { get; set; } = ChooseTheAncientConfig.VoteClickTarget;

    public Control? DefaultFocusedControl { get; private set; }

    public AncientBanSelectionScreen()
    {
        Name = "AncientBanSelectionScreen";
        ProcessMode = ProcessModeEnum.Always;
        MouseFilter = MouseFilterEnum.Ignore;
        FocusMode = FocusModeEnum.All;
        SetFullRect(this);
    }

    public static AncientBanSelectionScreen Show(int nextActIndex, IReadOnlyList<Player> orderedPlayers)
    {
        AncientBanSelectionScreen screen = new();
        screen.Initialize(nextActIndex, orderedPlayers);
        NOverlayStack.Instance.Push(screen);
        return screen;
    }

    public void Initialize(int nextActIndex, IReadOnlyList<Player> orderedPlayers)
    {
        _nextActIndex = nextActIndex;
        _orderedPlayers.Clear();
        _orderedPlayers.AddRange(orderedPlayers);
        _localPlayer = _orderedPlayers.FirstOrDefault(LocalContext.IsMe);
    }

    public async Task<int> RunRoundAsync(RoundDefinition round)
    {
        /* Handles each of the vote rounds for the selection screen */
        await _readyCompletion.Task;

        _voteSubmitted = new TaskCompletionSource<int>();
        _pool = round.Pool;
        _roundType = round.RoundType;
        _previewDataByAncientId = round.PreviewDataByAncientId?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
            ?? new Dictionary<string, AncientBanHelpers.AncientPreviewData>();
        _suppressedPreviewAncientId = round.SuppressedPreviewAncientId;
        _reactionAncientId = round.ReactionAncientId;
        _pendingPoolIndex = null;
        _selectedPoolIndex = null;
        _finalChosenPoolIndex = null;
        _currentlyHighlightedVotePlayer = null;
        _votesByPlayerNetId.Clear();
        _resolved = false;
        _hoveredSlot = null;
        _lastHoveredPoolIndex = null;
        _initialSecondRoundFocusPoolIndex = null;
        _initialSecondRoundWinnerEmphasisFadeTween?.Kill();
        _initialSecondRoundWinnerEmphasisFadeTween = null;
        _initialSecondRoundWinnerEmphasisAmount = 0f;

        await ApplyRoundAsync();
        return await _voteSubmitted.Task;
    }
    
    private static readonly List<AncientBanSelectionScreen> _openScreens = new();

    public static void RefreshModConfigHotkeys()
    {
        for (int i = _openScreens.Count - 1; i >= 0; i--)
        {
            AncientBanSelectionScreen screen = _openScreens[i];
            if (!GodotObject.IsInstanceValid(screen))
            {
                _openScreens.RemoveAt(i);
                continue;
            }

            screen.RefreshModConfigValues();
        }
    }

    public void InitiateConfigValues()
    {
        _lastShowControllerHotkeys = ShowControllerHotkeys;
        _lastShowOnlyButtonOutline = ShowOnlyButtonOutline;
        _lastVoteClickTarget = VoteClickTarget;
    }

    public void RefreshModConfigValues()
    {
        ShowControllerHotkeys = ChooseTheAncientConfig.ShowControllerHotkeys;
        ShowOnlyButtonOutline = ChooseTheAncientConfig.ShowOnlyButtonOutline;
        VoteClickTarget = ChooseTheAncientConfig.VoteClickTarget;
    }

    public override void _Process(double delta)
    {
        if (!_uiReady || !Visible)
            return;

        ChooseTheAncientConfig.RefreshFromModConfig();
        RefreshModConfigValues();

        if (_lastShowControllerHotkeys != ShowControllerHotkeys ||
            _lastShowOnlyButtonOutline != ShowOnlyButtonOutline ||
            _lastVoteClickTarget != VoteClickTarget)
        {
            RebuildForLiveConfigRefresh();

            _lastShowControllerHotkeys = ShowControllerHotkeys;
            _lastShowOnlyButtonOutline = ShowOnlyButtonOutline;
            _lastVoteClickTarget = VoteClickTarget;
        }

        UpdateWholeSlotHoverFromMouse();
    }

    private void SyncConfigFromSavedSettings()
    {
        ChooseTheAncientConfig.RefreshFromModConfig();
        ShowControllerHotkeys = ChooseTheAncientConfig.ShowControllerHotkeys;
        ShowOnlyButtonOutline = ChooseTheAncientConfig.ShowOnlyButtonOutline;
        VoteClickTarget = ChooseTheAncientConfig.VoteClickTarget;
    }

    private void RebuildForLiveConfigRefresh()
    {
        BuildUi();
        ApplySecondVotePresentation(animate: false);
        RefreshLayout();
        RefreshSlotVisuals(animate: false);
        RefreshButtonTexts();
        RefreshVoteDisplays(animate: false);
        GrabInitialFocus();
    }
    
    public override void _Ready()
    {
        SyncConfigFromSavedSettings();
        PackedScene? layoutScene = GD.Load<PackedScene>(LayoutScenePath);
        if (layoutScene == null)
        {
            throw new InvalidOperationException($"Could not load layout scene: {LayoutScenePath}");
        }

        _layoutRoot = layoutScene.Instantiate<Control>();
        SetFullRect(_layoutRoot);
        AddChild(_layoutRoot);

        _roundIntroAnchor = _layoutRoot.GetNode<Control>("RoundIntroOverlay/RoundIntroAnchor");
        _roundIntroBasePosition = _roundIntroAnchor.Position;

        Node? existingIntro = _roundIntroAnchor.GetNodeOrNull("RoundIntroLabel");
        existingIntro?.QueueFree();

        Font regularFont = GD.Load<Font>("res://themes/kreon_regular_glyph_space_one.tres")
            ?? throw new InvalidOperationException("Could not load kreon regular font.");
        Font boldFont = GD.Load<Font>("res://themes/kreon_bold_glyph_space_one.tres")
            ?? throw new InvalidOperationException("Could not load kreon bold font.");
        Font italicFont = GD.Load<Font>("res://themes/bitter_medium_italic_glyph_space_one.tres")
            ?? throw new InvalidOperationException("Could not load bitter italic font.");

        _roundIntroLabel = new MegaRichTextLabel
        {
            Name = "RoundIntroLabel",
            Visible = false,
            Modulate = new Color(1f, 1f, 1f, 0f),
            BbcodeEnabled = true,
            FitContent = false,
            ScrollActive = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutoSizeEnabled = true,
            MinFontSize = 42,
            MaxFontSize = 88,
            IsHorizontallyBound = true,
            IsVerticallyBound = true,
            MouseFilter = MouseFilterEnum.Ignore
        };

        _roundIntroLabel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        _roundIntroLabel.AddThemeFontOverride(RtNormalFont, regularFont);
        _roundIntroLabel.AddThemeFontOverride(RtBoldFont, boldFont);
        _roundIntroLabel.AddThemeFontOverride(RtItalicsFont, italicFont);

        _roundIntroLabel.AddThemeFontSizeOverride(RtNormalFontSize, 88);
        _roundIntroLabel.AddThemeFontSizeOverride(RtBoldFontSize, 88);
        _roundIntroLabel.AddThemeFontSizeOverride(RtBoldItalicsFontSize, 88);
        _roundIntroLabel.AddThemeFontSizeOverride(RtItalicsFontSize, 88);
        _roundIntroLabel.AddThemeFontSizeOverride(RtMonoFontSize, 88);

        _roundIntroLabel.AddThemeColorOverride(RtDefaultColor, Colors.White);
        _roundIntroLabel.AddThemeColorOverride(RtOutlineColor, Colors.Transparent);
        _roundIntroLabel.AddThemeColorOverride(RtShadowColor, Colors.Transparent);

        _roundIntroAnchor.AddChild(_roundIntroLabel);

        _stageArea = _layoutRoot.GetNode<Control>("StageMargin/StageArea");
        _slotsCanvas = _layoutRoot.GetNode<Control>("StageMargin/StageArea/SlotsCanvas");
        _hoverSfx = _layoutRoot.GetNodeOrNull<AudioStreamPlayer>("HoverSfx");
        _clickSfx = _layoutRoot.GetNodeOrNull<AudioStreamPlayer>("ClickSfx");

        if (_clickSfx != null)
        {
            _clickSfx.VolumeDb = -16f;
        }

        _layoutRoot.MouseFilter = MouseFilterEnum.Ignore;

        TryInstallGeneratedSounds();

        _stageArea.ClipContents = true;
        _slotsCanvas.ClipContents = false;
        _stageArea.MouseFilter = MouseFilterEnum.Pass;
        _slotsCanvas.MouseFilter = MouseFilterEnum.Ignore;
        _stageArea.GuiInput += OnStageAreaGuiInput;
        _stageArea.Resized += RefreshLayout;
        ConnectControllerPromptSignals();
        UpdateVoteButtonControllerIcons();
        
        InitiateConfigValues();
        RefreshModConfigValues();

        _uiReady = true;
        _readyCompletion.TrySetResult(true);
    } 
    

    private async Task ApplyRoundAsync()
    { 
        /*
         * Has two branches. One for if round has not loaded.
         * And the other that transitions the ancients in and out.
         */
        if (!_uiReady)
        {
            return;
        }

        // Copy the Font style from Ancient Banner
        //AncientEventModel? sampleAncient = _slots.Count > 0 ? _slots[0].Ancient : null;
        SyncConfigFromSavedSettings();
        
        if (!_hasLoadedRound)
        {
            BuildUi();
            _hasLoadedRound = true;
            ShowRoundIntro();

            if (_roundType == VoteRoundType.FinalRevealVote)
            {
                PrimeFinalRoundElementAnimation();
                CallDeferred(nameof(StartFinalRoundElementAnimation));
            }

            return;
        }

        await AnimateOutSlotsAsync();
        BuildUi();
        PrimeSlotsForTransitionIn();
        ShowRoundIntro();

        if (_roundType == VoteRoundType.FinalRevealVote)
        {
            PrimeFinalRoundElementAnimation();
        }

        await AnimateInSlotsAsync();
    } 

    private async Task AnimateOutSlotsAsync()
    {
        /* old slots slide slightly upward/outward and fade out. */
        if (_slots.Count == 0)
        {
            return;
        }

        Tween tween = CreateTween();
        tween.SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.In);

        for (int i = 0; i < _slots.Count; i++)
        {
            SlotRefs refs = _slots[i];
            float horizontalOffset = (i - ((_slots.Count - 1) * 0.5f)) * 120f;
            float verticalOffset = -32f;

            tween.Parallel().TweenProperty(refs.SlotRoot, "position", new Vector2(horizontalOffset, verticalOffset), 0.16f);
            tween.Parallel().TweenProperty(refs.SlotRoot, "modulate:a", 0f, 0.16f);
        }

        await ToSignal(tween, Tween.SignalName.Finished);
    }

    private void PrimeSlotsForTransitionIn()
    {
        /* → places the new slot roots off to the sides and invisible so they are ready to tween in after they've been moved out*/
        for (int i = 0; i < _slots.Count; i++)
        {
            SlotRefs refs = _slots[i];
            float horizontalOffset = (i == 0) ? -200f : 200f;
            if (_slots.Count == 3)
            {
                horizontalOffset = (i - 1) * 180f;
            }

            refs.SlotRoot.Position = new Vector2(horizontalOffset, 0f);
            refs.SlotRoot.Modulate = new Color(1f, 1f, 1f, 0f);
        }
    }

    private async Task AnimateInSlotsAsync()
    {
        /*
         Slot slide/fade into place. After that tween finishes, if it’s FinalRevealVote, it directly calls the ancient preview animation.
         */
        if (_slots.Count == 0)
        {
            return;
        }

        Tween tween = CreateTween();
        tween.SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);

        foreach (SlotRefs refs in _slots)
        {
            tween.Parallel().TweenProperty(refs.SlotRoot, "position", Vector2.Zero, 0.22f);
            tween.Parallel().TweenProperty(refs.SlotRoot, "modulate:a", 1f, 0.22f);
        }

        await ToSignal(tween, Tween.SignalName.Finished);

        if (_roundType == VoteRoundType.FinalRevealVote)
        {
            StartFinalRoundElementAnimation();
        }

        GrabInitialFocus();
    }

private string GetRoundIntroText()
{
    string actLabel = _nextActIndex == 1 ? "Act 2" : "Act 3";

    return _roundType == VoteRoundType.InitialKeepVote
        ? $"Choose the {actLabel} Ancients"
        : "It's Not Over";
}

private void ShowRoundIntro()
{
    if (_roundIntroAnchor == null || _roundIntroLabel == null)
    {
        return;
    }

    _roundIntroTween?.Kill();

    double holdDuration = Math.Max(0.0, RoundIntroDuration - RoundIntroFadeInDuration - RoundIntroFadeOutDuration);

    SetRoundIntroTextStyled(GetRoundIntroText());

    _roundIntroLabel.Visible = true;
    _roundIntroLabel.Modulate = new Color(1f, 1f, 1f, 0f);

    _roundIntroAnchor.Position = _roundIntroBasePosition + new Vector2(0f, 12f);
    _roundIntroAnchor.Scale = new Vector2(1.02f, 1.02f);

    Tween tween = CreateTween();
    _roundIntroTween = tween;

    tween.SetParallel();

    tween.TweenProperty(_roundIntroLabel, "modulate:a", 1f, RoundIntroFadeInDuration)
        .SetEase(Tween.EaseType.Out)
        .SetTrans(Tween.TransitionType.Cubic);

    tween.TweenProperty(_roundIntroAnchor, "position:y", _roundIntroBasePosition.Y, 0.30)
        .SetEase(Tween.EaseType.Out)
        .SetTrans(Tween.TransitionType.Circ);

    tween.TweenProperty(_roundIntroAnchor, "scale", Vector2.One, 0.30)
        .SetEase(Tween.EaseType.Out)
        .SetTrans(Tween.TransitionType.Circ);

    if (_roundIntroBannerEffect != null)
    {
        tween.TweenMethod(
                Callable.From<float>(value => _roundIntroBannerEffect.Rotation = value),
                _roundIntroBannerEffect.Rotation,
                1f,
                0.75f)
            .SetEase(Tween.EaseType.Out)
            .SetTrans(Tween.TransitionType.Spring);

        tween.TweenMethod(
                Callable.From<float>(value => _roundIntroBannerEffect.Spacing = value),
                _roundIntroBannerEffect.Spacing,
                0f,
                0.75f)
            .SetEase(Tween.EaseType.Out)
            .SetTrans(Tween.TransitionType.Expo);
    }

    tween.Chain();
    tween.TweenInterval(holdDuration);

    tween.Chain();
    tween.TweenProperty(_roundIntroLabel, "modulate:a", 0f, RoundIntroFadeOutDuration)
        .SetEase(Tween.EaseType.In)
        .SetTrans(Tween.TransitionType.Cubic);

    tween.Parallel().TweenProperty(_roundIntroAnchor, "position:y", _roundIntroBasePosition.Y - 10f, RoundIntroFadeOutDuration)
        .SetEase(Tween.EaseType.In)
        .SetTrans(Tween.TransitionType.Circ);

    tween.Chain();
    tween.TweenCallback(Callable.From(() =>
    {
        if (_roundIntroLabel == null || _roundIntroAnchor == null)
        {
            return;
        }

        _roundIntroLabel.Visible = false;
        _roundIntroLabel.Modulate = Colors.White;
        _roundIntroAnchor.Position = _roundIntroBasePosition;
        _roundIntroAnchor.Scale = Vector2.One;
    }));
}

    private void PrimeFinalRoundElementAnimation()
    {
        /* Readies the ancient preview elements to animation by setting alpha to 0, and putting down. */
        if (_roundType != VoteRoundType.FinalRevealVote)
        {
            return;
        }

        foreach (SlotRefs refs in _slots)
        {
            if (refs.ReactionBubble != null)
            {
                refs.ReactionBubble.Modulate = new Color(1f, 1f, 1f, 0f);
                refs.ReactionBubble.Position += new Vector2(0f, ReactionEntranceOffset);
                refs.ReactionBubble.Scale *= new Vector2(0.975f, 0.975f);

                Control? icon = refs.ReactionBubble.GetNodeOrNull<Control>("LineRoot/AncientIcon");
                if (icon != null)
                {
                    icon.Modulate = new Color(1f, 1f, 1f, 0f);
                    icon.Position += new Vector2(0f, 8f);
                }

                Control? text = refs.ReactionBubble.GetNodeOrNull<Control>("LineRoot/DialogueContainer/TextContainer/TextBox/LineText");
                if (text != null)
                {
                    text.Modulate = new Color(1f, 1f, 1f, 0f);
                    text.Position += new Vector2(0f, 8f);
                }
            }

            if (refs.PreviewAnchor.Visible)
            {
                foreach (PreviewWidgetRefs widget in refs.PreviewWidgets)
                {
                    widget.Wrapper.Modulate = new Color(1f, 1f, 1f, 0f);
                    widget.Wrapper.Position += new Vector2(0f, PreviewEntranceOffset);
                    widget.Wrapper.Scale *= new Vector2(0.982f, 0.982f);
                }
            }
        }
    }

    private void StartFinalRoundElementAnimation()
    {
        /* animation the ancient preview animation */   
        if (_roundType != VoteRoundType.FinalRevealVote)
        {
            return;
        }

        double delay = 0.0;

        foreach (SlotRefs refs in _slots)
        {
            if (refs.ReactionBubble != null)
            {
                Control bubble = refs.ReactionBubble;
                Tween bubbleTween = CreateTween();
                bubbleTween.SetTrans(Tween.TransitionType.Expo).SetEase(Tween.EaseType.Out);
                bubbleTween.TweenInterval(delay);
                bubbleTween.TweenProperty(bubble, "modulate:a", 1f, ReactionEntranceDuration);
                bubbleTween.Parallel().TweenProperty(bubble, "position", bubble.Position + new Vector2(0f, -ReactionEntranceOffset), ReactionEntranceDuration);
                bubbleTween.Parallel().TweenProperty(bubble, "scale", bubble.Scale / new Vector2(0.975f, 0.975f), ReactionEntranceDuration);

                Control? icon = bubble.GetNodeOrNull<Control>("LineRoot/AncientIcon");
                if (icon != null)
                {
                    Tween iconTween = CreateTween();
                    iconTween.SetTrans(Tween.TransitionType.Expo).SetEase(Tween.EaseType.Out);
                    iconTween.TweenInterval(delay + 0.18);
                    iconTween.TweenProperty(icon, "modulate:a", 1f, 0.36f);
                    iconTween.Parallel().TweenProperty(icon, "position", icon.Position + new Vector2(0f, -8f), 0.36f);
                }

                Control? text = bubble.GetNodeOrNull<Control>("LineRoot/DialogueContainer/TextContainer/TextBox/LineText");
                if (text != null)
                {
                    Tween textTween = CreateTween();
                    textTween.SetTrans(Tween.TransitionType.Expo).SetEase(Tween.EaseType.Out);
                    textTween.TweenInterval(delay + 0.10);
                    textTween.TweenProperty(text, "modulate:a", 1f, ReactionTextDuration);
                    textTween.Parallel().TweenProperty(text, "position", text.Position + new Vector2(0f, -8f), ReactionTextDuration);
                }

                StartReactionWave(bubble);
                delay += FinalRoundStagger;
            }

            if (refs.PreviewAnchor.Visible)
            {
                foreach (PreviewWidgetRefs widget in refs.PreviewWidgets)
                {
                    Tween tween = CreateTween();
                    tween.SetTrans(Tween.TransitionType.Expo).SetEase(Tween.EaseType.Out);
                    tween.TweenInterval(delay);
                    tween.TweenProperty(widget.Wrapper, "modulate:a", 1f, PreviewEntranceDuration);
                    tween.Parallel().TweenProperty(widget.Wrapper, "position", widget.Wrapper.Position + new Vector2(0f, -PreviewEntranceOffset), PreviewEntranceDuration);
                    tween.Parallel().TweenProperty(widget.Wrapper, "scale", widget.Wrapper.Scale / new Vector2(0.982f, 0.982f), PreviewEntranceDuration);
                    delay += FinalRoundStagger;
                }
            }
        }
    }

    private float GetPreviewListStartY(SlotRefs refs, Vector2 anchorSize)
    {
        if (refs.PreviewWidgets.Count == 0)
        {
            return 0f;
        }

        float displayHeight = 70f;
        float gap = 8f;
        float totalHeight = (refs.PreviewWidgets.Count * displayHeight) + (Math.Max(0, refs.PreviewWidgets.Count - 1) * gap);

        float reserveTop = 0f;
        if (refs.ReactionBubble != null)
        {
            reserveTop = ReactionBubbleHeight + ReactionBubbleGap;
        }

        return MathF.Max(reserveTop, anchorSize.Y - totalHeight);
    }

    private void BuildUi()
    {
        if (_slotsCanvas == null)
        {
            throw new InvalidOperationException("Layout scene was missing StageMargin/StageArea/SlotsCanvas.");
        }

        PackedScene? cardScene = GD.Load<PackedScene>(CardScenePath);
        if (cardScene == null)
        {
            throw new InvalidOperationException($"Could not load card scene: {CardScenePath}");
        }

        if (_hoveredPreviewWidget != null)
        {
            NHoverTipSet.Remove(_hoveredPreviewWidget.Wrapper);
            _hoveredPreviewWidget = null;
        }

        ClearChildren(_slotsCanvas);
        _slots.Clear();
        DefaultFocusedControl = null;
        
        SlotRefs? preferredFocusRefs = null;

        for (int i = 0; i < _pool.Count; i++)
        {
            AncientEventModel ancient = _pool[i];
            SlotRefs refs = CreateSlot(ancient, i, cardScene);
            _slotsCanvas.AddChild(refs.SlotRoot);
            _slots.Add(refs);
            if (_suppressedPreviewAncientId == refs.Ancient.Id.Entry)
            {
                preferredFocusRefs = refs;
            }
            DefaultFocusedControl ??= refs.ChooseButtonWrap;
            LoadAncientScene(refs);
            PopulatePreview(refs);
        }

        if (preferredFocusRefs != null && _roundType == VoteRoundType.FinalRevealVote)
        {
            DefaultFocusedControl = preferredFocusRefs.ChooseButtonWrap;

            // Optional: start the second screen visually "on" that card too.
            _hoveredSlot = preferredFocusRefs;
            _lastHoveredPoolIndex = preferredFocusRefs.PoolIndex;
            _initialSecondRoundFocusPoolIndex = preferredFocusRefs.PoolIndex;
            SetInitialSecondRoundWinnerEmphasisAmount(1f);
        }
        
        RefreshLayout();
        ApplySecondVotePresentation(animate: false);
        RefreshLayout();
        RefreshSlotVisuals(animate: false);
        RefreshButtonTexts();
        ConfigureControllerNavigation();
        UpdateVoteButtonControllerIcons();
        RefreshVoteDisplays(animate: false);
        GrabInitialFocus();
    }

    private static void ApplyCardOutlineLook(Panel outline)
    {
        StyleBoxFlat sb = new()
        {
            BgColor = new Color(0f, 0f, 0f, 0f),
            DrawCenter = false,
            BorderWidthLeft = 4,
            BorderWidthTop = 4,
            BorderWidthRight = 4,
            BorderWidthBottom = 4,
            BorderColor = Colors.White,
            CornerRadiusTopLeft = 12,
            CornerRadiusTopRight = 12,
            CornerRadiusBottomRight = 12,
            CornerRadiusBottomLeft = 12
        };

        outline.AddThemeStyleboxOverride("panel", sb);
    }

    private static void SetMouseFilterRecursive(
        Control root,
        MouseFilterEnum mouseFilter,
        Func<Control, bool>? shouldSkip = null)
    {
        if (shouldSkip?.Invoke(root) != true)
        {
            root.MouseFilter = mouseFilter;
        }

        foreach (Node child in root.GetChildren())
        {
            if (child is Control childControl)
            {
                SetMouseFilterRecursive(childControl, mouseFilter, shouldSkip);
            }
        }
    }

    private static Vector2[] BuildPortalPolygon(PortalShape shape)
    {
        return
        [
            shape.TopLeft,
            shape.TopRight,
            shape.BottomRight,
            shape.BottomLeft,
        ];
    }

    private bool TryGetStageLocalMousePosition(out Vector2 stageLocalMouse)
    {
        stageLocalMouse = Vector2.Zero;

        if (_stageArea == null)
        {
            return false;
        }

        Vector2 viewportMouse = GetViewport().GetMousePosition();
        if (!_stageArea.GetGlobalRect().HasPoint(viewportMouse))
        {
            return false;
        }

        stageLocalMouse = _stageArea.GetLocalMousePosition();
        return true;
    }

    private static bool IsPointInsidePortalShape(PortalShape shape, Vector2 stagePoint)
    {
        return Geometry2D.IsPointInPolygon(stagePoint, BuildPortalPolygon(shape));
    }

    private int? GetWholeSlotPoolIndexAtStagePoint(Vector2 stagePoint)
    {
        foreach (SlotRefs refs in _slots)
        {
            if (IsPointInsidePortalShape(refs.Shape, stagePoint))
            {
                return refs.PoolIndex;
            }
        }

        return null;
    }

    private bool IsMouseOverWholeSlot(SlotRefs refs)
    {
        return TryGetStageLocalMousePosition(out Vector2 stagePoint) &&
               IsPointInsidePortalShape(refs.Shape, stagePoint);
    }

    private void UpdateWholeSlotHoverFromMouse()
    {
        if (VoteClickTarget != ChooseTheAncientConfig.VoteClickTargetMode.WholeSlot)
        {
            return;
        }

        if (_resolved)
        {
            return;
        }

        if (!TryGetStageLocalMousePosition(out Vector2 stagePoint))
        {
            if (_hoveredSlot != null)
            {
                ClearInitialSecondRoundWinnerEmphasisIfFocusChanged(null);
                _hoveredSlot = null;
                _lastHoveredPoolIndex = null;
                RefreshSlotVisuals(animate: true);
                RefreshAllVoteButtonOutlines();
            }

            return;
        }

        int? hoveredPoolIndex = GetWholeSlotPoolIndexAtStagePoint(stagePoint);
        if (!hoveredPoolIndex.HasValue)
        {
            if (_hoveredSlot != null)
            {
                ClearInitialSecondRoundWinnerEmphasisIfFocusChanged(null);
                _hoveredSlot = null;
                _lastHoveredPoolIndex = null;
                RefreshSlotVisuals(animate: true);
                RefreshAllVoteButtonOutlines();
            }

            return;
        }

        OnSlotHovered(hoveredPoolIndex.Value);
    }

    private void OnStageAreaGuiInput(InputEvent @event)
    {
        if (VoteClickTarget != ChooseTheAncientConfig.VoteClickTargetMode.WholeSlot)
        {
            return;
        }

        if (_resolved || _closing)
        {
            return;
        }

        if (@event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
        {
            return;
        }

        if (!TryGetStageLocalMousePosition(out Vector2 stagePoint))
        {
            return;
        }

        int? poolIndex = GetWholeSlotPoolIndexAtStagePoint(stagePoint);
        if (!poolIndex.HasValue)
        {
            return;
        }

        Select(poolIndex.Value);
        AcceptEvent();
    }

    private Control CreateVoteClickTarget(string name, int poolIndex, bool isSlotTarget)
    {
        Control clickTarget = new()
        {
            Name = name,
            MouseFilter = MouseFilterEnum.Ignore,
            FocusMode = FocusModeEnum.None,
        };

        SetFullRect(clickTarget);
        clickTarget.GuiInput += @event => OnVoteClickTargetGuiInput(poolIndex, isSlotTarget, @event);
        clickTarget.MouseEntered += () => OnSlotHovered(poolIndex);
        clickTarget.MouseExited += () => OnSlotUnhovered(poolIndex);
        return clickTarget;
    }

    private void OnVoteClickTargetGuiInput(int poolIndex, bool isSlotTarget, InputEvent @event)
    {
        if (_resolved)
        {
            return;
        }

        if (@event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
        {
            return;
        }

        if (isSlotTarget)
        {
            if (VoteClickTarget != ChooseTheAncientConfig.VoteClickTargetMode.WholeSlot)
            {
                return;
            }
        }
        else if (VoteClickTarget == ChooseTheAncientConfig.VoteClickTargetMode.ButtonOnly)
        {
            return;
        }

        Select(poolIndex);
        AcceptEvent();
    }

    private void ApplyVoteClickTargetMode(SlotRefs refs)
    {
        refs.CardClickTarget.MouseFilter = VoteClickTarget == ChooseTheAncientConfig.VoteClickTargetMode.ButtonOnly
            ? MouseFilterEnum.Pass
            : MouseFilterEnum.Stop;

        // Whole-slot hit testing is handled against the actual portal polygon from the shared stage area.
        // That keeps hover/click exactly inside the visible slot seams instead of using overlapping rectangles.
        refs.SlotClickTarget.MouseFilter = MouseFilterEnum.Ignore;
    }

    private static void ApplyVoteButtonLook(
        Button chooseButton,
        NinePatchRect chooseButtonOutline, bool bodyVisible)
    {
        Texture2D buttonTexture = GD.Load<Texture2D>(VoteButtonTexturePath)
            ?? throw new InvalidOperationException($"Could not load {VoteButtonTexturePath}");
        Texture2D outlineTexture = GD.Load<Texture2D>(VoteButtonOutlineTexturePath)
            ?? throw new InvalidOperationException($"Could not load {VoteButtonOutlineTexturePath}");
        Font buttonFont = GD.Load<Font>(VoteButtonFontPath)
            ?? throw new InvalidOperationException($"Could not load {VoteButtonFontPath}");

        // Style the button to have the game's event button texture
        StyleBoxTexture normal = new()
        {
            Texture = buttonTexture,
            TextureMarginLeft = 0f,
            TextureMarginTop = 0f,
            TextureMarginRight = 0f,
            TextureMarginBottom = 0f,
            AxisStretchHorizontal = StyleBoxTexture.AxisStretchMode.Stretch,
            AxisStretchVertical = StyleBoxTexture.AxisStretchMode.Stretch,
            ModulateColor = bodyVisible ? Colors.White : new Color(1f, 1f, 1f, 0f)
        };

        StyleBoxTexture hover = (StyleBoxTexture)normal.Duplicate();
        hover.ModulateColor = bodyVisible ? Colors.White : new Color(1f, 1f, 1f, 0f);

        StyleBoxTexture pressed = (StyleBoxTexture)normal.Duplicate();
        pressed.ModulateColor = bodyVisible
            ? new Color(0.92f, 0.92f, 0.92f, 1f)
            : new Color(1f, 1f, 1f, 0f);

        StyleBoxTexture disabled = (StyleBoxTexture)normal.Duplicate();
        disabled.ModulateColor = bodyVisible
            ? new Color(0.70f, 0.70f, 0.70f, 0.90f)
            : new Color(1f, 1f, 1f, 0f);

        chooseButtonOutline.Texture = outlineTexture;
        chooseButtonOutline.PatchMarginLeft = 0;
        chooseButtonOutline.PatchMarginTop = 18;
        chooseButtonOutline.PatchMarginRight = 0;
        chooseButtonOutline.PatchMarginBottom = 18;
        chooseButtonOutline.Modulate = new Color(1f, 1f, 1f, 0f);

        chooseButton.AddThemeStyleboxOverride("normal", normal);
        chooseButton.AddThemeStyleboxOverride("hover", normal);
        chooseButton.AddThemeStyleboxOverride("pressed", normal);
        chooseButton.AddThemeStyleboxOverride("focus", normal);
        chooseButton.AddThemeStyleboxOverride("disabled", normal);

        chooseButton.CustomMinimumSize = new Vector2(0f, 72f);
        chooseButton.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        chooseButton.Alignment = HorizontalAlignment.Center;

        chooseButton.AddThemeFontOverride("font", buttonFont);
        chooseButton.AddThemeFontSizeOverride("font_size", 24);
        chooseButton.AddThemeColorOverride("font_color", Colors.White);
        chooseButton.AddThemeColorOverride("font_hover_color", Colors.White);
        chooseButton.AddThemeColorOverride("font_pressed_color", Colors.White);
        chooseButton.AddThemeColorOverride("font_focus_color", Colors.White);
        chooseButton.AddThemeColorOverride("font_disabled_color", new Color(0.78f, 0.78f, 0.78f, 1f));
        chooseButton.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.70f));
        chooseButton.AddThemeConstantOverride("outline_size", 6);

    }
    
    private SlotRefs CreateSlot(AncientEventModel ancient, int poolIndex, PackedScene cardScene)
    {
        Color accentColor = GetAccentColor(ancient.Id.Entry, poolIndex);

        Control slotRoot = new()
        {
            Name = $"AncientSlot_{ancient.Id.Entry}",
            MouseFilter = MouseFilterEnum.Ignore,
            ClipContents = false,
            FocusMode = FocusModeEnum.None,
        };

        SubViewport sceneViewport = new()
        {
            Name = "SceneViewport",
            Disable3D = true,
            TransparentBg = false,
            HandleInputLocally = false,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            RenderTargetClearMode = SubViewport.ClearMode.Always,
        };
        slotRoot.AddChild(sceneViewport);

        Control sceneMount = new()
        {
            Name = "SceneMount",
            MouseFilter = MouseFilterEnum.Ignore,
            FocusMode = FocusModeEnum.None,
            ZIndex = 0,
        };
        SetFullRect(sceneMount);
        sceneViewport.AddChild(sceneMount);

        Polygon2D scenePolygon = new()
        {
            Name = "ScenePolygon",
            Texture = sceneViewport.GetTexture(),
            Antialiased = true,
            ZIndex = 0,
        };
        slotRoot.AddChild(scenePolygon);

        Polygon2D glowPolygon = new()
        {
            Name = "GlowPolygon",
            Color = new Color(accentColor.R, accentColor.G, accentColor.B, 0f),
            Antialiased = true,
            ZIndex = 1,
        };
        slotRoot.AddChild(glowPolygon);

        Polygon2D hoverFlashPolygon = new()
        {
            Name = "HoverFlashPolygon",
            Color = new Color(1f, 1f, 1f, 0f),
            Antialiased = true,
            ZIndex = 2,
        };
        slotRoot.AddChild(hoverFlashPolygon);

        Polygon2D leftRim = new()
        {
            Name = "LeftRim",
            Color = new Color(accentColor.R, accentColor.G, accentColor.B, 0.55f),
            Antialiased = true,
            ZIndex = 1,
        };
        slotRoot.AddChild(leftRim);

        Polygon2D rightRim = new()
        {
            Name = "RightRim",
            Color = new Color(accentColor.R, accentColor.G, accentColor.B, 0.55f),
            Antialiased = true,
            ZIndex = 1,
        };
        slotRoot.AddChild(rightRim);

        Control slotClickTarget = CreateVoteClickTarget("SlotClickTarget", poolIndex, isSlotTarget: true);
        slotClickTarget.ZIndex = 1;
        slotClickTarget.LayoutMode = 0;
        slotClickTarget.AnchorLeft = 0f;
        slotClickTarget.AnchorTop = 0f;
        slotClickTarget.AnchorRight = 0f;
        slotClickTarget.AnchorBottom = 0f;
        slotRoot.AddChild(slotClickTarget);

        Control cardRoot = cardScene.Instantiate<Control>();
        cardRoot.Name = $"AncientChoice_{ancient.Id.Entry}";
        cardRoot.ClipContents = false;
        cardRoot.ZIndex = 2;
        slotRoot.AddChild(cardRoot);

        ColorRect cardShade = cardRoot.GetNode<ColorRect>("BottomShade");
        ColorRect topAccent = cardRoot.GetNode<ColorRect>("TopAccent");
        Panel cardOutline = cardRoot.GetNode<Panel>("CardOutline");
        TextureRect icon = cardRoot.GetNode<TextureRect>("Padding/VBox/Header/Icon");
        Label nameLabel = cardRoot.GetNode<Label>("Padding/VBox/Header/TextBox/NameLabel");
        Label epithetLabel = cardRoot.GetNode<Label>("Padding/VBox/Header/TextBox/EpithetLabel");
        Control chooseButtonWrap = cardRoot.GetNode<Control>("Padding/VBox/ChooseButtonWrap");
        Button chooseButton = cardRoot.GetNode<Button>("Padding/VBox/ChooseButtonWrap/ChooseButton");
        NinePatchRect chooseButtonOutline = cardRoot.GetNode<NinePatchRect>("Padding/VBox/ChooseButtonWrap/ChooseButtonOutline");
        TextureRect? chooseButtonControllerIcon = chooseButtonWrap.GetNodeOrNull<TextureRect>("ControllerIcon");
        Control previewAnchor = cardRoot.GetNode<Control>("PreviewAnchor");
        Control reactionAnchor = cardRoot.GetNode<Control>("ReactionAnchor");
        Control voteIconsAnchor = cardRoot.GetNode<Control>("VoteIconsAnchor");

        Control cardClickTarget = CreateVoteClickTarget("CardClickTarget", poolIndex, isSlotTarget: false);
        cardClickTarget.ZIndex = 0;
        cardRoot.AddChild(cardClickTarget);
        cardRoot.MoveChild(cardClickTarget, 3);

        SetMouseFilterRecursive(cardRoot, MouseFilterEnum.Ignore, control =>
            ReferenceEquals(control, chooseButton) || ReferenceEquals(control, cardClickTarget));

        ApplyVoteButtonLook(chooseButton, chooseButtonOutline, bodyVisible: !ShowOnlyButtonOutline);
        ApplyCardOutlineLook(cardOutline);

        chooseButtonWrap.FocusMode = FocusModeEnum.All;
        chooseButtonWrap.MouseFilter = MouseFilterEnum.Ignore;
        chooseButton.FocusMode = FocusModeEnum.None;
        chooseButton.MouseFilter = MouseFilterEnum.Stop;

        NMultiplayerVoteContainer? voteContainer = null;
        if (_orderedPlayers.Count > 1)
        {
            voteContainer = new NMultiplayerVoteContainer
            {
                Name = "VoteContainer",
                MouseFilter = MouseFilterEnum.Ignore,
                FocusMode = FocusModeEnum.None,
                ZIndex = 8,
            };
            SetFullRect(voteContainer);
            voteContainer.Initialize(
                player => _votesByPlayerNetId.TryGetValue(player.NetId, out int votedPoolIndex) && votedPoolIndex == poolIndex,
                _orderedPlayers);
            voteIconsAnchor.AddChild(voteContainer);
        }

        voteIconsAnchor.ZIndex = 8;
        previewAnchor.ZIndex = 3;
        reactionAnchor.ZIndex = 4;
        topAccent.Color = new Color(accentColor.R, accentColor.G, accentColor.B, 0.82f);
        icon.Texture = ancient.MapIcon;
        nameLabel.Text = ancient.Title.GetFormattedText();

        try
        {
            epithetLabel.Text = ancient.Epithet.GetFormattedText();
            epithetLabel.Visible = true;
        }
        catch
        {
            epithetLabel.Text = string.Empty;
            epithetLabel.Visible = false;
        }

        chooseButton.MouseEntered += () => OnSlotHovered(poolIndex);
        chooseButton.MouseExited += () => OnSlotUnhovered(poolIndex);
        chooseButtonWrap.FocusEntered += () => OnSlotHovered(poolIndex);
        chooseButtonWrap.FocusExited += () => OnSlotUnhovered(poolIndex);
        chooseButton.FocusEntered += () => OnSlotHovered(poolIndex);
        chooseButton.FocusExited += () => OnSlotUnhovered(poolIndex);

        int capturedIndex = poolIndex;
        chooseButton.Pressed += () => Select(capturedIndex);

        SlotRefs refs = new()
        {
            PoolIndex = poolIndex,
            Ancient = ancient,
            SlotRoot = slotRoot,
            SceneViewport = sceneViewport,
            SceneMount = sceneMount,
            ScenePolygon = scenePolygon,
            GlowPolygon = glowPolygon,
            HoverFlashPolygon = hoverFlashPolygon,
            LeftRim = leftRim,
            RightRim = rightRim,
            CardRoot = cardRoot,
            CardShade = cardShade,
            TopAccent = topAccent,
            CardOutline = cardOutline,
            Icon = icon,
            NameLabel = nameLabel,
            EpithetLabel = epithetLabel,
            ChooseButtonWrap = chooseButtonWrap,
            ChooseButton = chooseButton,
            CardClickTarget = cardClickTarget,
            SlotClickTarget = slotClickTarget,
            ChooseButtonControllerIcon = chooseButtonControllerIcon,
            ChooseButtonOutline = chooseButtonOutline,
            VoteIconsAnchor = voteIconsAnchor,
            PreviewAnchor = previewAnchor,
            ReactionAnchor = reactionAnchor,
            AccentColor = accentColor,
            VoteContainer = voteContainer,
            Shape = default,
        };

        ApplyVoteClickTargetMode(refs);
        return refs;
    }


    private void UpdateVoteButtonOutline(SlotRefs refs)
    {
        bool show =
            !_resolved &&
            !refs.ChooseButton.Disabled &&
            ReferenceEquals(_hoveredSlot, refs);

        refs.ChooseButtonOutline.Modulate = new Color(1f, 1f, 1f, show ? 1f : 0f);
    }
    
    private void RefreshAllVoteButtonOutlines()
    {
        foreach (SlotRefs refs in _slots)
        {
            UpdateVoteButtonOutline(refs);
        }
    }
    
    private void PopulatePreview(SlotRefs refs)
    {
        /* for this slot, create the little final-round option preview cards and register them for later layout, hover, and entrance animation. */
        ClearChildren(refs.PreviewAnchor);
        ClearChildren(refs.ReactionAnchor);
        refs.ReactionBubble = null;
        refs.PreviewWidgets.Clear();

        if (_roundType != VoteRoundType.FinalRevealVote)
        {
            refs.PreviewAnchor.Visible = false;
            return;
        }

        if (!_previewDataByAncientId.TryGetValue(refs.Ancient.Id.Entry, out AncientBanHelpers.AncientPreviewData? preview))
        {
            ModLog.Warn($"No preview data found for {refs.Ancient.Id.Entry}.");
            refs.PreviewAnchor.Visible = false;
            return;
        }

        ModLog.Debug($"Building preview UI for {refs.Ancient.Id.Entry} with {preview.Options.Count} option(s).");

        if (preview.Options.Count == 0)
        {
            refs.PreviewAnchor.Visible = false;
            return;
        }

        refs.PreviewAnchor.Visible = true;

        for (int i = 0; i < preview.Options.Count; i++)
        {
            EventOption option = preview.Options[i];

            Control previewWrapper = new()
            {
                Name = $"PreviewWrapper_{i}",
                MouseFilter = MouseFilterEnum.Stop,
                FocusMode = FocusModeEnum.All,
                ClipContents = false,
                ZIndex = 3,
                CustomMinimumSize = new Vector2(0f, 76f),
            };
            refs.PreviewAnchor.AddChild(previewWrapper);

            NEventOptionButton previewButton = NEventOptionButton.Create(preview.PreviewEvent, option, i);
            previewButton.MouseFilter = MouseFilterEnum.Ignore;
            previewButton.FocusMode = FocusModeEnum.None;
            previewButton.ProcessMode = ProcessModeEnum.Always;
            previewButton.ZIndex = 3;
            SetFullRect(previewButton);

            previewWrapper.AddChild(previewButton);

            Control? voteContainer = previewButton.GetNodeOrNull<Control>("PlayerVoteContainer");
            if (voteContainer != null)
            {
                voteContainer.Visible = false;
            }

            int previewIndex = i;
            previewWrapper.MouseEntered += () => OnPreviewHovered(refs, previewIndex);
            previewWrapper.MouseExited += () => OnPreviewUnhovered(refs, previewIndex);
            previewWrapper.FocusEntered += () => OnPreviewHovered(refs, previewIndex);
            previewWrapper.FocusExited += () => OnPreviewUnhovered(refs, previewIndex);
            NinePatchRect? previewOutline = previewButton.GetNodeOrNull<NinePatchRect>("Outline");
            if (previewOutline != null)
            {
                previewOutline.Modulate = new Color(previewOutline.Modulate.R, previewOutline.Modulate.G, previewOutline.Modulate.B, 0f);
            }

            ShaderMaterial? previewHsvMaterial = previewButton.GetNodeOrNull<NinePatchRect>("Image")?.Material as ShaderMaterial;
            previewHsvMaterial?.SetShaderParameter("v", 0.9f);

            refs.PreviewWidgets.Add(new PreviewWidgetRefs
            {
                Wrapper = previewWrapper,
                Button = previewButton,
                Option = option,
                HoverAlignment = refs.PoolIndex == 0 ? HoverTipAlignment.Right : HoverTipAlignment.Left,
                Outline = previewOutline,
                HsvMaterial = previewHsvMaterial,
            });

            ModLog.Trace($"Added preview widget {i} for {refs.Ancient.Id.Entry}: relic={(option.Relic?.Id.Entry ?? "<none>")}, textKey={option.TextKey}");
        }
    }


    private void ApplySecondVotePresentation(bool animate)
    {
        /*
         * is the “final-round visibility policy” method: it hides the suppressed ancient’s preview, shows other previews,
         * and attaches the special reaction bubble to the designated ancient.
         */
        foreach (SlotRefs refs in _slots)
        {
            if (refs.ReactionBubble != null && GodotObject.IsInstanceValid(refs.ReactionBubble))
            {
                refs.ReactionBubble.QueueFree();
            }

            ClearChildren(refs.ReactionAnchor);
            refs.ReactionBubble = null;
            refs.ReactionAnchor.Visible = false;
            refs.ReactionAnchor.Modulate = Colors.White;
            refs.PreviewAnchor.Modulate = Colors.White;
        }

        if (_roundType != VoteRoundType.FinalRevealVote)
        {
            foreach (SlotRefs refs in _slots)
            {
                refs.PreviewAnchor.Visible = refs.PreviewWidgets.Count > 0;
            }

            return;
        }

        foreach (SlotRefs refs in _slots)
        {
            bool suppressPreview = !string.IsNullOrEmpty(_suppressedPreviewAncientId)
                && refs.Ancient.Id.Entry == _suppressedPreviewAncientId;

            refs.PreviewAnchor.Visible = refs.PreviewWidgets.Count > 0 && !suppressPreview;

            if (!string.IsNullOrEmpty(_reactionAncientId) && refs.Ancient.Id.Entry == _reactionAncientId)
            {
                TryShowReactionBubble(refs, animate);
            }
        }

        ModLog.Debug($"Second vote presentation: suppressed={_suppressedPreviewAncientId ?? "<none>"}, reaction={_reactionAncientId ?? "<none>"}");
    }

    private void TryShowReactionBubble(SlotRefs refs, bool animate)
    {
        refs.ReactionBubble = null;

        Control bubble = BuildReactionBubble(refs.Ancient);
        bubble.ZIndex = 6;
        refs.PreviewAnchor.AddChild(bubble);
        refs.ReactionBubble = bubble;
        refs.ReactionAnchor.Visible = false;

        try
        {
            SfxCmd.Play("event:/sfx/ui/enchant_simple");
        }
        catch
        {
        }

        bubble.Modulate = new Color(1f, 1f, 1f, 0f);
        ModLog.Trace($"Showing custom reaction bubble for {refs.Ancient.Id.Entry}: How about now?");
    }


    private Control BuildReactionBubble(AncientEventModel ancient)
    {
        Texture2D bubbleTexture = GD.Load<Texture2D>(DialogueBubbleTexturePath)
            ?? throw new InvalidOperationException($"Could not load {DialogueBubbleTexturePath}");
        Texture2D tailTexture = GD.Load<Texture2D>(DialogueTailTexturePath)
            ?? throw new InvalidOperationException($"Could not load {DialogueTailTexturePath}");
        Font regularFont = GD.Load<Font>(DialogueRegularFontPath)
            ?? throw new InvalidOperationException($"Could not load {DialogueRegularFontPath}");
        Font boldFont = GD.Load<Font>(DialogueBoldFontPath)
            ?? throw new InvalidOperationException($"Could not load {DialogueBoldFontPath}");
        Font italicFont = GD.Load<Font>(DialogueItalicFontPath)
            ?? throw new InvalidOperationException($"Could not load {DialogueItalicFontPath}");

        Control root = new()
        {
            Name = $"ReactionBubble_{ancient.Id.Entry}",
            MouseFilter = MouseFilterEnum.Ignore,
            FocusMode = FocusModeEnum.None,
            CustomMinimumSize = new Vector2(0f, ReactionBubbleHeight),
        };
        root.LayoutMode = 1;
        root.AnchorLeft = 0f;
        root.AnchorTop = 0f;
        root.AnchorRight = 1f;
        root.AnchorBottom = 0f;
        root.OffsetLeft = 0f;
        root.OffsetTop = 0f;
        root.OffsetRight = 0f;
        root.OffsetBottom = ReactionBubbleHeight;

        HBoxContainer line = new()
        {
            Name = "LineRoot",
            MouseFilter = MouseFilterEnum.Ignore,
            FocusMode = FocusModeEnum.None,
            CustomMinimumSize = new Vector2(0f, 68f),
        };
        line.LayoutMode = 1;
        line.AnchorLeft = 0f;
        line.AnchorTop = 0f;
        line.AnchorRight = 1f;
        line.AnchorBottom = 0f;
        line.OffsetLeft = 0f;
        line.OffsetTop = 0f;
        line.OffsetRight = 0f;
        line.OffsetBottom = 68f;
        root.AddChild(line);

        Control iconRoot = new()
        {
            Name = "AncientIcon",
            CustomMinimumSize = new Vector2(56f, 56f),
            MouseFilter = MouseFilterEnum.Ignore,
            FocusMode = FocusModeEnum.None,
        };
        line.AddChild(iconRoot);

        Texture2D? reactionIconTexture = ancient.RunHistoryIcon ?? ancient.MapIcon;
        Texture2D? reactionOutlineTexture = ancient.RunHistoryIconOutline ?? ancient.MapIcon;

        TextureRect icon = new()
        {
            Name = "Icon",
            Texture = reactionIconTexture,
            Modulate = Colors.White,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        icon.LayoutMode = 1;
        icon.AnchorLeft = 0f;
        icon.AnchorTop = 0f;
        icon.AnchorRight = 1f;
        icon.AnchorBottom = 1f;
        iconRoot.AddChild(icon);

        TextureRect outline = new()
        {
            Name = "Outline",
            Texture = reactionOutlineTexture,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
            Modulate = new Color(0f, 0f, 0f, 0.5f),
            ShowBehindParent = true,
        };
        outline.LayoutMode = 1;
        outline.AnchorLeft = 0f;
        outline.AnchorTop = 0f;
        outline.AnchorRight = 1f;
        outline.AnchorBottom = 1f;
        iconRoot.AddChild(outline);

        MarginContainer dialogueContainer = new()
        {
            Name = "DialogueContainer",
            CustomMinimumSize = new Vector2(0f, 68f),
            MouseFilter = MouseFilterEnum.Ignore,
            FocusMode = FocusModeEnum.None,
        };
        dialogueContainer.LayoutMode = 2;
        dialogueContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        line.AddChild(dialogueContainer);

        HBoxContainer tailRow = new()
        {
            Name = "TailRow",
            MouseFilter = MouseFilterEnum.Ignore,
            FocusMode = FocusModeEnum.None,
        };
        tailRow.LayoutMode = 1;
        tailRow.AnchorLeft = 0f;
        tailRow.AnchorTop = 0f;
        tailRow.AnchorRight = 1f;
        tailRow.AnchorBottom = 1f;
        tailRow.AddThemeConstantOverride("separation", -12);
        dialogueContainer.AddChild(tailRow);

        TextureRect tail = new()
        {
            Name = "DialogueTailLeft",
            Texture = tailTexture,
            CustomMinimumSize = new Vector2(28f, 0f),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
            SelfModulate = ancient.DialogueColor,
        };
        tailRow.AddChild(tail);

        TextureRect tailShadow = new()
        {
            Name = "Shadow",
            Texture = tailTexture,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
            SelfModulate = new Color(0f, 0f, 0f, 0.25098f),
            ShowBehindParent = true,
        };
        tailShadow.LayoutMode = 1;
        tailShadow.AnchorLeft = 0f;
        tailShadow.AnchorTop = 0f;
        tailShadow.AnchorRight = 1f;
        tailShadow.AnchorBottom = 1f;
        tailShadow.OffsetLeft = 2f;
        tailShadow.OffsetTop = 5f;
        tailShadow.OffsetRight = 2f;
        tailShadow.OffsetBottom = 5f;
        tail.AddChild(tailShadow);

        NinePatchRect bubble = new()
        {
            Name = "Bubble",
            Texture = bubbleTexture,
            RegionRect = new Rect2(0f, 0f, 116f, 85f),
            PatchMarginLeft = 27,
            PatchMarginTop = 28,
            PatchMarginRight = 27,
            PatchMarginBottom = 28,
            AxisStretchHorizontal = NinePatchRect.AxisStretchMode.Stretch,
            AxisStretchVertical = NinePatchRect.AxisStretchMode.Stretch,
            SelfModulate = ancient.DialogueColor,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        bubble.LayoutMode = 2;
        bubble.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        tailRow.AddChild(bubble);

        NinePatchRect bubbleShadow = new()
        {
            Name = "Shadow",
            Texture = bubbleTexture,
            RegionRect = new Rect2(0f, 0f, 116f, 85f),
            PatchMarginLeft = 27,
            PatchMarginTop = 28,
            PatchMarginRight = 27,
            PatchMarginBottom = 28,
            AxisStretchHorizontal = NinePatchRect.AxisStretchMode.Stretch,
            AxisStretchVertical = NinePatchRect.AxisStretchMode.Stretch,
            SelfModulate = new Color(0f, 0f, 0f, 0.25098f),
            MouseFilter = MouseFilterEnum.Ignore,
            ShowBehindParent = true,
        };
        bubbleShadow.LayoutMode = 1;
        bubbleShadow.AnchorLeft = 0f;
        bubbleShadow.AnchorTop = 0f;
        bubbleShadow.AnchorRight = 1f;
        bubbleShadow.AnchorBottom = 1f;
        bubbleShadow.OffsetLeft = 2f;
        bubbleShadow.OffsetTop = 5f;
        bubbleShadow.OffsetRight = 2f;
        bubbleShadow.OffsetBottom = 5f;
        bubble.AddChild(bubbleShadow);

        MarginContainer textContainer = new()
        {
            Name = "TextContainer",
            MouseFilter = MouseFilterEnum.Ignore,
            FocusMode = FocusModeEnum.None,
        };
        textContainer.LayoutMode = 1;
        textContainer.AnchorLeft = 0f;
        textContainer.AnchorTop = 0f;
        textContainer.AnchorRight = 1f;
        textContainer.AnchorBottom = 1f;
        textContainer.AddThemeConstantOverride("margin_left", 20);
        textContainer.AddThemeConstantOverride("margin_top", 8);
        textContainer.AddThemeConstantOverride("margin_right", 18);
        textContainer.AddThemeConstantOverride("margin_bottom", 10);
        dialogueContainer.AddChild(textContainer);

        VBoxContainer textBox = new()
        {
            Name = "TextBox",
            MouseFilter = MouseFilterEnum.Ignore,
            FocusMode = FocusModeEnum.None,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        textBox.LayoutMode = 2;
        textBox.AddThemeConstantOverride("separation", 0);
        textContainer.AddChild(textBox);

        RichTextLabel speakerLabel = new()
        {
            Name = "SpeakerLabel",
            BbcodeEnabled = true,
            Text = $"[b]{ancient.Title.GetFormattedText()}[/b]",
            FitContent = true,
            ScrollActive = false,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        speakerLabel.AddThemeColorOverride("default_color", new Color(1f, 0.964706f, 0.886275f, 1f));
        speakerLabel.AddThemeFontOverride("normal_font", regularFont);
        speakerLabel.AddThemeFontOverride("bold_font", boldFont);
        speakerLabel.AddThemeFontOverride("italics_font", italicFont);
        speakerLabel.AddThemeFontSizeOverride("normal_font_size", 14);
        speakerLabel.AddThemeFontSizeOverride("bold_font_size", 14);
        speakerLabel.AddThemeFontSizeOverride("italics_font_size", 14);
        speakerLabel.AddThemeConstantOverride("line_separation", -2);
        textBox.AddChild(speakerLabel);

        RichTextLabel lineText = new()
        {
            Name = "LineText",
            BbcodeEnabled = true,
            Text = "[i]How about now?[/i]",
            FitContent = true,
            ScrollActive = false,
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        lineText.AddThemeColorOverride("default_color", new Color(1f, 0.964706f, 0.886275f, 1f));
        lineText.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.25098f));
        lineText.AddThemeConstantOverride("shadow_offset_x", 3);
        lineText.AddThemeConstantOverride("shadow_offset_y", 2);
        lineText.AddThemeConstantOverride("line_separation", -2);
        lineText.AddThemeFontOverride("normal_font", regularFont);
        lineText.AddThemeFontOverride("bold_font", boldFont);
        lineText.AddThemeFontOverride("italics_font", italicFont);
        lineText.AddThemeFontSizeOverride("normal_font_size", 24);
        lineText.AddThemeFontSizeOverride("bold_font_size", 24);
        lineText.AddThemeFontSizeOverride("italics_font_size", 24);
        textBox.AddChild(lineText);

        return root;
    }


    private void OnPreviewHovered(SlotRefs refs, int previewIndex)
    {
        if (previewIndex < 0 || previewIndex >= refs.PreviewWidgets.Count)
        {
            return;
        }

        PreviewWidgetRefs widget = refs.PreviewWidgets[previewIndex];
        if (_hoveredPreviewWidget == widget)
        {
            return;
        }

        if (_hoveredPreviewWidget != null)
        {
            ApplyPreviewHoverVisuals(_hoveredPreviewWidget, hovered: false);
            NHoverTipSet.Remove(_hoveredPreviewWidget.Wrapper);
        }

        _hoveredPreviewWidget = widget;
        _hoveredSlot = refs;
        _lastHoveredPoolIndex = refs.PoolIndex;
        ApplyPreviewHoverVisuals(widget, hovered: true);
        RefreshSlotVisuals(animate: true);
        RefreshAllVoteButtonOutlines();

        try
        {
            SfxCmd.Play("event:/sfx/ui/enchant_simple");
        }
        catch
        {
        }

        try
        {
            float viewportMid = GetViewportRect().Size.X * 0.5f;
            float wrapperCenterX = widget.Wrapper.GetGlobalRect().GetCenter().X;
            HoverTipAlignment alignment = wrapperCenterX < viewportMid
                ? HoverTipAlignment.Right
                : HoverTipAlignment.Left;
            NHoverTipSet hoverTipSet = NHoverTipSet.CreateAndShow(widget.Wrapper, widget.Option.HoverTips, alignment);
            hoverTipSet.ZIndex = 9;
            hoverTipSet.TopLevel = true;
            hoverTipSet.ProcessMode = ProcessModeEnum.Always;
            hoverTipSet.Show();
            if (hoverTipSet.GetParent() != null)
            {
                hoverTipSet.GetParent().MoveChild(hoverTipSet, hoverTipSet.GetParent().GetChildCount() - 1);
            }

            foreach (Control tipChild in hoverTipSet.GetChildren().OfType<Control>())
            {
                tipChild.ZIndex = 8;
            }
        }
        catch
        {
        }
    }

    private void OnPreviewUnhovered(SlotRefs refs, int previewIndex)
    {
        if (previewIndex < 0 || previewIndex >= refs.PreviewWidgets.Count)
        {
            return;
        }

        PreviewWidgetRefs widget = refs.PreviewWidgets[previewIndex];
        if (_hoveredPreviewWidget != widget)
        {
            return;
        }

        _hoveredPreviewWidget = null;
        ApplyPreviewHoverVisuals(widget, hovered: false);
        NHoverTipSet.Remove(widget.Wrapper);
        CallDeferred(nameof(ClearHoveredSlotIfInactive), refs.PoolIndex);
    }

    private void ApplyPreviewHoverVisuals(PreviewWidgetRefs widget, bool hovered)
    {
        if (widget.Wrapper == null || !GodotObject.IsInstanceValid(widget.Wrapper))
        {
            return;
        }

        Tween tween = CreateTween();
        tween.SetTrans(Tween.TransitionType.Expo).SetEase(Tween.EaseType.Out);

        Vector2 targetScale = hovered
            ? widget.BaseScale * PreviewHoverScaleMultiplier
            : widget.BaseScale;

        tween.TweenProperty(widget.Wrapper, "scale", targetScale, hovered ? 0.12f : 0.22f);

        if (widget.Outline != null)
        {
            Color targetOutline = hovered
                ? new Color(0f, 0f, 0f, 0.92f)
                : new Color(widget.Outline.Modulate.R, widget.Outline.Modulate.G, widget.Outline.Modulate.B, 0f);
            tween.Parallel().TweenProperty(widget.Outline, "modulate", targetOutline, hovered ? 0.12f : 0.22f);
        }

        if (widget.HsvMaterial != null)
        {
            widget.HsvMaterial.SetShaderParameter("v", hovered ? 0.78f : 0.9f);
        }
    }

    private void StartReactionWave(Control bubble)
    {
        RichTextLabel? speakerLabel = bubble.GetNodeOrNull<RichTextLabel>("LineRoot/DialogueContainer/TextContainer/TextBox/SpeakerLabel");
        RichTextLabel? lineText = bubble.GetNodeOrNull<RichTextLabel>("LineRoot/DialogueContainer/TextContainer/TextBox/LineText");

        if (_dialogueWaveShader == null)
        {
            _dialogueWaveShader = new Shader
            {
                Code = "shader_type canvas_item; uniform float amplitude = 1.2; uniform float speed = 1.55; uniform float frequency = 0.055; void vertex() { VERTEX.y += sin((VERTEX.x * frequency) + (TIME * speed)) * amplitude; }"
            };
        }

        if (speakerLabel != null)
        {
            ShaderMaterial speakerMat = new() { Shader = _dialogueWaveShader };
            speakerMat.SetShaderParameter("amplitude", 0.55f);
            speakerMat.SetShaderParameter("speed", 1.35f);
            speakerMat.SetShaderParameter("frequency", 0.08f);
            speakerLabel.Material = speakerMat;
        }

        if (lineText != null)
        {
            ShaderMaterial lineMat = new() { Shader = _dialogueWaveShader };
            lineMat.SetShaderParameter("amplitude", 1.1f);
            lineMat.SetShaderParameter("speed", 1.6f);
            lineMat.SetShaderParameter("frequency", 0.06f);
            lineText.Material = lineMat;
        }
    }

    private static bool IsUsableScenePath(string? path)
    {
       /* Made to handle Custom ancient scene paths not working. */ 
        return !string.IsNullOrWhiteSpace(path)
               && path.StartsWith("res://", StringComparison.Ordinal)
               && path.EndsWith(".tscn", StringComparison.OrdinalIgnoreCase)
               && !string.Equals(path, "res://", StringComparison.Ordinal);
    }

    private string? GetAncientScenePath(AncientEventModel ancient)
    {
        if (AncientScenePaths.TryGetValue(ancient.Id.Entry, out string? mapped) && IsUsableScenePath(mapped))
            return mapped;

        string? reflected = Traverse.Create(ancient)
            .Property("BackgroundScenePath")
            .GetValue<string?>();

        ModLog.Trace($"Reflected BackgroundScenePath for {ancient.Id.Entry}: '{reflected ?? "<null>"}'");

        return IsUsableScenePath(reflected) ? reflected : null;
    }

    private void LoadAncientScene(SlotRefs refs)
    {
        ClearChildren(refs.SceneMount);
        refs.SceneRoot = null;

        string? scenePath = GetAncientScenePath(refs.Ancient);
        if (!IsUsableScenePath(scenePath))
        {
            ModLog.Debug($"No usable ancient scene for {refs.Ancient.Id.Entry}. Path was '{scenePath ?? "<null>"}'. Using overlay only.");
            return;
        }

        PackedScene? scene = GD.Load<PackedScene>(scenePath);
        if (scene == null)
        {
            ModLog.Warn($"Could not load ancient scene for {refs.Ancient.Id.Entry} at '{scenePath}'");
            return;
        }

        try
        {
            Node root = scene.Instantiate();
            refs.SceneMount.AddChild(root);
            refs.SceneRoot = root;
            ApplySceneTransform(refs, hovered: false, animate: false);
        }
        catch (Exception ex)
        {
            ModLog.Error($"Failed to instantiate ancient scene for {refs.Ancient.Id.Entry} at '{scenePath}': {ex}");
        }
    }
    private void RefreshLayout()
    {
        if (_stageArea == null || _slots.Count == 0)
        {
            return;
        }

        Vector2 area = _stageArea.Size;
        if (area.X <= 1f || area.Y <= 1f)
        {
            return;
        }

        float cardHeight = Math.Clamp(MathF.Round(area.Y * CardHeightRatio), 188f, 224f);
        float cardWidth = _slots.Count == 2
            ? Math.Clamp(area.X * 0.34f, 430f, 620f)
            : Math.Clamp(area.X * 0.29f, 430f, 520f);

        PortalShape[] shapes = _slots.Count switch
        {
            3 => BuildThreePortalShapes(area, cardWidth, cardHeight),
            2 => BuildTwoPortalShapes(area, cardWidth, cardHeight),
            _ => BuildFallbackShapes(area, cardWidth, cardHeight, _slots.Count)
        };

        for (int i = 0; i < _slots.Count; i++)
        {
            SlotRefs refs = _slots[i];
            PortalShape shape = shapes[Math.Min(i, shapes.Length - 1)];
            refs.Shape = shape;
            refs.BaseSize = area;
            refs.CardBasePosition = shape.CardRect.Position;

            refs.SlotRoot.Position = Vector2.Zero;
            refs.SlotRoot.Size = area;
            refs.SlotRoot.PivotOffset = area * 0.5f;
            refs.SlotRoot.ZIndex = 2;

            refs.SceneViewport.Size = new Vector2I(
                Math.Max(1, (int)MathF.Ceiling(area.X)),
                Math.Max(1, (int)MathF.Ceiling(area.Y)));

            refs.CardRoot.Position = refs.CardBasePosition;
            refs.CardRoot.Size = shape.CardRect.Size;
            refs.CardRoot.PivotOffset = refs.CardRoot.Size * 0.5f;
            refs.SlotClickTarget.Position = shape.PortalRect.Position;
            refs.SlotClickTarget.Size = shape.PortalRect.Size;

            ApplyPortalGeometry(shape, refs);
            ApplySceneTransform(refs, hovered: !_resolved && ReferenceEquals(_hoveredSlot, refs), animate: false);
            LayoutVoteIcons(refs);
            LayoutPreview(refs);
            LayoutReaction(refs);
        }
    }

    private void LayoutPreview(SlotRefs refs)
    {
        if (!refs.PreviewAnchor.Visible || refs.PreviewWidgets.Count == 0)
        {
            return;
        }

        Vector2 anchorSize = refs.PreviewAnchor.Size;
        if (anchorSize.X <= 1f || anchorSize.Y <= 1f)
        {
            anchorSize = new Vector2(Math.Max(1f, refs.CardRoot.Size.X - 24f), 280f);
        }

        float scaleX = _slots.Count == 2 ? 0.82f : 0.78f;
        float scaleY = _slots.Count == 2 ? 0.72f : 0.68f;
        float displayWidth = anchorSize.X;
        float displayHeight = 70f;
        float gap = 8f;
        float startY = GetPreviewListStartY(refs, anchorSize);

        ModLog.Trace($"Layout preview for {refs.Ancient.Id.Entry}: anchor={anchorSize}, wrappers={refs.PreviewWidgets.Count}, startY={startY}");

        for (int i = 0; i < refs.PreviewWidgets.Count; i++)
        {
            PreviewWidgetRefs widget = refs.PreviewWidgets[i];
            Control wrapper = widget.Wrapper;
            wrapper.LayoutMode = 1;
            wrapper.AnchorLeft = 0f;
            wrapper.AnchorTop = 0f;
            wrapper.AnchorRight = 0f;
            wrapper.AnchorBottom = 0f;
            wrapper.Position = new Vector2(0f, startY + (i * (displayHeight + gap)));
            wrapper.Size = new Vector2(displayWidth / scaleX, displayHeight / scaleY);
            wrapper.Scale = new Vector2(scaleX, scaleY);
            wrapper.PivotOffset = Vector2.Zero;
            widget.BasePosition = wrapper.Position;
            widget.BaseScale = wrapper.Scale;
        }
    }



    private void LayoutReaction(SlotRefs refs)
    {
        if (refs.ReactionBubble == null)
        {
            return;
        }

        Vector2 anchorSize = refs.PreviewAnchor.Size;
        if (anchorSize.X <= 1f || anchorSize.Y <= 1f)
        {
            anchorSize = new Vector2(Math.Max(1f, refs.CardRoot.Size.X - 24f), 280f);
        }

        float startY = GetPreviewListStartY(refs, anchorSize);
        float bubbleY = MathF.Max(0f, startY - ReactionBubbleHeight - ReactionBubbleGap);

        refs.ReactionBubble.LayoutMode = 1;
        refs.ReactionBubble.AnchorLeft = 0f;
        refs.ReactionBubble.AnchorTop = 0f;
        refs.ReactionBubble.AnchorRight = 1f;
        refs.ReactionBubble.AnchorBottom = 0f;
        refs.ReactionBubble.OffsetLeft = 0f;
        refs.ReactionBubble.OffsetTop = bubbleY;
        refs.ReactionBubble.OffsetRight = 0f;
        refs.ReactionBubble.OffsetBottom = bubbleY + ReactionBubbleHeight;
    }

    private static PortalShape[] BuildThreePortalShapes(Vector2 area, float cardWidth, float cardHeight)
    {
        float h = area.Y;
        float outerPad = Math.Max(20f, (area.X - (cardWidth * 3f)) / 4f);
        float cardGap = outerPad;
        float cardY = h - cardHeight - CardBottomInset;

        Rect2 leftCard = new(new Vector2(outerPad, cardY), new Vector2(cardWidth, cardHeight));
        Rect2 centerCard = new(new Vector2(outerPad + cardWidth + cardGap, cardY), new Vector2(cardWidth, cardHeight));
        Rect2 rightCard = new(new Vector2(outerPad + ((cardWidth + cardGap) * 2f), cardY), new Vector2(cardWidth, cardHeight));

        float leftGradient = (area.X * 0.06f) / area.Y;
        float rightGradient = (area.X * 0.06f) / area.Y;
        float seamLeftTopX = area.X * 0.38f;
        float seamLeftBottomX = seamLeftTopX - (area.Y * leftGradient);
        float seamRightTopX = area.X * 0.72f;
        float seamRightBottomX = seamRightTopX - (area.Y * rightGradient);

        float leftLogicalWidth = MathF.Max(seamLeftTopX, seamLeftBottomX) + (area.X * 0.28f);
        float centerLogicalLeft = MathF.Min(seamLeftTopX, seamLeftBottomX) - (area.X * 0.14f);
        float centerLogicalRight = MathF.Max(seamRightTopX, seamRightBottomX) + (area.X * 0.14f);
        float rightLogicalLeft = MathF.Min(seamRightTopX, seamRightBottomX) - (area.X * 0.28f);

        return new[]
        {
            new PortalShape(
                new Rect2(0f, 0f, leftLogicalWidth, h),
                leftCard,
                new Vector2(0f, 0f),
                new Vector2(seamLeftTopX, 0f),
                new Vector2(seamLeftBottomX, h),
                new Vector2(0f, h),
                1),
            new PortalShape(
                new Rect2(centerLogicalLeft, 0f, centerLogicalRight - centerLogicalLeft, h),
                centerCard,
                new Vector2(seamLeftTopX, 0f),
                new Vector2(seamRightTopX, 0f),
                new Vector2(seamRightBottomX, h),
                new Vector2(seamLeftBottomX, h),
                1),
            new PortalShape(
                new Rect2(rightLogicalLeft, 0f, area.X - rightLogicalLeft, h),
                rightCard,
                new Vector2(seamRightTopX, 0f),
                new Vector2(area.X, 0f),
                new Vector2(area.X, h),
                new Vector2(seamRightBottomX, h),
                1),
        };
    }

    private static PortalShape[] BuildTwoPortalShapes(Vector2 area, float cardWidth, float cardHeight)
    {
        float h = area.Y;
        float outerPad = Math.Max(42f, (area.X - (cardWidth * 2f)) / 3f);
        float cardGap = outerPad;
        float cardY = h - cardHeight - CardBottomInset;

        Rect2 leftCard = new(new Vector2(outerPad, cardY), new Vector2(cardWidth, cardHeight));
        Rect2 rightCard = new(new Vector2(outerPad + cardWidth + cardGap, cardY), new Vector2(cardWidth, cardHeight));

        float seamTopX = area.X * 0.56f;
        float seamBottomX = area.X * 0.46f;
        float leftLogicalWidth = MathF.Max(seamTopX, seamBottomX) + (area.X * 0.24f);
        float rightLogicalLeft = MathF.Min(seamTopX, seamBottomX) - (area.X * 0.24f);

        return new[]
        {
            new PortalShape(
                new Rect2(0f, 0f, leftLogicalWidth, h),
                leftCard,
                new Vector2(0f, 0f),
                new Vector2(seamTopX, 0f),
                new Vector2(seamBottomX, h),
                new Vector2(0f, h),
                1),
            new PortalShape(
                new Rect2(rightLogicalLeft, 0f, area.X - rightLogicalLeft, h),
                rightCard,
                new Vector2(seamTopX, 0f),
                new Vector2(area.X, 0f),
                new Vector2(area.X, h),
                new Vector2(seamBottomX, h),
                1),
        };
    }

private static PortalShape[] BuildFallbackShapes(Vector2 area, float cardWidth, float cardHeight, int count)
{
    if (count <= 0)
        return [];

    if (count == 3)
        return BuildThreePortalShapes(area, cardWidth, cardHeight);

    float h = area.Y;

    // Match the "3 portal" card spacing style as closely as possible.
    float outerPad = MathF.Max(20f, (area.X - (cardWidth * count)) / (count + 1f));
    float cardGap = outerPad;

    // If the requested card width doesn't fit, shrink it gracefully.
    float maxCardWidthThatFits = (area.X - (outerPad * (count + 1f))) / count;
    float actualCardWidth = MathF.Min(cardWidth, maxCardWidthThatFits);
    float cardY = h - cardHeight - CardBottomInset;

    Rect2[] cards = new Rect2[count];
    for (int i = 0; i < count; i++)
    {
        float x = outerPad + (i * (actualCardWidth + cardGap));
        cards[i] = new Rect2(new Vector2(x, cardY), new Vector2(actualCardWidth, cardHeight));
    }

    // Internal seam positions are midway between neighboring cards, like the 3-portal layout.
    float[] seamTopX = new float[count - 1];
    float[] seamBottomX = new float[count - 1];

    // Same leftward lean as BuildThreePortalShapes: bottom is shifted left by ~6% of screen width.
    float seamShift = area.X * 0.06f;

    for (int i = 0; i < count - 1; i++)
    {
        Rect2 left = cards[i];
        Rect2 right = cards[i + 1];

        float midpoint = (left.End.X + right.Position.X) * 0.5f;
        seamTopX[i] = midpoint;
        seamBottomX[i] = midpoint - seamShift;
    }

    PortalShape[] shapes = new PortalShape[count];

    for (int i = 0; i < count; i++)
    {
        float leftTop = i == 0 ? 0f : seamTopX[i - 1];
        float rightTop = i == count - 1 ? area.X : seamTopX[i];
        float rightBottom = i == count - 1 ? area.X : seamBottomX[i];
        float leftBottom = i == 0 ? 0f : seamBottomX[i - 1];

        // Give each portal a slightly wider logical rect than the visible polygon,
        // similar to the 3-portal version.
        float logicalPad = area.X * 0.10f;
        float logicalLeft = i == 0
            ? 0f
            : MathF.Max(0f, MathF.Min(leftTop, leftBottom) - logicalPad);

        float logicalRight = i == count - 1
            ? area.X
            : MathF.Min(area.X, MathF.Max(rightTop, rightBottom) + logicalPad);

        Rect2 logicalRect = new Rect2(
            logicalLeft,
            0f,
            logicalRight - logicalLeft,
            h);

        shapes[i] = new PortalShape(
            logicalRect,
            cards[i],
            new Vector2(leftTop, 0f),
            new Vector2(rightTop, 0f),
            new Vector2(rightBottom, h),
            new Vector2(leftBottom, h),
            1);
    }

    return shapes;
}

    private void ApplyPortalGeometry(PortalShape shape, SlotRefs refs)
    {
        Vector2[] polygon = new[]
        {
            shape.TopLeft,
            shape.TopRight,
            shape.BottomRight,
            shape.BottomLeft,
        };

        refs.ScenePolygon.Polygon = polygon;
        refs.ScenePolygon.Set("uv", polygon);
        refs.GlowPolygon.Polygon = polygon;
        refs.HoverFlashPolygon.Polygon = polygon;

        refs.LeftRim.Polygon = BuildLineQuad(shape.TopLeft, shape.BottomLeft, PortalRimThickness);
        refs.RightRim.Polygon = BuildLineQuad(shape.TopRight, shape.BottomRight, PortalRimThickness);
        refs.LeftRim.ZIndex = 0;
        refs.RightRim.ZIndex = 0;
    }

    private AncientSceneConfig GetSceneConfig(string ancientId)
    {
        return AncientSceneConfigs.TryGetValue(ancientId, out AncientSceneConfig found)
            ? found
            : DefaultAncientSceneConfig;
    }

    private Vector2 ResolveSceneBaseSize(SlotRefs refs, AncientSceneConfig cfg)
    {
        if (cfg.BaseSize.X > 1f && cfg.BaseSize.Y > 1f)
        {
            return cfg.BaseSize;
        }

        Vector2 viewportSize = GetViewportRect().Size;
        if (viewportSize.X > 1f && viewportSize.Y > 1f)
        {
            return viewportSize;
        }

        if (_stageArea != null && _stageArea.Size.X > 1f && _stageArea.Size.Y > 1f)
        {
            return _stageArea.Size;
        }

        if (refs.BaseSize.X > 1f && refs.BaseSize.Y > 1f)
        {
            return refs.BaseSize;
        }

        return new Vector2(1920f, 1080f);
    }

    private SceneTransform GetSceneTransform(SlotRefs refs, bool hovered)
    {
        AncientSceneConfig cfg = GetSceneConfig(refs.Ancient.Id.Entry);
        Vector2 baseSize = ResolveSceneBaseSize(refs, cfg);
        float appliedScale = cfg.Scale * (hovered ? HoverSceneScaleMultiplier : 1f);

        Vector2 slotAnchorPx = new(
            (refs.Shape.TopLeft.X + refs.Shape.TopRight.X + refs.Shape.BottomLeft.X + refs.Shape.BottomRight.X) * 0.25f,
            0f);

        Vector2 sourceAnchorPx = new(
            baseSize.X * appliedScale * cfg.SourceAnchor01.X,
            baseSize.Y * appliedScale * cfg.SourceAnchor01.Y);

        Vector2 extraPx = new(
            refs.BaseSize.X * cfg.ExtraOffset01.X,
            refs.BaseSize.Y * cfg.ExtraOffset01.Y);

        return new SceneTransform(
            baseSize,
            slotAnchorPx - sourceAnchorPx + extraPx,
            Vector2.One * appliedScale);
    }

    private void ApplySceneTransform(SlotRefs refs, bool hovered, bool animate)
    {
        if (refs.SceneRoot == null)
        {
            return;
        }

        SceneTransform target = GetSceneTransform(refs, hovered);
        refs.SceneMount.Size = target.Size;
        refs.SceneMount.PivotOffset = Vector2.Zero;
        refs.SceneMount.ZIndex = 0;

        if (animate)
        {
            Tween tween = CreateTween();
            tween.SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
            tween.TweenProperty(refs.SceneMount, "position", target.Position, 0.12f);
            tween.Parallel().TweenProperty(refs.SceneMount, "scale", target.Scale, 0.12f);
        }
        else
        {
            refs.SceneMount.Position = target.Position;
            refs.SceneMount.Scale = target.Scale;
        }

        if (refs.SceneRoot is CanvasItem canvasItem)
        {
            canvasItem.ZIndex = 0;
            canvasItem.ShowBehindParent = false;
        }
    }

    private SlotRefs? FindSlot(int poolIndex)
    {
        return _slots.Find(slot => slot.PoolIndex == poolIndex);
    }

    private void PlayHoverSoundIfNeeded(int poolIndex)
    {
        if (_lastHoveredPoolIndex == poolIndex)
        {
            return;
        }

        _lastHoveredPoolIndex = poolIndex;

        if (_hoverSfx?.Stream != null)
        {
            _hoverSfx.Stop();
            _hoverSfx.Play();
        }
    }

    private void SetInitialSecondRoundWinnerEmphasisAmount(float amount)
    {
        amount = Mathf.Clamp(amount, 0f, 1f);

        if (Mathf.IsEqualApprox(_initialSecondRoundWinnerEmphasisAmount, amount))
        {
            return;
        }

        _initialSecondRoundWinnerEmphasisAmount = amount;

        if (_uiReady && _slots.Count > 0)
        {
            RefreshSlotVisuals(animate: false);
        }
    }

    private void ClearInitialSecondRoundWinnerEmphasisIfFocusChanged(int? nextPoolIndex)
    {
        if (_roundType != VoteRoundType.FinalRevealVote)
        {
            _initialSecondRoundFocusPoolIndex = null;
            _initialSecondRoundWinnerEmphasisFadeTween?.Kill();
            _initialSecondRoundWinnerEmphasisFadeTween = null;
            SetInitialSecondRoundWinnerEmphasisAmount(0f);
            return;
        }

        if (!_initialSecondRoundFocusPoolIndex.HasValue)
        {
            return;
        }

        if (nextPoolIndex == _initialSecondRoundFocusPoolIndex.Value)
        {
            return;
        }

        _initialSecondRoundFocusPoolIndex = null;

        _initialSecondRoundWinnerEmphasisFadeTween?.Kill();
        _initialSecondRoundWinnerEmphasisFadeTween = CreateTween();
        _initialSecondRoundWinnerEmphasisFadeTween
            .SetTrans(InitialSecondRoundWinnerEmphasisFadeTransition)
            .SetEase(InitialSecondRoundWinnerEmphasisFadeEase);

        float startAmount = _initialSecondRoundWinnerEmphasisAmount;
        _initialSecondRoundWinnerEmphasisFadeTween.TweenMethod(
            Callable.From<float>(SetInitialSecondRoundWinnerEmphasisAmount),
            startAmount,
            0f,
            InitialSecondRoundWinnerEmphasisFadeDuration);
        _initialSecondRoundWinnerEmphasisFadeTween.TweenCallback(Callable.From(() =>
        {
            _initialSecondRoundWinnerEmphasisFadeTween = null;
        }));
    }


    private void OnSlotHovered(int poolIndex)
    {
        if (_resolved)
        {
            return;
        }

        SlotRefs? refs = FindSlot(poolIndex);
        if (refs == null)
        {
            return;
        }

        ClearInitialSecondRoundWinnerEmphasisIfFocusChanged(poolIndex);

        if (!ReferenceEquals(_hoveredSlot, refs))
        {
            PlayHoverSoundIfNeeded(poolIndex);
        }

        _hoveredSlot = refs;
        RefreshSlotVisuals(animate: true);
        RefreshAllVoteButtonOutlines();
    }

    private void OnSlotUnhovered(int poolIndex)
    {
        SlotRefs? refs = _slots.FirstOrDefault(s => s.PoolIndex == poolIndex);
        if (_resolved)
        {
            return;
        }

        CallDeferred(nameof(ClearHoveredSlotIfInactive), poolIndex);
    }

    private void ClearHoveredSlotIfInactive(int poolIndex)
    {
        SlotRefs? refs = FindSlot(poolIndex);
        if (refs == null || !ReferenceEquals(_hoveredSlot, refs))
        {
            return;
        }

        Vector2 mousePosition = GetViewport().GetMousePosition();
        bool stillHovering = refs.CardRoot.GetGlobalRect().HasPoint(mousePosition)
            || refs.ChooseButton.GetGlobalRect().HasPoint(mousePosition)
            || (VoteClickTarget == ChooseTheAncientConfig.VoteClickTargetMode.WholeSlot && IsMouseOverWholeSlot(refs))
            || refs.ChooseButtonWrap.HasFocus()
            || refs.ChooseButton.HasFocus()
            || refs.PreviewWidgets.Any(widget => GodotObject.IsInstanceValid(widget.Wrapper) && widget.Wrapper.HasFocus());

        if (stillHovering)
        {
            return;
        }

        ClearInitialSecondRoundWinnerEmphasisIfFocusChanged(null);

        _hoveredSlot = null;
        _lastHoveredPoolIndex = null;
        RefreshSlotVisuals(animate: true);
        
        UpdateVoteButtonOutline(refs);
    }

    private void RefreshSlotVisuals(bool animate)
    {
        bool anyHovered = !_resolved && _hoveredSlot != null;

        foreach (SlotRefs refs in _slots)
        {
            bool hovered = anyHovered && ReferenceEquals(_hoveredSlot, refs);
            bool pendingSelected = !_resolved && _pendingPoolIndex == refs.PoolIndex;
            bool selected = _resolved && _selectedPoolIndex == refs.PoolIndex;

            float firstRoundWinnerOnSecondScreenAmount =
                _roundType == VoteRoundType.FinalRevealVote &&
                !string.IsNullOrEmpty(_suppressedPreviewAncientId) &&
                refs.Ancient.Id.Entry == _suppressedPreviewAncientId &&
                !_finalChosenPoolIndex.HasValue
                    ? _initialSecondRoundWinnerEmphasisAmount
                    : 0f;

            bool finalVoteWinner =
                _finalChosenPoolIndex.HasValue &&
                _finalChosenPoolIndex.Value == refs.PoolIndex;

            float glowAlpha = pendingSelected ? 0.10f : 0f;
            float flashAlpha = hovered && !pendingSelected ? 0.05f : 0f;
            float shadeAlpha = _resolved
                ? (selected ? 0.06f : 0.34f)
                : (pendingSelected ? 0.04f : hovered ? 0.10f : 0.18f);
            float slotAlpha = _resolved
                ? (selected ? 1f : 0.42f)
                : (pendingSelected ? 1f : anyHovered ? (hovered ? 1f : 0.84f) : 1f);
            float rimAlpha = _resolved
                ? (selected ? 0.94f : 0.28f)
                : (pendingSelected ? 0.98f : hovered ? 0.86f : 0.58f);
            float accentAlpha = _resolved
                ? (selected ? 0.96f : 0.35f)
                : (pendingSelected ? 1f : hovered ? 0.95f : 0.78f);

            // Extra emphasis for the round-1 survivor on the second screen.
            if (firstRoundWinnerOnSecondScreenAmount > 0f)
            {
                glowAlpha = Mathf.Lerp(glowAlpha, MathF.Max(glowAlpha, 0.10f), firstRoundWinnerOnSecondScreenAmount);
                flashAlpha = Mathf.Lerp(flashAlpha, MathF.Max(flashAlpha, 0.02f), firstRoundWinnerOnSecondScreenAmount);
                shadeAlpha = Mathf.Lerp(shadeAlpha, MathF.Min(shadeAlpha, 0.08f), firstRoundWinnerOnSecondScreenAmount);
                slotAlpha = Mathf.Lerp(slotAlpha, MathF.Max(slotAlpha, 1f), firstRoundWinnerOnSecondScreenAmount);
                rimAlpha = Mathf.Lerp(rimAlpha, MathF.Max(rimAlpha, 0.95f), firstRoundWinnerOnSecondScreenAmount);
                accentAlpha = Mathf.Lerp(accentAlpha, MathF.Max(accentAlpha, 1f), firstRoundWinnerOnSecondScreenAmount);
            }

            // Stronger emphasis for the true final winner.
            if (finalVoteWinner)
            {
                glowAlpha = MathF.Max(glowAlpha, 0.18f);
                flashAlpha = MathF.Max(flashAlpha, 0.05f);
                shadeAlpha = MathF.Min(shadeAlpha, 0.03f);
                slotAlpha = 1f;
                rimAlpha = 1f;
                accentAlpha = 1f;
            }

            Color glowColor = new(refs.AccentColor.R, refs.AccentColor.G, refs.AccentColor.B, glowAlpha);
            Color flashColor = new(1f, 1f, 1f, flashAlpha);
            Color shadeColor = new(0f, 0f, 0f, shadeAlpha);
            Color slotModulate = new(1f, 1f, 1f, slotAlpha);
            Color rimColor = new(refs.AccentColor.R, refs.AccentColor.G, refs.AccentColor.B, rimAlpha);
            Color accentColor = new(refs.AccentColor.R, refs.AccentColor.G, refs.AccentColor.B, accentAlpha);

            float cardOutlineAlpha =
                finalVoteWinner
                    ? 1f
                    : 0.72f * firstRoundWinnerOnSecondScreenAmount;

            Color cardOutlineColor =
                new(refs.AccentColor.R, refs.AccentColor.G, refs.AccentColor.B, cardOutlineAlpha);

            if (animate)
            {
                Tween tween = CreateTween();
                tween.SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
                tween.TweenProperty(refs.GlowPolygon, "color", glowColor, SlotVisualFadeDuration);
                tween.Parallel().TweenProperty(refs.HoverFlashPolygon, "color", flashColor, SlotVisualFadeDuration);
                tween.Parallel().TweenProperty(refs.CardShade, "color", shadeColor, SlotVisualFadeDuration);
                tween.Parallel().TweenProperty(refs.ScenePolygon, "modulate", slotModulate, SlotVisualFadeDuration);
                tween.Parallel().TweenProperty(refs.CardRoot, "modulate", slotModulate, SlotVisualFadeDuration);
                tween.Parallel().TweenProperty(refs.PreviewAnchor, "modulate", slotModulate, SlotVisualFadeDuration);
                tween.Parallel().TweenProperty(refs.LeftRim, "color", rimColor, SlotVisualFadeDuration);
                tween.Parallel().TweenProperty(refs.RightRim, "color", rimColor, SlotVisualFadeDuration);
                tween.Parallel().TweenProperty(refs.TopAccent, "color", accentColor, SlotVisualFadeDuration);
                tween.Parallel().TweenProperty(refs.CardOutline, "modulate", cardOutlineColor, CardOutlineFadeDuration);
            }
            else
            {
                refs.GlowPolygon.Color = glowColor;
                refs.HoverFlashPolygon.Color = flashColor;
                refs.CardShade.Color = shadeColor;
                refs.ScenePolygon.Modulate = slotModulate;
                refs.CardRoot.Modulate = slotModulate;
                refs.PreviewAnchor.Modulate = slotModulate;
                refs.LeftRim.Color = rimColor;
                refs.RightRim.Color = rimColor;
                refs.TopAccent.Color = accentColor;
                refs.CardOutline.Modulate = cardOutlineColor;
            }

            ApplySceneTransform(refs, hovered, animate);
        }
    }


    private void ConnectControllerPromptSignals()
    {
        if (NControllerManager.Instance != null)
        {
            Callable updatePrompts = Callable.From(UpdateVoteButtonControllerIcons);
            if (!NControllerManager.Instance.IsConnected(NControllerManager.SignalName.MouseDetected, updatePrompts))
            {
                NControllerManager.Instance.Connect(NControllerManager.SignalName.MouseDetected, updatePrompts);
            }

            if (!NControllerManager.Instance.IsConnected(NControllerManager.SignalName.ControllerDetected, updatePrompts))
            {
                NControllerManager.Instance.Connect(NControllerManager.SignalName.ControllerDetected, updatePrompts);
            }
        }

        if (NInputManager.Instance != null)
        {
            Callable updatePrompts = Callable.From(UpdateVoteButtonControllerIcons);
            if (!NInputManager.Instance.IsConnected(NInputManager.SignalName.InputRebound, updatePrompts))
            {
                NInputManager.Instance.Connect(NInputManager.SignalName.InputRebound, updatePrompts);
            }
        }
    }

    private void DisconnectControllerPromptSignals()
    {
        Callable updatePrompts = Callable.From(UpdateVoteButtonControllerIcons);

        if (NControllerManager.Instance != null)
        {
            if (NControllerManager.Instance.IsConnected(NControllerManager.SignalName.MouseDetected, updatePrompts))
            {
                NControllerManager.Instance.Disconnect(NControllerManager.SignalName.MouseDetected, updatePrompts);
            }

            if (NControllerManager.Instance.IsConnected(NControllerManager.SignalName.ControllerDetected, updatePrompts))
            {
                NControllerManager.Instance.Disconnect(NControllerManager.SignalName.ControllerDetected, updatePrompts);
            }
        }

        if (NInputManager.Instance != null && NInputManager.Instance.IsConnected(NInputManager.SignalName.InputRebound, updatePrompts))
        {
            NInputManager.Instance.Disconnect(NInputManager.SignalName.InputRebound, updatePrompts);
        }
    }

    private void UpdateVoteButtonControllerIcons()
    {
        bool showControllerPrompts = NControllerManager.Instance != null && NControllerManager.Instance.IsUsingController;
        Texture2D? selectIcon = NInputManager.Instance?.GetHotkeyIcon(MegaInput.select);

        foreach (SlotRefs refs in _slots)
        {
            if (refs.ChooseButtonControllerIcon == null || !GodotObject.IsInstanceValid(refs.ChooseButtonControllerIcon))
            {
                continue;
            }

            refs.ChooseButtonControllerIcon.Visible = showControllerPrompts && !refs.ChooseButton.Disabled;
            if (selectIcon != null && ShowControllerHotkeys)
            {
                refs.ChooseButtonControllerIcon.Texture = selectIcon;
            }
        }
    }

    private List<PreviewWidgetRefs> GetNavigablePreviewWidgets(SlotRefs refs)
    {
        if (!refs.PreviewAnchor.Visible)
        {
            return new List<PreviewWidgetRefs>();
        }

        return refs.PreviewWidgets
            .Where(widget => GodotObject.IsInstanceValid(widget.Wrapper) && widget.Wrapper.Visible)
            .ToList();
    }

    private SlotRefs GetWrappedSlot(int slotIndex, int direction)
    {
        if (_slots.Count == 0)
        {
            throw new InvalidOperationException("Tried to navigate controller focus with no slots.");
        }

        int wrappedIndex = (slotIndex + direction) % _slots.Count;
        if (wrappedIndex < 0)
        {
            wrappedIndex += _slots.Count;
        }

        return _slots[wrappedIndex];
    }

    private Control GetPreviewTargetForAdjacentSlot(int slotIndex, int previewIndex, int direction)
    {
        SlotRefs adjacentSlot = GetWrappedSlot(slotIndex, direction);
        List<PreviewWidgetRefs> adjacentPreviews = GetNavigablePreviewWidgets(adjacentSlot);
        if (adjacentPreviews.Count == 0)
        {
            return adjacentSlot.ChooseButtonWrap;
        }

        int clampedPreviewIndex = Math.Clamp(previewIndex, 0, adjacentPreviews.Count - 1);
        return adjacentPreviews[clampedPreviewIndex].Wrapper;
    }

    private void ConfigureControllerNavigation()
    {
        if (_slots.Count == 0)
        {
            return;
        }

        for (int slotIndex = 0; slotIndex < _slots.Count; slotIndex++)
        {
            SlotRefs refs = _slots[slotIndex];
            refs.ChooseButtonWrap.FocusMode = refs.ChooseButton.Disabled ? FocusModeEnum.None : FocusModeEnum.All;

            Control leftTarget = GetWrappedSlot(slotIndex, -1).ChooseButtonWrap;
            Control rightTarget = GetWrappedSlot(slotIndex, 1).ChooseButtonWrap;

            refs.ChooseButtonWrap.FocusNeighborLeft = leftTarget.GetPath();
            refs.ChooseButtonWrap.FocusNeighborRight = rightTarget.GetPath();
            refs.ChooseButtonWrap.FocusNeighborBottom = refs.ChooseButtonWrap.GetPath();

            List<PreviewWidgetRefs> previews = GetNavigablePreviewWidgets(refs);
            if (previews.Count > 0)
            {
                refs.ChooseButtonWrap.FocusNeighborTop = previews[0].Wrapper.GetPath();
            }
            else
            {
                refs.ChooseButtonWrap.FocusNeighborTop = refs.ChooseButtonWrap.GetPath();
            }

            foreach (PreviewWidgetRefs widget in refs.PreviewWidgets)
            {
                widget.Wrapper.FocusMode = FocusModeEnum.None;
                widget.Wrapper.FocusNeighborLeft = widget.Wrapper.GetPath();
                widget.Wrapper.FocusNeighborRight = widget.Wrapper.GetPath();
                widget.Wrapper.FocusNeighborTop = refs.ChooseButtonWrap.GetPath();
                widget.Wrapper.FocusNeighborBottom = refs.ChooseButtonWrap.GetPath();
            }

            for (int previewIndex = 0; previewIndex < previews.Count; previewIndex++)
            {
                PreviewWidgetRefs widget = previews[previewIndex];
                widget.Wrapper.FocusMode = FocusModeEnum.All;
                widget.Wrapper.FocusNeighborTop = (previewIndex == 0
                    ? refs.ChooseButtonWrap.GetPath()
                    : previews[previewIndex - 1].Wrapper.GetPath());
                widget.Wrapper.FocusNeighborBottom = (previewIndex == previews.Count - 1
                    ? refs.ChooseButtonWrap.GetPath()
                    : previews[previewIndex + 1].Wrapper.GetPath());
                widget.Wrapper.FocusNeighborLeft = GetPreviewTargetForAdjacentSlot(slotIndex, previewIndex, -1).GetPath();
                widget.Wrapper.FocusNeighborRight = GetPreviewTargetForAdjacentSlot(slotIndex, previewIndex, 1).GetPath();
            }
        }
    }

    private SlotRefs? FindFocusedVoteSlot(Control? focusedControl)
    {
        if (focusedControl == null)
        {
            return null;
        }

        return _slots.FirstOrDefault(refs =>
            ReferenceEquals(refs.ChooseButtonWrap, focusedControl)
            || ReferenceEquals(refs.ChooseButton, focusedControl)
            || refs.ChooseButtonWrap.IsAncestorOf(focusedControl)
            || refs.ChooseButton.IsAncestorOf(focusedControl));
    }

    public void RecordVote(Player player, int poolIndex)
    {
        if (!GodotObject.IsInstanceValid(this))
        {
            return;
        }

        _votesByPlayerNetId[player.NetId] = poolIndex;
        RefreshVoteDisplays(animate: true);
    }

    private void RefreshVoteDisplays(bool animate, bool highlightLocalPlayer = true)
    {
        foreach (SlotRefs refs in _slots)
        {
            if (refs.VoteContainer == null || !GodotObject.IsInstanceValid(refs.VoteContainer))
            {
                continue;
            }

            refs.VoteContainer.RefreshPlayerVotes(animate);
            LayoutVoteIcons(refs);

            if (highlightLocalPlayer && _localPlayer != null)
            {
                try
                {
                    refs.VoteContainer.SetPlayerHighlighted(
                        _localPlayer,
                        _votesByPlayerNetId.TryGetValue(_localPlayer.NetId, out int voteIndex) && voteIndex == refs.PoolIndex);
                }
                catch
                {
                }
            }
        }
    }

    private async Task PlayVoteResolutionHighlightAsync(
        IReadOnlyList<int> votesByPlayerSlotOrder,
        int chosenPoolIndex)
    {
        RefreshVoteDisplays(animate: false, highlightLocalPlayer: false);

        List<(Player player, int poolIndex)> recordedVotes = new();
        for (int i = 0; i < Math.Min(_orderedPlayers.Count, votesByPlayerSlotOrder.Count); i++)
        {
            int poolIndex = votesByPlayerSlotOrder[i];
            if (poolIndex >= 0 && poolIndex < _pool.Count)
            {
                recordedVotes.Add((_orderedPlayers[i], poolIndex));
            }
        }

        int distinctVoteCount = recordedVotes
            .Select(v => v.poolIndex)
            .Distinct()
            .Count();

        if (distinctVoteCount > 1)
        {
            List<Player> sortedPlayers = recordedVotes
                .OrderBy(v => v.poolIndex)
                .ThenBy(v => GetVoteIconIndex(v.player, v.poolIndex))
                .Select(v => v.player)
                .ToList();

            List<Player> chosenPlayers = recordedVotes
                .Where(v => v.poolIndex == chosenPoolIndex)
                .Select(v => v.player)
                .ToList();

            if (sortedPlayers.Count > 0 && chosenPlayers.Count > 0)
            {
                int seed = BuildVoteResolutionSeed(votesByPlayerSlotOrder, chosenPoolIndex);
                int ticks = 12 + PositiveMod(seed, 6);
                float settleDelay = Mathf.Lerp(
                    VoteResolutionSettleDelayMin,
                    VoteResolutionSettleDelayMax,
                    PositiveMod(seed >> 4, 256) / 255f);

                Player winningPlayer = chosenPlayers[PositiveMod(seed >> 8, chosenPlayers.Count)];
                int winnerIndex = sortedPlayers.IndexOf(winningPlayer);
                double tickDelay = VoteResolutionSpinDuration / Math.Max(1, ticks);

                for (int step = 0; step <= ticks; step++)
                {
                    int rawIndex = winnerIndex - (ticks - step);
                    int index = PositiveMod(rawIndex, sortedPlayers.Count);
                    HighlightVotePlayer(sortedPlayers[index]);

                    try
                    {
                        NDebugAudioManager.Instance.Play("map_split_tick.mp3", 0.15f, PitchVariance.Small);
                    }
                    catch
                    {
                    }

                    await Cmd.Wait((float)tickDelay, ignoreCombatEnd: true);
                }

                await Cmd.Wait(settleDelay, ignoreCombatEnd: true);
            }
        }

        HighlightVotePlayer(null);
    }

    public async Task PlayFinalVoteResolutionAsync(IReadOnlyList<int> finalVotesByPlayerSlotOrder, int chosenPoolIndex)
    {
        if (!GodotObject.IsInstanceValid(this) || !IsInsideTree() || _roundType != VoteRoundType.FinalRevealVote)
        {
            return;
        }

        _finalChosenPoolIndex = chosenPoolIndex;

        await PlayVoteResolutionHighlightAsync(finalVotesByPlayerSlotOrder, chosenPoolIndex);

        _selectedPoolIndex = chosenPoolIndex;
        RefreshButtonTexts();
        RefreshSlotVisuals(animate: true);

        SlotRefs? chosenSlot = FindSlot(chosenPoolIndex);
        if (chosenSlot?.VoteContainer != null && GodotObject.IsInstanceValid(chosenSlot.VoteContainer))
        {
            chosenSlot.VoteContainer.BouncePlayers();
        }

        try
        {
            SfxCmd.Play("event:/sfx/ui/map/map_select");
        }
        catch
        {
        }

        await Cmd.Wait(0.45f, ignoreCombatEnd: true);
    } 
    
    public async Task PlayInitialVoteResolutionAsync(
        IReadOnlyList<int> firstVotesByPlayerSlotOrder,
        int chosenPoolIndex)
    {
        if (!GodotObject.IsInstanceValid(this) || !IsInsideTree() || _roundType != VoteRoundType.InitialKeepVote)
        {
            return;
        }

        await PlayVoteResolutionHighlightAsync(firstVotesByPlayerSlotOrder, chosenPoolIndex);

        _selectedPoolIndex = chosenPoolIndex;
        RefreshButtonTexts();
        RefreshSlotVisuals(animate: true);

        SlotRefs? chosenSlot = FindSlot(chosenPoolIndex);
        if (chosenSlot?.VoteContainer != null && GodotObject.IsInstanceValid(chosenSlot.VoteContainer))
        {
            chosenSlot.VoteContainer.BouncePlayers();
        }

        try
        {
            SfxCmd.Play("event:/sfx/ui/map/map_select");
        }
        catch
        {
        }

        await Cmd.Wait(0.45f, ignoreCombatEnd: true);
    }

    private void HighlightVotePlayer(Player? player)
    {
        if (_currentlyHighlightedVotePlayer == player)
        {
            return;
        }

        if (_currentlyHighlightedVotePlayer != null)
        {
            foreach (SlotRefs refs in _slots)
            {
                if (refs.VoteContainer == null || !GodotObject.IsInstanceValid(refs.VoteContainer))
                {
                    continue;
                }

                try
                {
                    refs.VoteContainer.SetPlayerHighlighted(_currentlyHighlightedVotePlayer, isHighlighted: false);
                }
                catch
                {
                }
            }
        }

        _currentlyHighlightedVotePlayer = player;

        if (player == null)
        {
            return;
        }

        foreach (SlotRefs refs in _slots)
        {
            if (refs.VoteContainer == null || !GodotObject.IsInstanceValid(refs.VoteContainer))
            {
                continue;
            }

            try
            {
                bool votedForThisSlot = _votesByPlayerNetId.TryGetValue(player.NetId, out int poolIndex) && poolIndex == refs.PoolIndex;
                refs.VoteContainer.SetPlayerHighlighted(player, votedForThisSlot);
            }
            catch
            {
            }
        }
    }

    private int GetVoteIconIndex(Player player, int poolIndex)
    {
        SlotRefs? slot = FindSlot(poolIndex);
        if (slot?.VoteContainer != null && GodotObject.IsInstanceValid(slot.VoteContainer))
        {
            try
            {
                int index = slot.VoteContainer.GetVoteIndex(player);
                if (index >= 0)
                {
                    return index;
                }
            }
            catch
            {
            }
        }

        int fallbackIndex = _orderedPlayers.IndexOf(player);
        return fallbackIndex >= 0 ? fallbackIndex : 999;
    }

    private int BuildVoteResolutionSeed(IReadOnlyList<int> finalVotesByPlayerSlotOrder, int chosenPoolIndex)
    {
        unchecked
        {
            int seed = 17;
            seed = (seed * 31) + _nextActIndex;
            seed = (seed * 31) + chosenPoolIndex;

            foreach (Player player in _orderedPlayers)
            {
                seed = (seed * 31) + (int)(player.NetId & 0x7FFFFFFF);
                if (_votesByPlayerNetId.TryGetValue(player.NetId, out int vote))
                {
                    seed = (seed * 31) + vote;
                }
            }

            foreach (int vote in finalVotesByPlayerSlotOrder)
            {
                seed = (seed * 31) + vote;
            }

            return seed;
        }
    }

    private static int PositiveMod(int value, int mod)
    {
        if (mod <= 0)
        {
            return 0;
        }

        int result = value % mod;
        return result < 0 ? result + mod : result;
    }

    private void LayoutVoteIcons(SlotRefs refs)
    {
        if (refs.VoteIconsAnchor == null || !GodotObject.IsInstanceValid(refs.VoteIconsAnchor))
        {
            return;
        }

        if (refs.VoteContainer == null || !GodotObject.IsInstanceValid(refs.VoteContainer))
        {
            return;
        }

        refs.VoteIconsAnchor.LayoutMode = 1;
        refs.VoteIconsAnchor.AnchorLeft = 1f;
        refs.VoteIconsAnchor.AnchorTop = 1f;
        refs.VoteIconsAnchor.AnchorRight = 1f;
        refs.VoteIconsAnchor.AnchorBottom = 1f;
        refs.VoteIconsAnchor.OffsetLeft = -112f;
        refs.VoteIconsAnchor.OffsetTop = -88f;
        refs.VoteIconsAnchor.OffsetRight = -16f;
        refs.VoteIconsAnchor.OffsetBottom = -48f;

        List<Control> icons = refs.VoteContainer.GetChildren()
            .OfType<Control>()
            .OrderBy(node => node.GetIndex())
            .ToList();

        if (icons.Count == 0)
        {
            return;
        }

        float spacing = VoteIconSize - VoteIconOverlap;
        float totalWidth = VoteIconSize + ((icons.Count - 1) * spacing);
        float startX = MathF.Max(0f, refs.VoteIconsAnchor.Size.X - totalWidth);
        float y = MathF.Max(0f, (refs.VoteIconsAnchor.Size.Y - VoteIconSize) * 0.5f);

        for (int i = 0; i < icons.Count; i++)
        {
            Control icon = icons[i];
            icon.LayoutMode = 1;
            icon.AnchorLeft = 0f;
            icon.AnchorTop = 0f;
            icon.AnchorRight = 0f;
            icon.AnchorBottom = 0f;
            icon.Position = new Vector2(startX + (i * spacing), y);
            icon.Size = new Vector2(VoteIconSize, VoteIconSize);
            icon.ZIndex = 12;
            icon.MouseFilter = MouseFilterEnum.Ignore;
        }
    }

    private void RefreshButtonTexts()
    {
        foreach (SlotRefs refs in _slots)
        {
            bool resolvedSelected = _resolved && _selectedPoolIndex == refs.PoolIndex;
            bool finalWinner = _finalChosenPoolIndex.HasValue && _finalChosenPoolIndex.Value == refs.PoolIndex;

            if (_resolved)
            {
                refs.ChooseButton.Disabled = true;
                if (_finalChosenPoolIndex.HasValue)
                {
                    refs.ChooseButton.Text = finalWinner ? "Selected Ancient" : "Voting Closed";
                }
                else
                {
                    refs.ChooseButton.Text = resolvedSelected ? "Vote Locked" : "Unavailable";
                }
            }
            else
            {
                refs.ChooseButton.Disabled = false;
                refs.ChooseButton.Text = "Vote For This Ancient";
            }
            
            UpdateVoteButtonOutline(refs);
        }

        ConfigureControllerNavigation();
        UpdateVoteButtonControllerIcons();
    }

    private void GrabInitialFocus()
    {
        DefaultFocusedControl?.CallDeferred(Control.MethodName.GrabFocus);
        CallDeferred(nameof(RefreshVoteButtonOutlinesAfterFocus));
    }
    
    private void RefreshVoteButtonOutlinesAfterFocus()
    {
        /* Deferred to deal with timing to settle focus. Might need to be deferred twice if
         this is not working. */
        CallDeferred(nameof(RefreshAllVoteButtonOutlines));
    }

    private void Select(int poolIndex)
    {
        if (_resolved)
        {
            return;
        }

        _resolved = true;
        _selectedPoolIndex = poolIndex;
        _pendingPoolIndex = null;
        _hoveredSlot = null;
        _lastHoveredPoolIndex = null;

        if (_localPlayer != null)
        {
            RecordVote(_localPlayer, poolIndex);
        }

        if (_clickSfx?.Stream != null)
        {
            _clickSfx.Stop();
            _clickSfx.Play();
        }

        RefreshButtonTexts();
        RefreshSlotVisuals(animate: true);

        _voteSubmitted.TrySetResult(poolIndex);
    }

    public void CloseScreen()
    {
        if (_closing || !GodotObject.IsInstanceValid(this))
        {
            return;
        }

        _closing = true;

        try
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
        catch (ObjectDisposedException)
        {
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_resolved)
        {
            return;
        }

        if (@event.IsActionPressed(MegaInput.select) && !@event.IsEcho())
        {
            Control? focusedControl = GetViewport().GuiGetFocusOwner();
            SlotRefs? focusedSlot = FindFocusedVoteSlot(focusedControl);
            if (focusedSlot != null && !focusedSlot.ChooseButton.Disabled)
            {
                Select(focusedSlot.PoolIndex);
                AcceptEvent();
                return;
            }
        }

        if (@event.IsActionPressed(MegaInput.cancel))
        {
            AcceptEvent();
        }
    }
    
    public override void _EnterTree()
    {
        base._EnterTree();
        _openScreens.Add(this);
    }
    
    public override void _ExitTree()
    {
        _openScreens.Remove(this);
        _roundIntroTween?.Kill();
        DisconnectControllerPromptSignals();
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

    private void TryInstallGeneratedSounds()
    {
        try
        {
            if (_hoverSfx != null && _hoverSfx.Stream == null)
            {
                _generatedHoverStream ??= BuildUiTone(
                    baseFrequencyHz: 698f,
                    overtoneFrequencyHz: 932f,
                    durationSeconds: 0.09f,
                    peakAmplitude: 0.17f,
                    attackSeconds: 0.004f,
                    releaseSeconds: 0.070f,
                    noiseAmount: 0.008f);
                _hoverSfx.Stream = _generatedHoverStream;
            }

            if (_clickSfx != null && _clickSfx.Stream == null)
            {
                _generatedClickStream ??= BuildUiTone(
                    baseFrequencyHz: 164f,
                    overtoneFrequencyHz: 328f,
                    durationSeconds: 0.14f,
                    peakAmplitude: 0.18f,
                    attackSeconds: 0.003f,
                    releaseSeconds: 0.10f,
                    noiseAmount: 0.012f);
                _clickSfx.Stream = _generatedClickStream;
            }
        }
        catch (Exception ex)
        {
            ModLog.Error($"Failed to create fallback UI sounds: {ex}");
        }
    }

    private static AudioStreamWav BuildUiTone(
        float baseFrequencyHz,
        float overtoneFrequencyHz,
        float durationSeconds,
        float peakAmplitude,
        float attackSeconds,
        float releaseSeconds,
        float noiseAmount)
    {
        const int mixRate = 44100;
        int sampleCount = Math.Max(1, (int)MathF.Round(durationSeconds * mixRate));
        byte[] data = new byte[sampleCount * sizeof(short)];
        float phaseA = 0f;
        float phaseB = 0f;
        uint noiseState = 0x12345678u;

        for (int i = 0; i < sampleCount; i++)
        {
            float t = i / (float)mixRate;
            float envelope = ComputeEnvelope(t, durationSeconds, attackSeconds, releaseSeconds);
            float detuneA = 1f + (0.0025f * MathF.Sin(t * 21f));
            float detuneB = 1f + (0.0035f * MathF.Sin((t * 14f) + 0.4f));

            phaseA += (baseFrequencyHz * detuneA * MathF.PI * 2f) / mixRate;
            phaseB += (overtoneFrequencyHz * detuneB * MathF.PI * 2f) / mixRate;

            noiseState = (noiseState * 1664525u) + 1013904223u;
            float noise = ((((noiseState >> 9) & 0x7FFFu) / 16383.5f) - 1f) * noiseAmount;

            float sample =
                (MathF.Sin(phaseA) * 0.80f) +
                (MathF.Sin(phaseB) * 0.20f) +
                (noise * envelope);

            sample *= peakAmplitude * envelope;
            sample = Math.Clamp(sample, -1f, 1f);

            short pcm = (short)MathF.Round(sample * short.MaxValue);
            BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(i * sizeof(short), sizeof(short)), pcm);
        }

        return new AudioStreamWav
        {
            MixRate = mixRate,
            Stereo = false,
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            Data = data,
        };
    }

    private static float ComputeEnvelope(float t, float duration, float attackSeconds, float releaseSeconds)
    {
        float attack = attackSeconds <= 0f ? 1f : Math.Clamp(t / attackSeconds, 0f, 1f);
        float sustainUntil = MathF.Max(0f, duration - releaseSeconds);
        float release = releaseSeconds <= 0f || t <= sustainUntil
            ? 1f
            : Math.Clamp((duration - t) / releaseSeconds, 0f, 1f);

        float shapedAttack = attack * attack;
        float shapedRelease = release * release;
        return shapedAttack * shapedRelease;
    }

    private static Color GetAccentColor(string ancientId, int fallbackIndex)
    {
        return ancientId switch
        {
            "OROBAS" => new Color(0.47f, 0.86f, 0.98f, 1f),
            "PAEL" => new Color(0.93f, 0.82f, 0.56f, 1f),
            "TEZCATARA" => new Color(0.98f, 0.55f, 0.22f, 1f),
            _ => fallbackIndex switch
            {
                0 => new Color(0.47f, 0.86f, 0.98f, 1f),
                1 => new Color(0.93f, 0.82f, 0.56f, 1f),
                _ => new Color(0.98f, 0.55f, 0.22f, 1f),
            },
        };
    }

    private static Vector2[] BuildLineQuad(Vector2 a, Vector2 b, float thickness)
    {
        Vector2 delta = b - a;
        Vector2 normal = delta.LengthSquared() <= 0.001f
            ? new Vector2(thickness * 0.5f, 0f)
            : delta.Orthogonal().Normalized() * (thickness * 0.5f);

        return new[]
        {
            a - normal,
            a + normal,
            b + normal,
            b - normal,
        };
    }

    private static void SetFullRect(Control control)
    {
        control.LayoutMode = 1;
        control.AnchorLeft = 0f;
        control.AnchorTop = 0f;
        control.AnchorRight = 1f;
        control.AnchorBottom = 1f;
        control.OffsetLeft = 0f;
        control.OffsetTop = 0f;
        control.OffsetRight = 0f;
        control.OffsetBottom = 0f;
        control.GrowHorizontal = Control.GrowDirection.Both;
        control.GrowVertical = Control.GrowDirection.Both;
    }

    private static void ClearChildren(Node parent)
    {
        foreach (Node child in parent.GetChildren())
        {
            child.QueueFree();
        }
    }

    private static readonly StringName RtNormalFont = "normal_font";
    private static readonly StringName RtBoldFont = "bold_font";
    private static readonly StringName RtItalicsFont = "italics_font";

    private static readonly StringName RtNormalFontSize = "normal_font_size";
    private static readonly StringName RtBoldFontSize = "bold_font_size";
    private static readonly StringName RtBoldItalicsFontSize = "bold_italics_font_size";
    private static readonly StringName RtItalicsFontSize = "italics_font_size";
    private static readonly StringName RtMonoFontSize = "mono_font_size";

    private static readonly StringName RtDefaultColor = "default_color";
    private static readonly StringName RtOutlineColor = "font_outline_color";
    private static readonly StringName RtShadowColor = "font_shadow_color";
    
    private void SetRoundIntroTextStyled(string text)
    {
        if (_roundIntroLabel == null)
        {
            return;
        }

        string upper = text.ToUpperInvariant();

        Font font = _roundIntroLabel.GetThemeFont(RtNormalFont, "RichTextLabel");
        int fontSize = _roundIntroLabel.GetThemeFontSize(RtNormalFontSize, "RichTextLabel");

        _roundIntroLabel.BbcodeEnabled = true;
        _roundIntroLabel.Call("InstallEffectsIfNeeded");

        Godot.Collections.Array effects = _roundIntroLabel.CustomEffects;

        if (_roundIntroBannerEffect != null && effects.Contains(_roundIntroBannerEffect))
        {
            effects.Remove(_roundIntroBannerEffect);
        }

        _roundIntroBannerEffect = new RichTextAncientBanner
        {
            CenterCharacter = GetTextCenterGlyphIndex(upper, font, fontSize),
            Rotation = 0.05f,
            Spacing = 650f
        };

        effects.Add(_roundIntroBannerEffect);
        _roundIntroLabel.CustomEffects = effects;

        _roundIntroLabel.SetTextAutoSize($"[ancient_banner]{upper}[/ancient_banner]");
    } 

    private static float GetTextCenterGlyphIndex(string text, Font font, int fontSize)
    {
        using TextParagraph paragraph = new();
        paragraph.AddString(text, font, fontSize);

        TextServer textServer = TextServerManager.Singleton.GetPrimaryInterface();
        Godot.Collections.Array<Godot.Collections.Dictionary> glyphs =
            textServer.ShapedTextGetGlyphs(paragraph.GetLineRid(0));

        float totalWidth = 0f;
        foreach (Godot.Collections.Dictionary glyph in glyphs)
        {
            totalWidth += glyph.GetValueOrDefault("advance").AsSingle();
        }

        float traversedWidth = 0f;
        int glyphIndex = 0;

        foreach (Godot.Collections.Dictionary glyph in glyphs)
        {
            float advance = glyph.GetValueOrDefault("advance").AsSingle();
            traversedWidth += advance;

            if (traversedWidth > totalWidth * 0.5f)
            {
                return glyphIndex + (totalWidth * 0.5f - (traversedWidth - advance)) / advance;
            }

            glyphIndex++;
        }

        return 0f;
    }
}