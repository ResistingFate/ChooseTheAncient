// Unused as can't attach scripts to root nodes via godot and have the came recognize them
using System;
using Godot;
using MegaCrit.Sts2.Core.Models;

namespace ChooseTheAncient.ChooseTheAncientCode;
[GlobalClass]
public sealed partial class AncientBanChoiceCard : PanelContainer
{
    public Button ChooseButton { get; private set; } = null!;

    private TextureRect? _icon;
    private Label? _nameLabel;
    private Label? _epithetLabel;

    private AncientEventModel? _pendingAncient;
    private Action? _pendingOnPressed;
    private bool _isReady;
    private bool _isApplied;

    public override void _Ready()
    {
        _icon = GetNode<TextureRect>("Padding/VBox/Icon");
        _nameLabel = GetNode<Label>("Padding/VBox/NameLabel");
        _epithetLabel = GetNode<Label>("Padding/VBox/EpithetLabel");
        ChooseButton = GetNode<Button>("Padding/VBox/ChooseButton");

        _isReady = true;
        TryApplySetup();
    }

    public void Setup(AncientEventModel ancient, Action onPressed)
    {
        _pendingAncient = ancient;
        _pendingOnPressed = onPressed;
        TryApplySetup();
    }

    private void TryApplySetup()
    {
        if (!_isReady || _isApplied || _pendingAncient == null || _pendingOnPressed == null)
        {
            return;
        }

        _icon!.Texture = _pendingAncient.MapIcon;
        _nameLabel!.Text = _pendingAncient.Title.GetFormattedText();

        try
        {
            _epithetLabel!.Text = _pendingAncient.Epithet.GetFormattedText();
        }
        catch
        {
            _epithetLabel!.Text = "";
            _epithetLabel!.Visible = false;
        }

        ChooseButton.Text = "Ban This Ancient";
        ChooseButton.Pressed += _pendingOnPressed;

        _isApplied = true;
    }
}