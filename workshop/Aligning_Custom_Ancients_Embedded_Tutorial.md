# Video Tutorials

```
Note: All steps are important. If your ancient doesn't allign correctly you might have
accidently made a mistake. Fastest way is just to start from the begining I find.
```

## Tutorial where I align this template ancient for the Choose The Acient Selection Screen

[![Aligning Focus Ancient 2 Simple Tutorial](https://img.youtube.com/vi/LANKDKYO1GM/maxresdefault.jpg)](https://www.youtube.com/watch?v=uaN_P9K37Dk)
[A more verbose version of the video is here](https://www.youtube.com/watch?v=LANKDKYO1GM)
### Step by Step guide

<details>
<summary>Show step-by-step guide</summary>


1. [0:00](https://www.youtube.com/watch?v=uaN_P9K37Dk&t=0s) — Open your scene, select the root node, then go to Layout → Transform → Size and record the values. In this example: (1152, 648).

2. [0:03](https://www.youtube.com/watch?v=uaN_P9K37Dk&t=3s) — Use those values for the baseSize variable in the ChooseTheAncientBaseSize property: (1152f, 648f). Add this to your Custom Ancient class. The same applies to BaseLib or RitsLib ancients.

3. [0:06](https://www.youtube.com/watch?v=uaN_P9K37Dk&t=6s) — Create a new scene in MegaDot. This is the most reliable way to choose the centre point for your ancient on the selection screen, regardless of scene type.

4. [0:09](https://www.youtube.com/watch?v=uaN_P9K37Dk&t=9s) — Add a new Control node as a child of the root. Instantiating the ancient scene under this Control node preserves its default MegaDot position.

5. [0:12](https://www.youtube.com/watch?v=uaN_P9K37Dk&t=12s) — Set the Control node’s Layout → Transform → Size to match your scene’s root node. In this example: (1152, 648).

6. [0:18](https://www.youtube.com/watch?v=uaN_P9K37Dk&t=18s) — Download the Choose the Ancient selection-screen templates. I placed them beside my scene, but they do not need to be compiled into your mod.

7. [0:23](https://www.youtube.com/watch?v=uaN_P9K37Dk&t=23s) — Drag in both the 2-Slot and 3-Slot templates. Hide the 2-Slot template for now.

8. [0:27](https://www.youtube.com/watch?v=uaN_P9K37Dk&t=27s) — Right-click the Control node and select Instantiate Child Scene. Add only your ancient scene. It appears in the list because the Godot project containing it is already open.

9. [0:30](https://www.youtube.com/watch?v=uaN_P9K37Dk&t=30s) — Your ancient now appears exactly as it does in its original scene, while remaining contained under a single Control node.

10. [0:32](https://www.youtube.com/watch?v=uaN_P9K37Dk&t=32s) — Add a Marker2D as a child of the ancient scene. We will use markers to determine the required positions.

11. [0:35](https://www.youtube.com/watch?v=uaN_P9K37Dk&t=35s) — This marker will represent the SourceAnchor position.

12. [0:38](https://www.youtube.com/watch?v=uaN_P9K37Dk&t=38s) — Create the marker as a child of the ancient scene first. This ensures its coordinates use the ancient scene’s local coordinate space.

13. [0:43](https://www.youtube.com/watch?v=uaN_P9K37Dk&t=43s) — The SourceAnchor marker starts at the origin. Use the Move tool to place it at the point you want centred. For this template ancient, use the centre of the two-circle shape.

14. [0:50](https://www.youtube.com/watch?v=uaN_P9K37Dk&t=50s) — For precise coordinates, go to Layout → Transform → Position. In this example, use (574, 86), the centre of the two-circle shape.

15. [0:55](https://www.youtube.com/watch?v=uaN_P9K37Dk&t=55s) — Copy the marker coordinates into ChooseTheAncientSourceAnchor. Here: (574f / baseSize.X, 86f / baseSize.Y). SourceAnchor uses percentages, which some Godot scenes can skew, so divide the marker coordinates by baseSize.

16. [1:00](https://www.youtube.com/watch?v=uaN_P9K37Dk&t=60s) — You may need to experiment with SourceAnchor. Choose the point that should remain centred before scaling or repositioning the ancient. You can fine-tune it later.

17. [1:05](https://www.youtube.com/watch?v=uaN_P9K37Dk&t=65s) — Drag SourceAnchor in the node tree so it becomes the parent of the Control node and the ancient scene. Moving SourceAnchor will now move the ancient with it.

18. [1:08](https://www.youtube.com/watch?v=uaN_P9K37Dk&t=68s) — Move both selection-screen templates under the root node. Then place the Control node containing the ancient under SourceAnchor.

19. [1:11](https://www.youtube.com/watch?v=uaN_P9K37Dk&t=71s) — The selection-screen templates and SourceAnchor should now appear above the ancient scene. Next, move SourceAnchor to the centre of a selection slot.

20. [1:13](https://www.youtube.com/watch?v=uaN_P9K37Dk&t=73s) — Use the Move tool to place the marker at a slot centre. For the 3-Slot template, the centre positions are:
Left: (335.65, 556)
Middle: (997.36, 556)
Right: (1620.71, 556)

21. [1:21](https://www.youtube.com/watch?v=uaN_P9K37Dk&t=81s) — Select the Anchor tool, hold Shift, and click the centre point. This creates a temporary anchor so the ancient scales around the chosen point.

22. [1:26](https://www.youtube.com/watch?v=uaN_P9K37Dk&t=86s) — Select the Scale tool and hold Shift to preserve the aspect ratio.

23. [1:30](https://www.youtube.com/watch?v=uaN_P9K37Dk&t=90s) — Hold Shift and drag up or down to scale the ancient in or out.

24. [1:33](https://www.youtube.com/watch?v=uaN_P9K37Dk&t=93s) — For precise scaling, go to Layout → Transform → Scale. In this example, use 2.75, which fits the two-circle shape inside the slot.

25. [1:37](https://www.youtube.com/watch?v=uaN_P9K37Dk&t=97s) — Copy this value into ChooseTheAncientScale. In this example: 2.75f.

26. [1:40](https://www.youtube.com/watch?v=uaN_P9K37Dk&t=100s) — Build the mod and compare the result with the game.

27. [1:43](https://www.youtube.com/watch?v=uaN_P9K37Dk&t=103s) — The in-game scene should now match the template.

28. [1:48](https://www.youtube.com/watch?v=uaN_P9K37Dk&t=108s) — Use the Move tool to drag SourceAnchor between the other slot centres. Check that the ancient fits correctly in every slot.

29. [1:55](https://www.youtube.com/watch?v=uaN_P9K37Dk&t=115s) — After scaling, fine-tune the final position with ChooseTheAncientExtraOffset. This property uses coordinate offsets, not percentages. See the Aligning Neow video for an example.

30. [2:01](https://www.youtube.com/watch?v=uaN_P9K37Dk&t=121s) — Once the ancient looks correct in all three slots, hide the 3-Slot template and show the 2-Slot template. Confirm that it also fits the larger slots.

31. [2:04](https://www.youtube.com/watch?v=uaN_P9K37Dk&t=124s) — Your Custom Ancient should now be aligned correctly on the Choose the Ancient selection screen. Well done.
</details>

## Tutorial where I align Neow for the Choose The Acient Selection Screen
### (A more Complex Scene and uses ExtraOffset)

[![Aligning Neow Simple Tutorial](https://img.youtube.com/vi/ZBrM62Mp7To/maxresdefault.jpg)](https://www.youtube.com/watch?v=ZBrM62Mp7To)
[A more verbose version of the video is here](https://www.youtube.com/watch?v=sR6bj5xkRec)
### Step by Step guide

<details>
<summary>Show step-by-step guide</summary>

1. [0:00](https://www.youtube.com/watch?v=ZBrM62Mp7To&t=0s) — As shown in the Aligning Custom Ancients video, select Neow’s root node and record its Layout → Transform → Size values. Neow uses (1920, 1080), which already matches the example baseSize value, so no code changes are needed.

2. [0:05](https://www.youtube.com/watch?v=ZBrM62Mp7To&t=5s) — Create a new scene.

3. [0:07](https://www.youtube.com/watch?v=ZBrM62Mp7To&t=7s) — Add a Control node as a child of the root. Instantiating Neow’s scene under this Control node preserves its default MegaDot position.

4. [0:10](https://www.youtube.com/watch?v=ZBrM62Mp7To&t=10s) — Set the Control node’s Layout → Transform → Size to match Neow’s root node. In this example: (1920, 1080).

5. [0:15](https://www.youtube.com/watch?v=ZBrM62Mp7To&t=15s) — Download the Choose the Ancient selection-screen templates. I placed them beside my scene, but they do not need to be compiled into your mod.

6. [0:18](https://www.youtube.com/watch?v=ZBrM62Mp7To&t=18s) — Add the 3-Slot selection-screen template. You may also add the 2-Slot template, but only the 3-Slot template is needed here.

7. [0:20](https://www.youtube.com/watch?v=ZBrM62Mp7To&t=20s) — Right-click the Control node, select Instantiate Child Scene, and add your Neow scene. It appears in the list because its Godot project is already open. I am using a Neow Idle scene in which Neow’s head is animated by default.

8. [0:24](https://www.youtube.com/watch?v=ZBrM62Mp7To&t=24s) — Neow now appears exactly as positioned in the original project, while remaining contained under a single Control node.

9. [0:27](https://www.youtube.com/watch?v=ZBrM62Mp7To&t=27s) — Add a Marker2D as a child of the Neow scene. This marker will represent the SourceAnchor position.

10. [0:30](https://www.youtube.com/watch?v=ZBrM62Mp7To&t=30s) — Create the marker under the Neow scene first so its coordinates use Neow’s local coordinate space.

11. [0:33](https://www.youtube.com/watch?v=ZBrM62Mp7To&t=33s) — The SourceAnchor marker starts at the origin. Use the Move tool to place it at the point you want centred. For Neow, place it inside the centre of the mouth.

12. [0:40](https://www.youtube.com/watch?v=ZBrM62Mp7To&t=40s) — For more precision, go to Layout → Transform → Position and set the marker to (757, 542).

13. [0:44](https://www.youtube.com/watch?v=ZBrM62Mp7To&t=44s) — Set ChooseTheAncientSourceAnchor to (757f / baseSize.X, 542f / baseSize.Y). SourceAnchor uses normalized coordinates, so divide the marker position by baseSize.

14. [0:48](https://www.youtube.com/watch?v=ZBrM62Mp7To&t=48s) — You may need to experiment with SourceAnchor. Choose the point that should remain centred before scaling or repositioning Neow. You can fine-tune the result later.

15. [0:51](https://www.youtube.com/watch?v=ZBrM62Mp7To&t=51s) — Reparent the Control node under SourceAnchor so the marker becomes the parent of both the Control node and Neow. Moving SourceAnchor will now move Neow with it.

16. [0:54](https://www.youtube.com/watch?v=ZBrM62Mp7To&t=54s) — Move the selection-screen template under the root node. Keep the Control node containing Neow under SourceAnchor.

17. [0:56](https://www.youtube.com/watch?v=ZBrM62Mp7To&t=56s) — The selection-screen template and SourceAnchor should now appear above Neow. Next, move SourceAnchor to the centre of a selection slot.

18. [0:59](https://www.youtube.com/watch?v=ZBrM62Mp7To&t=59s) — Use the Move tool to place SourceAnchor at a slot centre. For the 3-Slot template, the centre positions are:
Left: (335.65, 556)
Middle: (997.36, 556)
Right: (1620.71, 556)

19. [1:04](https://www.youtube.com/watch?v=ZBrM62Mp7To&t=64s) — Select the Anchor tool, hold Shift, and click the slot centre. This creates a temporary anchor so Neow scales around the chosen point.

20. [1:07](https://www.youtube.com/watch?v=ZBrM62Mp7To&t=67s) — Select the Scale tool and hold Shift to preserve the aspect ratio. Drag up or down to scale Neow in or out.

21. [1:12](https://www.youtube.com/watch?v=ZBrM62Mp7To&t=72s) — For more precision, go to Layout → Transform → Scale. Set the scale to 0.88 because Neow is slightly too large for the slot.

22. [1:16](https://www.youtube.com/watch?v=ZBrM62Mp7To&t=76s) — Set ChooseTheAncientScale to 0.88f.

23. [1:20](https://www.youtube.com/watch?v=ZBrM62Mp7To&t=80s) — Neow still needs horizontal adjustment. Select the Move tool and estimate how far left Neow should move.

24. [1:26](https://www.youtube.com/watch?v=ZBrM62Mp7To&t=86s) — The required offset is about 100 pixels. For more precision, go to Layout → Transform → Position and append -93 in the X field. Godot accepts calculations directly in these fields. A negative X value moves Neow left.

25. [1:32](https://www.youtube.com/watch?v=ZBrM62Mp7To&t=92s) — Set ChooseTheAncientExtraOffset to (-93f, 0f). Do not divide this value by baseSize because ExtraOffset uses pixel coordinates, not percentages.

26. [1:36](https://www.youtube.com/watch?v=ZBrM62Mp7To&t=96s) — Add another Marker2D under Neow so it starts in the same coordinate space. Rename it Center.

27. [1:41](https://www.youtube.com/watch?v=ZBrM62Mp7To&t=101s) — Reparent Center under the root. Then place SourceAnchor, together with the Control node and Neow, under Center. Move the selection-screen template back under the root so Center and the template appear above Neow.

28. [1:52](https://www.youtube.com/watch?v=ZBrM62Mp7To&t=112s) — Use Center to move Neow between slot centres for comparison. A second marker is needed because SourceAnchor has been offset from Neow’s visual centre.

29. [1:55](https://www.youtube.com/watch?v=ZBrM62Mp7To&t=115s) — Use the Move tool to drag Center between the slot centres and check the fit. Neow now fits, so compare the third-slot position with the game.

30. [2:00](https://www.youtube.com/watch?v=ZBrM62Mp7To&t=120s) — The in-game scene now matches the template. Neow is aligned with the Choose the Ancient selection screen. Well done.


</details>

## The SelectionScreen Templates

[TODO] Add the github link to the selection screen templates

## The ChooseTheAncient API

For Baselib, put the following properties into your main class that looks like::
```
public sealed class CustomAncient : CustomAncientModel
```
For Ritsulib, put the following properties into your main class that looks like:
```
[RegisterSharedAncient]
   public sealed class CustomAncient : ModAncientEventTemplate ```
```
```
    //////////////////////////////////////////////////////////////////////////////////////
    ///////////////////// CHOOSE THE ANCIENT PRESENTATION PROPERTIES /////////////////////
    //////////////////////////////////////////////////////////////////////////////////////

    // These are convention properties. They do not create a compile-time dependency
    // on ChooseTheAncient.

    //////////////////////////////////////////////////////////////////////////////////////
    //////////////////////////////////// POSITION ////////////////////////////////////////
    //////////////////////////////////////////////////////////////////////////////////////

    private Vector2 baseSize =
        new (1920f, 1080);

    public Vector2 ChooseTheAncientPortalBaseSize =>
        baseSize;
   
    public Vector2 ChooseTheAncientPortalSourceAnchor =>
        new( 0 / baseSize.X, 0f / baseSize.Y );
    
    public float ChooseTheAncientPortalScale =>
        1f;


    public Vector2 ChooseTheAncientPortalExtraOffset =>
        new( 0f,0f );

    //////////////////////////////////////////////////////////////////////////////////////
    ///////////////////////////////////// COLOURS /////////////////////////////////////////
    //////////////////////////////////////////////////////////////////////////////////////

    // Accent colour used for ChooseTheAncient presentation elements.
    //
    // Use either ChooseTheAncientAccentColor or ChooseTheAncientAccentHex,
    // not both.

    public string ChooseTheAncientAccentHex => "#78DBFA";


    // Dialogue bubble colour.
    //
    // ChooseTheAncient already falls back to the Ancient model's DialogueColor,
    // but this explicitly supplies the same colour through the convention API.
    //
    // Use either ChooseTheAncientDialogueColor or
    // ChooseTheAncientDialogueColorHex, not both.

    public string ChooseTheAncientDialogueColorHex => "#27213F";


    //////////////////////////////////////////////////////////////////////////////////////
    //////////////////////////////////// DIALOGUE /////////////////////////////////////////
    //////////////////////////////////////////////////////////////////////////////////////

    // These values must exactly match keys added to this mod's ancients.json.
    //
    // ChooseTheAncient searches more-specific character and act suffixes first,
    // then falls back to the base prefix.

    private const string ChooseTheAncientLocRoot =
        "CustomAncient.choose_the_ancient";

    public string ChooseTheAncientSecondRoundDialoguePrefix =>
        $"{ChooseTheAncientLocRoot}.second_round.{LocalEntry}.";

    public string ChooseTheAncientFinalRevealBannerKey =>
        $"{ChooseTheAncientLocRoot}.final_reveal.{LocalEntry}";
```