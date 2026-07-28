# Video Tutorials

The default settings for the `Choose The Ancient` mod are pretty good. Just in case, here's how to adjust them for your Custom Ancient.
```
Note: All steps are important. If your ancient doesn't align correctly you might have
accidentally made a mistake. I find the fastest way to restart, is just to start from the beginning.
```

## Getting Your Ancient to the right Position (click the image to load the video.)

[![Aligning Focus Ancient 2 Simple Tutorial](https://img.youtube.com/vi/YgH6Ayu-Ttw/maxresdefault.jpg)](https://www.youtube.com/watch?v=YgH6Ayu-Ttw)
[The Short guide](https://www.youtube.com/watch?v=YgH6Ayu-Ttw)

[A more verbose version of the video is here](https://www.youtube.com/watch?v=rH6-15n0qmo)
### Step by Step guide

<details>
<summary>Show step-by-step guide</summary>


1. [0:00](https://www.youtube.com/watch?v=YgH6Ayu-Ttw&t=0s) — Open your scene, select the root node, then go to Layout → Transform → Size and record the values. In this example: (1152, 648).

2. [0:03](https://www.youtube.com/watch?v=YgH6Ayu-Ttw&t=3s) — Use those values for the baseSize variable in the ChooseTheAncientBaseSize property: (1152f, 648f). Add this to your Custom Ancient class. The same applies to BaseLib or RitsLib ancients.

3. [0:06](https://www.youtube.com/watch?v=YgH6Ayu-Ttw&t=6s) — Create a new scene in MegaDot. This is the most reliable way to choose the centre point for your ancient on the selection screen, regardless of scene type.

4. [0:09](https://www.youtube.com/watch?v=YgH6Ayu-Ttw&t=9s) — Add a new Control node as a child of the root. Instantiating the ancient scene under this Control node preserves its default MegaDot position.

5. [0:12](https://www.youtube.com/watch?v=YgH6Ayu-Ttw&t=12s) — Set the Control node’s Layout → Transform → Size to match your scene’s root node. In this example: (1152, 648).

6. [0:18](https://www.youtube.com/watch?v=YgH6Ayu-Ttw&t=18s) — Download the Choose the Ancient selection-screen templates. I placed them beside my scene, but they do not need to be compiled into your mod.

7. [0:23](https://www.youtube.com/watch?v=YgH6Ayu-Ttw&t=23s) — Drag in both the 2-Slot and 3-Slot templates. Hide the 2-Slot template for now.

8. [0:27](https://www.youtube.com/watch?v=YgH6Ayu-Ttw&t=27s) — Right-click the Control node and select Instantiate Child Scene. Add only your ancient scene. It appears in the list because the Godot project containing it is already open.

9. [0:30](https://www.youtube.com/watch?v=YgH6Ayu-Ttw&t=30s) — Your ancient now appears exactly as it does in its original scene, while remaining contained under a single Control node.

10. [0:32](https://www.youtube.com/watch?v=YgH6Ayu-Ttw&t=32s) — Add a Marker2D as a child of the ancient scene. We will use markers to determine the required positions.

11. [0:35](https://www.youtube.com/watch?v=YgH6Ayu-Ttw&t=35s) — This marker will represent the SourceAnchor position.

12. [0:38](https://www.youtube.com/watch?v=YgH6Ayu-Ttw&t=38s) — Create the marker as a child of the ancient scene first. This ensures its coordinates use the ancient scene’s local coordinate space.

13. [0:43](https://www.youtube.com/watch?v=YgH6Ayu-Ttw&t=43s) — The SourceAnchor marker starts at the origin. Use the Move tool to place it at the point you want centred. For this template ancient, use the centre of the two-circle shape.

14. [0:50](https://www.youtube.com/watch?v=YgH6Ayu-Ttw&t=50s) — For precise coordinates, go to Layout → Transform → Position. In this example, use (574, 86), the centre of the two-circle shape.

15. [0:55](https://www.youtube.com/watch?v=YgH6Ayu-Ttw&t=55s) — Copy the marker coordinates into ChooseTheAncientSourceAnchor. Here: (574f / baseSize.X, 86f / baseSize.Y). SourceAnchor uses percentages, which some Godot scenes can skew, so divide the marker coordinates by baseSize.

16. [1:00](https://www.youtube.com/watch?v=YgH6Ayu-Ttw&t=60s) — You may need to experiment with SourceAnchor. Choose the point that should remain centred before scaling or repositioning the ancient. You can fine-tune it later.

17. [1:05](https://www.youtube.com/watch?v=YgH6Ayu-Ttw&t=65s) — Drag SourceAnchor in the node tree so it becomes the parent of the Control node and the ancient scene. Moving SourceAnchor will now move the ancient with it.

18. [1:08](https://www.youtube.com/watch?v=YgH6Ayu-Ttw&t=68s) — Move both selection-screen templates under the root node. Then place the Control node containing the ancient under SourceAnchor.

19. [1:11](https://www.youtube.com/watch?v=YgH6Ayu-Ttw&t=71s) — The selection-screen templates and SourceAnchor should now appear above the ancient scene. Next, move SourceAnchor to the centre of a selection slot.

20. [1:13](https://www.youtube.com/watch?v=YgH6Ayu-Ttw&t=73s) — Use the Move tool to place the marker at a slot centre. For the 3-Slot template, the centre positions are:
Left: (335.65, 556)
Middle: (997.36, 556)
Right: (1620.71, 556)

21. [1:21](https://www.youtube.com/watch?v=YgH6Ayu-Ttw&t=81s) — Select the Anchor tool, hold Shift, and click the centre point. This creates a temporary anchor so the ancient scales around the chosen point.

22. [1:26](https://www.youtube.com/watch?v=YgH6Ayu-Ttw&t=86s) — Select the Scale tool and hold Shift to preserve the aspect ratio.

23. [1:30](https://www.youtube.com/watch?v=YgH6Ayu-Ttw&t=90s) — Hold Shift and drag up or down to scale the ancient in or out.

24. [1:33](https://www.youtube.com/watch?v=YgH6Ayu-Ttw&t=93s) — For precise scaling, go to Layout → Transform → Scale. In this example, use 2.75, which fits the two-circle shape inside the slot.

25. [1:37](https://www.youtube.com/watch?v=YgH6Ayu-Ttw&t=97s) — Copy this value into ChooseTheAncientScale. In this example: 2.75f.

26. [1:40](https://www.youtube.com/watch?v=YgH6Ayu-Ttw&t=100s) — Build the mod and compare the result with the game.

27. [1:43](https://www.youtube.com/watch?v=YgH6Ayu-Ttw&t=103s) — The in-game scene should now match the template.

28. [1:48](https://www.youtube.com/watch?v=YgH6Ayu-Ttw&t=108s) — Use the Move tool to drag SourceAnchor between the other slot centres. Check that the ancient fits correctly in every slot.

29. [1:55](https://www.youtube.com/watch?v=YgH6Ayu-Ttw&t=115s) — After scaling, fine-tune the final position with ChooseTheAncientExtraOffset. This property uses coordinate offsets, not percentages. See the Aligning Neow video for an example.

30. [2:01](https://www.youtube.com/watch?v=YgH6Ayu-Ttw&t=121s) — Once the ancient looks correct in all three slots, hide the 3-Slot template and show the 2-Slot template. Confirm that it also fits the larger slots.

31. [2:04](https://www.youtube.com/watch?v=YgH6Ayu-Ttw&t=124s) — Your Custom Ancient should now be aligned correctly on the Choose the Ancient selection screen. Well done.
</details>

## Picking the Perfect spot for Neow
### (A more Complex Scene and uses ExtraOffset)

[![Aligning Neow Simple Tutorial](https://img.youtube.com/vi/MmiknDN1rWM/maxresdefault.jpg)](https://www.youtube.com/watch?v=MmiknDN1rWM)
[The Short guide](https://www.youtube.com/watch?v=MmiknDN1rWM)

[A more verbose version of the video is here](https://www.youtube.com/watch?v=soAe4h3K-D8)
### Step by Step guide

<details>
<summary>Show step-by-step guide</summary>

1. [0:00](https://www.youtube.com/watch?v=MmiknDN1rWM&t=0s) — As shown in the Aligning Custom Ancients video, select Neow’s root node and record its Layout → Transform → Size values. Neow uses (1920, 1080), which already matches the example baseSize value, so no code changes are needed.

2. [0:05](https://www.youtube.com/watch?v=MmiknDN1rWM&t=5s) — Create a new scene.

3. [0:07](https://www.youtube.com/watch?v=MmiknDN1rWM&t=7s) — Add a Control node as a child of the root. Instantiating Neow’s scene under this Control node preserves its default MegaDot position.

4. [0:10](https://www.youtube.com/watch?v=MmiknDN1rWM&t=10s) — Set the Control node’s Layout → Transform → Size to match Neow’s root node. In this example: (1920, 1080).

5. [0:15](https://www.youtube.com/watch?v=MmiknDN1rWM&t=15s) — Download the Choose the Ancient selection-screen templates. I placed them beside my scene, but they do not need to be compiled into your mod.

6. [0:18](https://www.youtube.com/watch?v=MmiknDN1rWM&t=18s) — Add the 3-Slot selection-screen template. You may also add the 2-Slot template, but only the 3-Slot template is needed here.

7. [0:20](https://www.youtube.com/watch?v=MmiknDN1rWM&t=20s) — Right-click the Control node, select Instantiate Child Scene, and add your Neow scene. It appears in the list because its Godot project is already open. I am using a Neow Idle scene in which Neow’s head is animated by default.

8. [0:24](https://www.youtube.com/watch?v=MmiknDN1rWM&t=24s) — Neow now appears exactly as positioned in the original project, while remaining contained under a single Control node.

9. [0:27](https://www.youtube.com/watch?v=MmiknDN1rWM&t=27s) — Add a Marker2D as a child of the Neow scene. This marker will represent the SourceAnchor position.

10. [0:30](https://www.youtube.com/watch?v=MmiknDN1rWM&t=30s) — Create the marker under the Neow scene first so its coordinates use Neow’s local coordinate space.

11. [0:33](https://www.youtube.com/watch?v=MmiknDN1rWM&t=33s) — The SourceAnchor marker starts at the origin. Use the Move tool to place it at the point you want centred. For Neow, place it inside the centre of the mouth.

12. [0:40](https://www.youtube.com/watch?v=MmiknDN1rWM&t=40s) — For more precision, go to Layout → Transform → Position and set the marker to (757, 542).

13. [0:44](https://www.youtube.com/watch?v=MmiknDN1rWM&t=44s) — Set ChooseTheAncientSourceAnchor to (757f / baseSize.X, 542f / baseSize.Y). SourceAnchor uses normalized coordinates, so divide the marker position by baseSize.

14. [0:48](https://www.youtube.com/watch?v=MmiknDN1rWM&t=48s) — You may need to experiment with SourceAnchor. Choose the point that should remain centred before scaling or repositioning Neow. You can fine-tune the result later.

15. [0:51](https://www.youtube.com/watch?v=MmiknDN1rWM&t=51s) — Reparent the Control node under SourceAnchor so the marker becomes the parent of both the Control node and Neow. Moving SourceAnchor will now move Neow with it.

16. [0:54](https://www.youtube.com/watch?v=MmiknDN1rWM&t=54s) — Move the selection-screen template under the root node. Keep the Control node containing Neow under SourceAnchor.

17. [0:56](https://www.youtube.com/watch?v=MmiknDN1rWM&t=56s) — The selection-screen template and SourceAnchor should now appear above Neow. Next, move SourceAnchor to the centre of a selection slot.

18. [0:59](https://www.youtube.com/watch?v=MmiknDN1rWM&t=59s) — Use the Move tool to place SourceAnchor at a slot centre. For the 3-Slot template, the centre positions are:
Left: (335.65, 556)
Middle: (997.36, 556)
Right: (1620.71, 556)

19. [1:04](https://www.youtube.com/watch?v=MmiknDN1rWM&t=64s) — Select the Anchor tool, hold Shift, and click the slot centre. This creates a temporary anchor so Neow scales around the chosen point.

20. [1:07](https://www.youtube.com/watch?v=MmiknDN1rWM&t=67s) — Select the Scale tool and hold Shift to preserve the aspect ratio. Drag up or down to scale Neow in or out.

21. [1:12](https://www.youtube.com/watch?v=MmiknDN1rWM&t=72s) — For more precision, go to Layout → Transform → Scale. Set the scale to 0.88 because Neow is slightly too large for the slot.

22. [1:16](https://www.youtube.com/watch?v=MmiknDN1rWM&t=76s) — Set ChooseTheAncientScale to 0.88f.

23. [1:20](https://www.youtube.com/watch?v=MmiknDN1rWM&t=80s) — Neow still needs horizontal adjustment. Select the Move tool and estimate how far left Neow should move.

24. [1:26](https://www.youtube.com/watch?v=MmiknDN1rWM&t=86s) — The required offset is about 100 pixels. For more precision, go to Layout → Transform → Position and append -93 in the X field. Godot accepts calculations directly in these fields. A negative X value moves Neow left.

25. [1:32](https://www.youtube.com/watch?v=MmiknDN1rWM&t=92s) — Set ChooseTheAncientExtraOffset to (-93f, 0f). Do not divide this value by baseSize because ExtraOffset uses pixel coordinates, not percentages.

26. [1:36](https://www.youtube.com/watch?v=MmiknDN1rWM&t=96s) — Add another Marker2D under Neow so it starts in the same coordinate space. Rename it Center.

27. [1:41](https://www.youtube.com/watch?v=MmiknDN1rWM&t=101s) — Reparent Center under the root. Then place SourceAnchor, together with the Control node and Neow, under Center. Move the selection-screen template back under the root so Center and the template appear above Neow.

28. [1:52](https://www.youtube.com/watch?v=MmiknDN1rWM&t=112s) — Use Center to move Neow between slot centres for comparison. A second marker is needed because SourceAnchor has been offset from Neow’s visual centre.

29. [1:55](https://www.youtube.com/watch?v=MmiknDN1rWM&t=115s) — Use the Move tool to drag Center between the slot centres and check the fit. Neow now fits, so compare the third-slot position with the game.

30. [2:00](https://www.youtube.com/watch?v=MmiknDN1rWM&t=120s) — The in-game scene now matches the template. Neow is aligned with the Choose the Ancient selection screen. Well done.


</details>

## The SelectionScreen Templates

These files are stored on the github and are useful for aligning your Custom Ancietns.
* [Download Selection Screen Templates](https://raw.githubusercontent.com/ResistingFate/ChooseTheAncient/master/workshop/cta_slot_templates_1080p_slot_anchors.zip)

## The ChooseTheAncient API

For BaseLib, put the following properties into your main class, which looks like:

```csharp
public sealed class CustomAncient : CustomAncientModel
```

For RitsuLib, put the following properties into your main class, which looks like:

```csharp
[RegisterSharedAncient]
public sealed class CustomAncient : ModAncientEventTemplate
```

<details>
<summary><strong>Click to add this to your custom ancient code</strong></summary>

```csharp
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

    // Accent color used for ChooseTheAncient presentation elements.
    // Use either ChooseTheAncientAccentColor or ChooseTheAncientAccentHex, not both.

    public string ChooseTheAncientAccentHex => "#78DBFA";
    // public Color ChooseTheAncientDialogueColor => new Color(0.47f, 0.86f, 0.98f, 0.9f);;


    // Dialogue bubble color.
    // Use either ChooseTheAncientDialogueColor or ChooseTheAncientDialogueColorHex, not both.

    public string ChooseTheAncientDialogueColorHex => "#27213F";
    // public Color ChooseTheAncientDialogueColor => new Color(0.15f, 0.13f, 0.25f, 0.9f);
```

</details>

## Adding The Final Reveal Banner

The final Reveal banner is the big white test introducing your ancient sabotaging the most voted ancient.
Replace:
* `MY_ANCIENT` with your ancient’s `Id.Entry
```json
"choose_the_ancient.round_intro.final_reveal.MY_ANCIENT": "{SpeakerAncient} sabotages your vote"
```
Add a line like above to the `ancients.json` for each language you want to support.
```
└── Localization
    └── eng
        └── ancients.json
```
## Adding Custom Ancient Dialogue

Add these entries to `ancients.json` for each language you want to support.
```
└── Localization
    └── eng
        └── ancients.json
```
All keys will be of the form:
```json
choose_the_ancient.second_round.dialogue.<reaction/suppressed>.MY_ANCIENT.[other_ancient.OTHER_ANCIENT].[character.IRONCLAD].[act.HIVE].<N>
```
Where `[]` are optional and `<>` are required parts of the key.
* `reaction` means this dialogue is said by the ancient revealing their options for a second try.
* `suppressed` means this dialogue is said by the most voted ancient. (Only shows during Fair Fight mode)
* The `[]` keys mean this key is only said when the condition is met.
* `N` is just an identifier for the key of the same type as all keys needs to be different. If there is more than 1 key for the same type, the key is randomly picked for all keys of the same type using the different indices. Number them `0 to 1` numerically.
  
Replace:
* `MY_ANCIENT` with your ancient’s `Id.Entry`
* `OTHER_ANCIENT` with another ancient’s name
* `IRONCLAD` with another character
* `HIVE` with another act

```json
{ "choose_the_ancient.second_round.dialogue.reaction.MY_ANCIENT.0": "{SpeakerAncient} challenges {OtherAncient}.",
  "choose_the_ancient.second_round.dialogue.reaction.MY_ANCIENT.1": "You should reconsider my offer.",
  "choose_the_ancient.second_round.dialogue.reaction.MY_ANCIENT.other_ancient.OTHER_ANCIENT.0": "Why choose {OtherAncient} over me?",
  "choose_the_ancient.second_round.dialogue.reaction.MY_ANCIENT.character.IRONCLAD.0": "This offer suits you, {Character}.",
  "choose_the_ancient.second_round.dialogue.reaction.MY_ANCIENT.act.HIVE.0": "You will need my help in {ActTitle}.",
  "choose_the_ancient.second_round.dialogue.reaction.MY_ANCIENT.other_ancient.OTHER_ANCIENT.character.IRONCLAD.0": "{Character}, do not choose {OtherAncient}.",
  "choose_the_ancient.second_round.dialogue.reaction.MY_ANCIENT.other_ancient.OTHER_ANCIENT.act.HIVE.0": "{OtherAncient} cannot prepare you for {ActTitle}.",
  "choose_the_ancient.second_round.dialogue.reaction.MY_ANCIENT.character.IRONCLAD.act.HIVE.0": "{Character}, my offer will help you survive {ActTitle}.", 
  "choose_the_ancient.second_round.dialogue.reaction.MY_ANCIENT.other_ancient.OTHER_ANCIENT.character.IRONCLAD.act.HIVE.0": "{Character}, choose me over {OtherAncient} before entering {ActTitle}.", 
  "choose_the_ancient.second_round.dialogue.suppressed.MY_ANCIENT.0": "{SpeakerAncient} answers {OtherAncient}.",
  "choose_the_ancient.second_round.dialogue.suppressed.MY_ANCIENT.other_ancient.OTHER_ANCIENT.character.IRONCLAD.act.HIVE.0": "{OtherAncient} won't help you in {ActTitle}."
}
```

The Available localized text variables you can use in dialogue:

```text
{SpeakerAncient}  The ancient currently speaking no matter if it the chosen ancient or not
{OtherAncient}    The other finalist
{Character}       The current character
{ActTitle}        The current act title
```


## Advanced: Dialouge branching based on in game modded values

A dialogue branch lets another mod select higher-priority dialogue with new keys according to states of that mod.

This is best explain with an example. Here is my rudimentary take on Ancient Affections. We will track 4  states:
```Text
affection.friendly
affection.hostile
reputation.trusted
quest.completed
```


In `main.cs` register the branch once from the mod.

```csharp
using ChooseTheAncient.ChooseTheAncientCode.Interop;

namespace AffectionateAncients;

public static class MainFile
{
public static void Initialize()
{
// Register one branch named "affection".
//
// CTA calls this resolver whenever it needs to select dialogue.

ChooseTheAncientApi.RegisterDialogueBranch(
"affection",
ResolveAffectionBranch);
}

    private static string? ResolveAffectionBranch(
        ChooseTheAncientDialogueContext context)
    {

        if (!AffectionTracker.Tracks(context.SpeakerAncientEntry)) // 
            return null;

        return AffectionTracker.GetDialogueTier(
            context.SpeakerAncientEntry);
    }
}
```

`AffectionTracker` is just some theoretical code for the example mod.

```csharp
namespace AffectionateAncients;

internal static class AffectionTracker
{
private static readonly Dictionary<string, int>
AffectionByAncient = new();

    internal static bool Tracks(string ancientEntry)
    {
        return AffectionByAncient.ContainsKey(ancientEntry);
    }

    internal static string GetDialogueTier(string ancientEntry)
    {
        int affection = AffectionByAncient[ancientEntry];

        if (affection >= 75)
            return "devoted";

        if (affection >= 25)
            return "friendly";

        return "hostile";
    }
}
```

 The important part is that your mod needs to return the state of your branch, in this case `affection`'s state which becomes the second part of the branch path.
 
When the resolver returns `friendly`, `Choose The Ancient` searches the `affection.friendly` pools in `ancient.json` before ordinary dialogue:

```json
{
"choose_the_ancient.second_round.dialogue.reaction.NEOW.affection.friendly.0":
"It is pleasant to see you again, {Character}.",

"choose_the_ancient.second_round.dialogue.reaction.NEOW.affection.friendly.other_ancient.DARV.0":
"Surely you would not choose {OtherAncient} over me.",

"choose_the_ancient.second_round.dialogue.suppressed.NEOW.affection.friendly.0":
"I thought our relationship meant more than this."
}
```

As you can see branch dialogue supports the same optional qualifiers as ordinary dialogue. Make sure to add the dialogue to an `ancients.json` too.