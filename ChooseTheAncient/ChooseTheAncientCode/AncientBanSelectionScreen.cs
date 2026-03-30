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

    private const float HoverSceneScaleMultiplier = 1.035f;
    private const float HoverCardScaleMultiplier = 1.015f;

    private readonly record struct AncientSceneConfig(
        Vector2 BaseSize,
        float Scale,
        Vector2 SourceAnchor01,
        Vector2 ExtraOffset01);

    private sealed class SlotRefs
    {
        public required int PoolIndex { get; init; }
        public required AncientEventModel Ancient { get; init; }
        public required Control SlotRoot { get; init; }
        public required Control SceneClip { get; init; }
        public required Control SceneMount { get; init; }
        public required ColorRect Glow { get; init; }
        public required ColorRect HoverFlash { get; init; }
        public required Control CardRoot { get; init; }
        public required ColorRect CardShade { get; init; }
        public required TextureRect Icon { get; init; }
        public required Label NameLabel { get; init; }
        public required Label EpithetLabel { get; init; }
        public required Button ChooseButton { get; init; }

        public Node? SceneRoot { get; set; }
        public Vector2 BasePosition { get; set; }
        public Vector2 BaseSize { get; set; }
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
        new(Vector2.Zero, 1.0f, new Vector2(0.5f, 0.0f), Vector2.Zero);

    private static readonly Dictionary<string, AncientSceneConfig> AncientSceneConfigs = new()
    {
        ["DARV"] = DefaultAncientSceneConfig,
        ["OROBAS"] = DefaultAncientSceneConfig,
        ["PAEL"] = DefaultAncientSceneConfig,
        ["TEZCATARA"] = DefaultAncientSceneConfig,
        ["NONUPEIPE"] = DefaultAncientSceneConfig,
        ["TANX"] = DefaultAncientSceneConfig,
        ["VAKUU"] = DefaultAncientSceneConfig,
        ["NEOW"] = DefaultAncientSceneConfig,
    };

    private readonly TaskCompletionSource<int> _voteSubmitted = new();
    private readonly List<SlotRefs> _slots = new();

    private IReadOnlyList<AncientEventModel> _pool = Array.Empty<AncientEventModel>();
    private int _nextActIndex;
    private bool _resolved;
    private bool _closing;
    private SlotRefs? _hoveredSlot;
    private int? _lastHoveredPoolIndex;

    private Control? _layoutRoot;
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
        MouseFilter = MouseFilterEnum.Stop;
        FocusMode = FocusModeEnum.All;

        SetFullRect(this);
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

    public Task<int> WaitForVoteAsync() => _voteSubmitted.Task;

    public override void _Ready()
    {
        PackedScene? layoutScene = GD.Load<PackedScene>(LayoutScenePath);
        if (layoutScene == null)
        {
            throw new InvalidOperationException($"Could not load layout scene: {LayoutScenePath}");
        }

        _layoutRoot = layoutScene.Instantiate<Control>();
        SetFullRect(_layoutRoot);
        _layoutRoot.ZIndex = 0;
        AddChild(_layoutRoot);

        _titleLabel = _layoutRoot.GetNode<Label>("TopBox/TitleLabel");
        _subtitleLabel = _layoutRoot.GetNode<Label>("TopBox/SubtitleLabel");
        _stageArea = _layoutRoot.GetNode<Control>("StageMargin/StageArea");
        _slotsCanvas = _layoutRoot.GetNode<Control>("StageMargin/StageArea/SlotsCanvas");
        _hoverSfx = _layoutRoot.GetNodeOrNull<AudioStreamPlayer>("HoverSfx");
        _clickSfx = _layoutRoot.GetNodeOrNull<AudioStreamPlayer>("ClickSfx");

        _stageArea.ClipContents = true;
        _stageArea.ZIndex = 0;
        _slotsCanvas.ClipContents = true;
        _slotsCanvas.ZIndex = 0;

        _titleLabel.ZIndex = 100;
        _subtitleLabel.ZIndex = 100;

        _titleLabel.Text = _nextActIndex == 1
            ? "Choose 1 Ancient to Remove Before Act 2"
            : "Choose 1 Ancient to Remove Before Act 3";
        _subtitleLabel.Text = "Each player votes for 1 ancient to remove. Majority bans it; ties are broken randomly.";

        _stageArea.Resized += RefreshLayout;

        CallDeferred(nameof(BuildUi));
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
        }

        RefreshLayout();
        GrabInitialFocus();
    }

    private SlotRefs CreateSlot(AncientEventModel ancient, int poolIndex, PackedScene cardScene)
    {
        Control slotRoot = new()
        {
            Name = $"AncientSlot_{ancient.Id.Entry}",
            MouseFilter = MouseFilterEnum.Ignore,
            ClipContents = true,
            FocusMode = FocusModeEnum.None,
            ZIndex = 0
        };

        Control sceneClip = new()
        {
            Name = "SceneClip",
            MouseFilter = MouseFilterEnum.Ignore,
            ClipContents = true,
            FocusMode = FocusModeEnum.None,
            ZIndex = 0
        };
        SetFullRect(sceneClip);
        slotRoot.AddChild(sceneClip);

        Control sceneMount = new()
        {
            Name = "SceneMount",
            MouseFilter = MouseFilterEnum.Ignore,
            FocusMode = FocusModeEnum.None,
            ZIndex = 0
        };
        sceneMount.AnchorLeft = 0f;
        sceneMount.AnchorTop = 0f;
        sceneMount.AnchorRight = 0f;
        sceneMount.AnchorBottom = 0f;
        sceneMount.OffsetLeft = 0f;
        sceneMount.OffsetTop = 0f;
        sceneMount.OffsetRight = 0f;
        sceneMount.OffsetBottom = 0f;
        sceneMount.Position = Vector2.Zero;
        sceneMount.Size = Vector2.Zero;
        sceneMount.Scale = Vector2.One;
        sceneClip.AddChild(sceneMount);

        ColorRect glow = new()
        {
            Name = "Glow",
            Color = new Color(1f, 0.72f, 0.25f, 0f),
            MouseFilter = MouseFilterEnum.Ignore,
            ZIndex = 1
        };
        SetFullRect(glow);
        sceneClip.AddChild(glow);

        ColorRect hoverFlash = new()
        {
            Name = "HoverFlash",
            Color = new Color(1f, 1f, 1f, 0f),
            MouseFilter = MouseFilterEnum.Ignore,
            ZIndex = 2
        };
        SetFullRect(hoverFlash);
        sceneClip.AddChild(hoverFlash);

        Control cardRoot = cardScene.Instantiate<Control>();
        cardRoot.Name = $"AncientChoice_{ancient.Id.Entry}";
        cardRoot.ClipContents = true;
        cardRoot.ZIndex = 3;
        slotRoot.AddChild(cardRoot);

        ColorRect cardShade = cardRoot.GetNode<ColorRect>("BottomShade");
        TextureRect icon = cardRoot.GetNode<TextureRect>("Padding/VBox/Header/Icon");
        Label nameLabel = cardRoot.GetNode<Label>("Padding/VBox/Header/TextBox/NameLabel");
        Label epithetLabel = cardRoot.GetNode<Label>("Padding/VBox/Header/TextBox/EpithetLabel");
        Button chooseButton = cardRoot.GetNode<Button>("Padding/VBox/ChooseButton");

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
            SceneClip = sceneClip,
            SceneMount = sceneMount,
            Glow = glow,
            HoverFlash = hoverFlash,
            CardRoot = cardRoot,
            CardShade = cardShade,
            Icon = icon,
            NameLabel = nameLabel,
            EpithetLabel = epithetLabel,
            ChooseButton = chooseButton
        };
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
            ApplySceneTransform(refs, hovered: false);
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

        float count = _slots.Count;
        float slotWidth = area.X / count;
        float slotHeight = area.Y;
        float cardHeight = MathF.Round(slotHeight / 3f);
        float cardTop = slotHeight - cardHeight;

        for (int i = 0; i < _slots.Count; i++)
        {
            SlotRefs refs = _slots[i];

            Vector2 slotPos = new(i * slotWidth, 0f);
            Vector2 slotSize = new(slotWidth, slotHeight);

            refs.BasePosition = slotPos;
            refs.BaseSize = slotSize;

            refs.SlotRoot.Position = slotPos;
            refs.SlotRoot.Size = slotSize;
            refs.SlotRoot.PivotOffset = slotSize * 0.5f;

            refs.SceneClip.Position = Vector2.Zero;
            refs.SceneClip.Size = slotSize;

            refs.CardRoot.Position = new Vector2(0f, cardTop);
            refs.CardRoot.Size = new Vector2(slotWidth, cardHeight);
            refs.CardRoot.PivotOffset = refs.CardRoot.Size * 0.5f;

            ApplySceneTransform(refs, hovered: ReferenceEquals(_hoveredSlot, refs));
        }
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

    private void ApplySceneTransform(SlotRefs refs, bool hovered)
    {
        if (refs.SceneRoot == null)
        {
            return;
        }

        AncientSceneConfig cfg = GetSceneConfig(refs.Ancient.Id.Entry);
        Vector2 baseSize = ResolveSceneBaseSize(refs, cfg);
        float appliedScale = cfg.Scale * (hovered ? HoverSceneScaleMultiplier : 1.0f);

        Vector2 slotAnchorPx = new(refs.BaseSize.X * 0.5f, 0f);
        Vector2 sourceAnchorPx = new(
            baseSize.X * appliedScale * cfg.SourceAnchor01.X,
            baseSize.Y * appliedScale * cfg.SourceAnchor01.Y);
        Vector2 extraPx = new(
            refs.BaseSize.X * cfg.ExtraOffset01.X,
            refs.BaseSize.Y * cfg.ExtraOffset01.Y);

        refs.SceneMount.Size = baseSize;
        refs.SceneMount.Position = slotAnchorPx - sourceAnchorPx + extraPx;
        refs.SceneMount.Scale = Vector2.One * appliedScale;
        refs.SceneMount.PivotOffset = Vector2.Zero;
        refs.SceneMount.ZIndex = 0;

        if (refs.SceneRoot is CanvasItem canvasItem)
        {
            canvasItem.ZIndex = 0;
            canvasItem.ShowBehindParent = true;
        }
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
        SlotRefs? refs = _slots.Find(s => s.PoolIndex == poolIndex);
        if (refs == null)
        {
            return;
        }

        _hoveredSlot = refs;
        PlayHoverSoundIfNeeded(poolIndex);

        Tween tween = CreateTween();
        tween.TweenProperty(refs.Glow, "color:a", 0.14f, 0.10f);
        tween.Parallel().TweenProperty(refs.HoverFlash, "color:a", 0.05f, 0.06f);
        tween.Parallel().TweenProperty(refs.CardShade, "color:a", 0.08f, 0.10f);
        tween.Parallel().TweenProperty(refs.CardRoot, "scale", new Vector2(HoverCardScaleMultiplier, HoverCardScaleMultiplier), 0.10f);

        ApplySceneTransform(refs, hovered: true);
    }

    private void OnSlotUnhovered(int poolIndex)
    {
        SlotRefs? refs = _slots.Find(s => s.PoolIndex == poolIndex);
        if (refs == null)
        {
            return;
        }

        if (ReferenceEquals(_hoveredSlot, refs))
        {
            _hoveredSlot = null;
        }

        _lastHoveredPoolIndex = null;

        Tween tween = CreateTween();
        tween.TweenProperty(refs.Glow, "color:a", 0.0f, 0.10f);
        tween.Parallel().TweenProperty(refs.HoverFlash, "color:a", 0.0f, 0.08f);
        tween.Parallel().TweenProperty(refs.CardShade, "color:a", 0.18f, 0.10f);
        tween.Parallel().TweenProperty(refs.CardRoot, "scale", Vector2.One, 0.10f);

        ApplySceneTransform(refs, hovered: false);
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

        if (_clickSfx?.Stream != null)
        {
            _clickSfx.Stop();
            _clickSfx.Play();
        }

        foreach (SlotRefs refs in _slots)
        {
            refs.ChooseButton.Disabled = true;

            if (refs.PoolIndex == bannedIndex)
            {
                refs.CardShade.Color = new Color(0f, 0f, 0f, 0.08f);
                refs.SlotRoot.Modulate = Colors.White;
            }
            else
            {
                refs.CardShade.Color = new Color(0f, 0f, 0f, 0.32f);
                refs.SlotRoot.Modulate = new Color(1f, 1f, 1f, 0.48f);
            }
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