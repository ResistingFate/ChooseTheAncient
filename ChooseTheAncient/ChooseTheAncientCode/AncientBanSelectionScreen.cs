using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;

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

    public enum VoteRoundType
    {
        InitialKeepVote,
        FinalRevealVote,
    }

    public readonly record struct RoundDefinition(
        IReadOnlyList<AncientEventModel> Pool,
        VoteRoundType RoundType,
        IReadOnlyDictionary<string, AncientBanHelpers.AncientPreviewData>? PreviewDataByAncientId);

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
        public required TextureRect Icon { get; init; }
        public required Label NameLabel { get; init; }
        public required Label EpithetLabel { get; init; }
        public required Label InfoLabel { get; init; }
        public required Button ChooseButton { get; init; }
        public required Control PreviewAnchor { get; init; }
        public required Color AccentColor { get; init; }

        public Node? SceneRoot { get; set; }
        public Vector2 BaseSize { get; set; }
        public Vector2 CardBasePosition { get; set; }
        public PortalShape Shape { get; set; }
        public List<Control> PreviewWrappers { get; } = new();
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

    private static AudioStreamWav? _generatedHoverStream;
    private static AudioStreamWav? _generatedClickStream;

    private readonly List<SlotRefs> _slots = new();
    private readonly TaskCompletionSource<bool> _readyCompletion = new();

    private TaskCompletionSource<int> _voteSubmitted = new();

    private IReadOnlyList<AncientEventModel> _pool = Array.Empty<AncientEventModel>();
    private VoteRoundType _roundType = VoteRoundType.InitialKeepVote;
    private Dictionary<string, AncientBanHelpers.AncientPreviewData> _previewDataByAncientId = new();
    private int _nextActIndex;
    private int? _pendingPoolIndex;
    private int? _selectedPoolIndex;
    private bool _resolved;
    private bool _closing;
    private bool _uiReady;
    private bool _hasLoadedRound;
    private SlotRefs? _hoveredSlot;
    private int? _lastHoveredPoolIndex;

    private Control? _layoutRoot;
    private Control? _headerPanel;
    private Control? _footerPanel;
    private Label? _titleLabel;
    private Label? _subtitleLabel;
    private Control? _stageArea;
    private Control? _slotsCanvas;
    private AudioStreamPlayer? _hoverSfx;
    private AudioStreamPlayer? _clickSfx;

    public NetScreenType ScreenType => NetScreenType.Rewards;
    public bool UseSharedBackstop => true;
    public Control? DefaultFocusedControl { get; private set; }

    public AncientBanSelectionScreen()
    {
        Name = "AncientBanSelectionScreen";
        ProcessMode = ProcessModeEnum.Always;
        MouseFilter = MouseFilterEnum.Ignore;
        FocusMode = FocusModeEnum.All;
        SetFullRect(this);
    }

    public static AncientBanSelectionScreen Show(int nextActIndex)
    {
        AncientBanSelectionScreen screen = new();
        screen.Initialize(nextActIndex);
        NOverlayStack.Instance.Push(screen);
        return screen;
    }

    public void Initialize(int nextActIndex)
    {
        _nextActIndex = nextActIndex;
    }

    public async Task<int> RunRoundAsync(RoundDefinition round)
    {
        await _readyCompletion.Task;

        _voteSubmitted = new TaskCompletionSource<int>();
        _pool = round.Pool;
        _roundType = round.RoundType;
        _previewDataByAncientId = round.PreviewDataByAncientId?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
            ?? new Dictionary<string, AncientBanHelpers.AncientPreviewData>();
        _pendingPoolIndex = null;
        _selectedPoolIndex = null;
        _resolved = false;
        _hoveredSlot = null;
        _lastHoveredPoolIndex = null;

        await ApplyRoundAsync();
        return await _voteSubmitted.Task;
    }

    public override void _Ready()
    {
        PackedScene? layoutScene = GD.Load<PackedScene>(LayoutScenePath);
        if (layoutScene == null)
        {
            throw new InvalidOperationException($"Could not load layout scene: {LayoutScenePath}");
        }

        _layoutRoot = layoutScene.Instantiate<Control>();
        SetFullRect(_layoutRoot);
        AddChild(_layoutRoot);

        _headerPanel = _layoutRoot.GetNode<Control>("HeaderPanel");
        _footerPanel = _layoutRoot.GetNodeOrNull<Control>("FooterPanel");
        _titleLabel = _layoutRoot.GetNode<Label>("HeaderPanel/HeaderPadding/TopBox/TitleLabel");
        _subtitleLabel = _layoutRoot.GetNode<Label>("HeaderPanel/HeaderPadding/TopBox/SubtitleLabel");
        _stageArea = _layoutRoot.GetNode<Control>("StageMargin/StageArea");
        _slotsCanvas = _layoutRoot.GetNode<Control>("StageMargin/StageArea/SlotsCanvas");
        _hoverSfx = _layoutRoot.GetNodeOrNull<AudioStreamPlayer>("HoverSfx");
        _clickSfx = _layoutRoot.GetNodeOrNull<AudioStreamPlayer>("ClickSfx");

        _layoutRoot.MouseFilter = MouseFilterEnum.Ignore;

        TryInstallGeneratedSounds();

        if (_headerPanel != null)
        {
            _headerPanel.OffsetTop = 96f;
            _headerPanel.OffsetBottom = 184f;
            _headerPanel.ZIndex = 2;
        }

        if (_footerPanel != null)
        {
            _footerPanel.Visible = false;
        }

        _stageArea.ClipContents = true;
        _slotsCanvas.ClipContents = false;
        _stageArea.MouseFilter = MouseFilterEnum.Ignore;
        _slotsCanvas.MouseFilter = MouseFilterEnum.Ignore;
        _stageArea.Resized += RefreshLayout;

        _uiReady = true;
        _readyCompletion.TrySetResult(true);
    }

    private async Task ApplyRoundAsync()
    {
        UpdateRoundText();

        if (!_uiReady)
        {
            return;
        }

        if (!_hasLoadedRound)
        {
            BuildUi();
            _hasLoadedRound = true;
            return;
        }

        await AnimateOutSlotsAsync();
        BuildUi();
        PrimeSlotsForTransitionIn();
        await AnimateInSlotsAsync();
    }

    private async Task AnimateOutSlotsAsync()
    {
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
        GrabInitialFocus();
    }

    private void UpdateRoundText()
    {
        if (_titleLabel == null || _subtitleLabel == null)
        {
            return;
        }

        string actLabel = _nextActIndex == 1 ? "Act 2" : "Act 3";

        if (_roundType == VoteRoundType.InitialKeepVote)
        {
            _titleLabel.Text = $"Choose the {actLabel} Finalists";
            _subtitleLabel.Text = "Vote once. The least-voted ancient is eliminated, and ties for last are broken with this mod's deterministic RNG.";
        }
        else
        {
            _titleLabel.Text = $"Final Ancient Vote Before {actLabel}";
            _subtitleLabel.Text = "The final 2 ancients now reveal the rewards they would offer. Vote once to lock in the ancient you want next act.";
        }
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

        ClearChildren(_slotsCanvas);
        _slots.Clear();
        DefaultFocusedControl = null;

        for (int i = 0; i < _pool.Count; i++)
        {
            AncientEventModel ancient = _pool[i];
            SlotRefs refs = CreateSlot(ancient, i, cardScene);
            _slotsCanvas.AddChild(refs.SlotRoot);
            _slots.Add(refs);
            DefaultFocusedControl ??= refs.ChooseButton;
            LoadAncientScene(refs);
            PopulatePreview(refs);
        }

        RefreshLayout();
        RefreshSlotVisuals(animate: false);
        RefreshButtonTexts();
        GrabInitialFocus();
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

        Control cardRoot = cardScene.Instantiate<Control>();
        cardRoot.Name = $"AncientChoice_{ancient.Id.Entry}";
        cardRoot.ClipContents = false;
        cardRoot.ZIndex = 2;
        slotRoot.AddChild(cardRoot);

        ColorRect cardShade = cardRoot.GetNode<ColorRect>("BottomShade");
        ColorRect topAccent = cardRoot.GetNode<ColorRect>("TopAccent");
        TextureRect icon = cardRoot.GetNode<TextureRect>("Padding/VBox/Header/Icon");
        Label nameLabel = cardRoot.GetNode<Label>("Padding/VBox/Header/TextBox/NameLabel");
        Label epithetLabel = cardRoot.GetNode<Label>("Padding/VBox/Header/TextBox/EpithetLabel");
        Label infoLabel = cardRoot.GetNode<Label>("Padding/VBox/InfoLabel");
        Button chooseButton = cardRoot.GetNode<Button>("Padding/VBox/ChooseButton");
        Control previewAnchor = cardRoot.GetNode<Control>("PreviewAnchor");

        previewAnchor.ZIndex = 3;
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
        chooseButton.FocusEntered += () => OnSlotHovered(poolIndex);
        chooseButton.FocusExited += () => OnSlotUnhovered(poolIndex);
        cardRoot.MouseEntered += () => OnSlotHovered(poolIndex);
        cardRoot.MouseExited += () => OnSlotUnhovered(poolIndex);

        int capturedIndex = poolIndex;
        chooseButton.Pressed += () => Select(capturedIndex);

        return new SlotRefs
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
            Icon = icon,
            NameLabel = nameLabel,
            EpithetLabel = epithetLabel,
            InfoLabel = infoLabel,
            ChooseButton = chooseButton,
            PreviewAnchor = previewAnchor,
            AccentColor = accentColor,
            Shape = default,
        };
    }

    private void PopulatePreview(SlotRefs refs)
    {
        ClearChildren(refs.PreviewAnchor);
        refs.PreviewWrappers.Clear();

        if (_roundType != VoteRoundType.FinalRevealVote)
        {
            refs.PreviewAnchor.Visible = false;
            return;
        }

        if (!_previewDataByAncientId.TryGetValue(refs.Ancient.Id.Entry, out AncientBanHelpers.AncientPreviewData? preview))
        {
            GD.Print($"[ChooseTheAncient] No preview data found for {refs.Ancient.Id.Entry}.");
            refs.PreviewAnchor.Visible = false;
            return;
        }

        GD.Print($"[ChooseTheAncient] Building preview UI for {refs.Ancient.Id.Entry} with {preview.Options.Count} option(s).");

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
                MouseFilter = MouseFilterEnum.Ignore,
                FocusMode = FocusModeEnum.None,
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
            refs.PreviewWrappers.Add(previewWrapper);

            GD.Print($"[ChooseTheAncient] Added preview widget {i} for {refs.Ancient.Id.Entry}: relic={(option.Relic?.Id.Entry ?? "<none>")}, textKey={option.TextKey}");
        }
    }

    private void LoadAncientScene(SlotRefs refs)
    {
        ClearChildren(refs.SceneMount);
        refs.SceneRoot = null;

        if (!AncientScenePaths.TryGetValue(refs.Ancient.Id.Entry, out string? scenePath))
        {
            GD.Print($"[ChooseTheAncient] No ancient scene mapping for {refs.Ancient.Id.Entry}; using overlay only.");
            return;
        }

        PackedScene? scene = GD.Load<PackedScene>(scenePath);
        if (scene == null)
        {
            GD.Print($"[ChooseTheAncient] Could not load ancient scene at {scenePath}");
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
            GD.PrintErr($"[ChooseTheAncient] Failed to instantiate ancient scene {scenePath}: {ex}");
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

            ApplyPortalGeometry(shape, refs);
            ApplySceneTransform(refs, hovered: !_resolved && ReferenceEquals(_hoveredSlot, refs), animate: false);
            LayoutPreview(refs);
        }
    }

    private void LayoutPreview(SlotRefs refs)
    {
        if (!refs.PreviewAnchor.Visible || refs.PreviewWrappers.Count == 0)
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
        float totalHeight = (refs.PreviewWrappers.Count * displayHeight) + (Math.Max(0, refs.PreviewWrappers.Count - 1) * gap);
        float startY = MathF.Max(0f, anchorSize.Y - totalHeight);

        GD.Print($"[ChooseTheAncient] Layout preview for {refs.Ancient.Id.Entry}: anchor={anchorSize}, wrappers={refs.PreviewWrappers.Count}, startY={startY}");

        for (int i = 0; i < refs.PreviewWrappers.Count; i++)
        {
            Control wrapper = refs.PreviewWrappers[i];
            wrapper.LayoutMode = 1;
            wrapper.AnchorLeft = 0f;
            wrapper.AnchorTop = 0f;
            wrapper.AnchorRight = 0f;
            wrapper.AnchorBottom = 0f;
            wrapper.Position = new Vector2(0f, startY + (i * (displayHeight + gap)));
            wrapper.Size = new Vector2(displayWidth / scaleX, displayHeight / scaleY);
            wrapper.Scale = new Vector2(scaleX, scaleY);
            wrapper.PivotOffset = Vector2.Zero;
        }
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
        PortalShape[] shapes = new PortalShape[count];
        float gap = Math.Max(20f, area.X * 0.02f);
        float portalWidth = (area.X - (gap * Math.Max(0, count - 1))) / Math.Max(1, count);
        float cardY = area.Y - cardHeight - CardBottomInset;

        for (int i = 0; i < count; i++)
        {
            float portalX = i * (portalWidth + gap);
            Rect2 portal = new(new Vector2(portalX, 0f), new Vector2(portalWidth, area.Y));
            float cardX = portalX + Math.Max(10f, (portalWidth - cardWidth) * 0.5f);
            Rect2 card = new(new Vector2(cardX, cardY), new Vector2(Math.Min(cardWidth, portalWidth - 20f), cardHeight));
            shapes[i] = new PortalShape(
                portal,
                card,
                new Vector2(portalWidth * 0.05f, 0f),
                new Vector2(portalWidth * 0.95f, 0f),
                new Vector2(portalWidth * 0.90f, area.Y),
                new Vector2(portalWidth * 0.10f, area.Y),
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

        if (!ReferenceEquals(_hoveredSlot, refs))
        {
            PlayHoverSoundIfNeeded(poolIndex);
        }

        _hoveredSlot = refs;
        RefreshSlotVisuals(animate: true);
    }

    private void OnSlotUnhovered(int poolIndex)
    {
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
            || refs.ChooseButton.HasFocus();

        if (stillHovering)
        {
            return;
        }

        _hoveredSlot = null;
        _lastHoveredPoolIndex = null;
        RefreshSlotVisuals(animate: true);
    }

    private void RefreshSlotVisuals(bool animate)
    {
        bool anyHovered = !_resolved && _hoveredSlot != null;

        foreach (SlotRefs refs in _slots)
        {
            bool hovered = anyHovered && ReferenceEquals(_hoveredSlot, refs);
            bool pendingSelected = !_resolved && _pendingPoolIndex == refs.PoolIndex;
            bool selected = _resolved && _selectedPoolIndex == refs.PoolIndex;

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

            Color glowColor = new(refs.AccentColor.R, refs.AccentColor.G, refs.AccentColor.B, glowAlpha);
            Color flashColor = new(1f, 1f, 1f, flashAlpha);
            Color shadeColor = new(0f, 0f, 0f, shadeAlpha);
            Color slotModulate = new(1f, 1f, 1f, slotAlpha);
            Color rimColor = new(refs.AccentColor.R, refs.AccentColor.G, refs.AccentColor.B, rimAlpha);
            Color accentColor = new(refs.AccentColor.R, refs.AccentColor.G, refs.AccentColor.B, accentAlpha);

            if (animate)
            {
                Tween tween = CreateTween();
                tween.SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
                tween.TweenProperty(refs.GlowPolygon, "color", glowColor, 0.10f);
                tween.Parallel().TweenProperty(refs.HoverFlashPolygon, "color", flashColor, 0.08f);
                tween.Parallel().TweenProperty(refs.CardShade, "color", shadeColor, 0.10f);
                tween.Parallel().TweenProperty(refs.ScenePolygon, "modulate", slotModulate, 0.10f);
                tween.Parallel().TweenProperty(refs.CardRoot, "modulate", slotModulate, 0.10f);
                tween.Parallel().TweenProperty(refs.PreviewAnchor, "modulate", slotModulate, 0.10f);
                tween.Parallel().TweenProperty(refs.LeftRim, "color", rimColor, 0.12f);
                tween.Parallel().TweenProperty(refs.RightRim, "color", rimColor, 0.12f);
                tween.Parallel().TweenProperty(refs.TopAccent, "color", accentColor, 0.12f);
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
            }

            ApplySceneTransform(refs, hovered, animate);
        }
    }

    private void RefreshButtonTexts()
    {
        foreach (SlotRefs refs in _slots)
        {
            bool resolvedSelected = _resolved && _selectedPoolIndex == refs.PoolIndex;

            refs.InfoLabel.Text = _roundType == VoteRoundType.InitialKeepVote
                ? "Vote once to keep this ancient in the final round."
                : "These previewed options are your local rewards for this ancient.";

            if (_resolved)
            {
                refs.ChooseButton.Disabled = true;
                refs.ChooseButton.Text = resolvedSelected ? "Vote Locked" : "Unavailable";
            }
            else
            {
                refs.ChooseButton.Disabled = false;
                refs.ChooseButton.Text = "Vote For This Ancient";
            }
        }
    }

    private void GrabInitialFocus()
    {
        DefaultFocusedControl?.CallDeferred(Control.MethodName.GrabFocus);
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

        if (_clickSfx?.Stream != null)
        {
            _clickSfx.Stop();
            _clickSfx.Play();
        }

        RefreshButtonTexts();
        RefreshSlotVisuals(animate: true);

        if (_subtitleLabel != null)
        {
            _subtitleLabel.Text = "Vote submitted. Waiting for the rest of the party...";
        }

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

        if (@event.IsActionPressed("ui_cancel"))
        {
            AcceptEvent();
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

    private void TryInstallGeneratedSounds()
    {
        try
        {
            if (_hoverSfx != null && _hoverSfx.Stream == null)
            {
                _generatedHoverStream ??= BuildUiTone(
                    baseFrequencyHz: 1046f,
                    overtoneFrequencyHz: 1396f,
                    durationSeconds: 0.05f,
                    peakAmplitude: 0.20f,
                    attackSeconds: 0.003f,
                    releaseSeconds: 0.036f,
                    noiseAmount: 0.015f);
                _hoverSfx.Stream = _generatedHoverStream;
            }

            if (_clickSfx != null && _clickSfx.Stream == null)
            {
                _generatedClickStream ??= BuildUiTone(
                    baseFrequencyHz: 196f,
                    overtoneFrequencyHz: 392f,
                    durationSeconds: 0.13f,
                    peakAmplitude: 0.34f,
                    attackSeconds: 0.002f,
                    releaseSeconds: 0.085f,
                    noiseAmount: 0.025f);
                _clickSfx.Stream = _generatedClickStream;
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[ChooseTheAncient] Failed to create fallback UI sounds: {ex}");
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
}