Pushed as an overlay with `NOverlayStack.Instance.Push(screen)` then add Zindex to elements
- the layout root scene starts at z_index = -2
- then each SlotRoot is set to ZIndex = 2
- CardRoot is ZIndex = 2
- HoverFlashPolygon is ZIndex = 2
- PreviewAnchor is ZIndex = 3
- each preview wrapper is ZIndex = 3
- each preview button is ZIndex = 3
- ReactionAnchor is ZIndex = 4
- VoteIconsAnchor is ZIndex = 8
- VoteContainer is also ZIndex = 8
- your hover tips are even more aggressive: hoverTipSet.ZIndex = 9 and hoverTipSet.TopLevel = true

With relative z left at the normal defaults, that means the effective draw depth of some descendants gets well above 0. Roughly:
- ScenePolygon: -2 + 2 + 0 = 0
- GlowPolygon: -2 + 2 + 1 = 1
- HoverFlashPolygon: -2 + 2 + 2 = 2
- ChooseButton: -2 + 2 + 2 = 2
- CardOutline: -2 + 2 + 2 + 5 = 7
- PreviewButton: -2 + 2 + 2 + 3 + 3 + 3 = 11
- VoteContainer: -2 + 2 + 2 + 8 + 8 = 18
- I made RemoteCursors ZIndex = 1000 as it would go under vote buttons