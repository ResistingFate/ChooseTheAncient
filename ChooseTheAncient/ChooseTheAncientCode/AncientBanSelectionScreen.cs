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
    private readonly TaskCompletionSource<int> _completion = new();
    private readonly IReadOnlyList<AncientEventModel> _pool;
    private readonly int _nextActIndex;

    private Button? _firstButton;
    private bool _resolved;

    public NetScreenType ScreenType => NetScreenType.Rewards;
    public bool UseSharedBackstop => true;
    public Control? DefaultFocusedControl => _firstButton;

    public AncientBanSelectionScreen(IReadOnlyList<AncientEventModel> pool, int nextActIndex)
    {
        _pool = pool;
        _nextActIndex = nextActIndex;

        Name = $"AncientBanSelection_{nextActIndex}";
        ProcessMode = ProcessModeEnum.Always;
        MouseFilter = MouseFilterEnum.Stop;
        FocusMode = FocusModeEnum.All;

        SetAnchorsPreset(LayoutPreset.FullRect);
        OffsetLeft = 0;
        OffsetTop = 0;
        OffsetRight = 0;
        OffsetBottom = 0;
    }

    public static async Task<int> ShowAndWait(IReadOnlyList<AncientEventModel> pool, int nextActIndex)
    {
        AncientBanSelectionScreen screen = new(pool, nextActIndex);
        NOverlayStack.Instance.Push(screen);
        return await screen._completion.Task;
    }

    public override void _Ready()
    {
        BuildUi();
        GrabInitialFocus();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // Deliberately do not allow cancel/back in v1.
        // The player must choose one ancient to ban.
        if (_resolved)
        {
            return;
        }

        if (@event.IsActionPressed("ui_cancel"))
        {
            AcceptEvent();
        }
    }

    private void BuildUi()
    {
        ColorRect dim = new()
        {
            Color = new Color(0, 0, 0, 0.78f),
            MouseFilter = MouseFilterEnum.Stop
        };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(dim);

        MarginContainer outer = new();
        outer.SetAnchorsPreset(LayoutPreset.FullRect);
        outer.AddThemeConstantOverride("margin_left", 120);
        outer.AddThemeConstantOverride("margin_top", 80);
        outer.AddThemeConstantOverride("margin_right", 120);
        outer.AddThemeConstantOverride("margin_bottom", 80);
        AddChild(outer);

        PanelContainer panel = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        outer.AddChild(panel);

        MarginContainer panelPadding = new();
        panelPadding.AddThemeConstantOverride("margin_left", 24);
        panelPadding.AddThemeConstantOverride("margin_top", 24);
        panelPadding.AddThemeConstantOverride("margin_right", 24);
        panelPadding.AddThemeConstantOverride("margin_bottom", 24);
        panel.AddChild(panelPadding);

        VBoxContainer root = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        root.AddThemeConstantOverride("separation", 20);
        panelPadding.AddChild(root);

        Label title = new()
        {
            Text = _nextActIndex == 1
                ? "Choose 1 Ancient to Remove Before Act 2"
                : "Choose 1 Ancient to Remove Before Act 3",
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        root.AddChild(title);

        Label subtitle = new()
        {
            Text = "The removed ancient cannot spawn at the start of the next act.",
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        root.AddChild(subtitle);

        HBoxContainer choicesRow = new()
        {
            Alignment = BoxContainer.AlignmentMode.Center,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        choicesRow.AddThemeConstantOverride("separation", 24);
        root.AddChild(choicesRow);

        for (int i = 0; i < _pool.Count; i++)
        {
            choicesRow.AddChild(BuildChoiceCard(_pool[i], i));
        }
    }

    private Control BuildChoiceCard(AncientEventModel ancient, int index)
    {
        PanelContainer card = new()
        {
            CustomMinimumSize = new Vector2(240, 0),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };

        MarginContainer cardPadding = new();
        cardPadding.AddThemeConstantOverride("margin_left", 16);
        cardPadding.AddThemeConstantOverride("margin_top", 16);
        cardPadding.AddThemeConstantOverride("margin_right", 16);
        cardPadding.AddThemeConstantOverride("margin_bottom", 16);
        card.AddChild(cardPadding);

        VBoxContainer box = new()
        {
            Alignment = BoxContainer.AlignmentMode.Center,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        box.AddThemeConstantOverride("separation", 12);
        cardPadding.AddChild(box);

        TextureRect icon = new()
        {
            CustomMinimumSize = new Vector2(128, 128),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered
        };
        icon.Texture = ancient.MapIcon;
        box.AddChild(icon);

        Label name = new()
        {
            Text = ancient.Title.GetFormattedText(),
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        box.AddChild(name);

        // Some models may have Epithet, some may not depending on version.
        // Safe fallback: only show title if you hit compile issues here.
        try
        {
            Label epithet = new()
            {
                Text = ancient.Epithet.GetFormattedText(),
                HorizontalAlignment = HorizontalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.WordSmart
            };
            box.AddChild(epithet);
        }
        catch
        {
            // Intentionally ignore if the property is absent or changed.
        }

        Button chooseButton = new()
        {
            Text = "Ban This Ancient",
            FocusMode = FocusModeEnum.All,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        chooseButton.Pressed += () => Select(index);
        box.AddChild(chooseButton);

        _firstButton ??= chooseButton;
        return card;
    }

    private void GrabInitialFocus()
    {
        if (_firstButton == null)
        {
            return;
        }

        _firstButton.CallDeferred(Button.MethodName.GrabFocus);
    }

    private void Select(int bannedIndex)
    {
        if (_resolved)
        {
            return;
        }

        _resolved = true;
        _completion.TrySetResult(bannedIndex);

        // Depending on exact overlay API version, this may be Remove(this) or Pop(this).
        NOverlayStack.Instance.Remove(this);
    }

    public override void _ExitTree()
    {
        if (!_completion.Task.IsCompleted)
        {
            _completion.TrySetCanceled();
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