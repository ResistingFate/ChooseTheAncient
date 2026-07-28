# ChooseTheAncient Presentation API / Interop Design Notes

This note assumes the current working baseline is **convention properties** on the custom Ancient class.

The goal is to let custom Ancients provide ChooseTheAncient-specific presentation values without requiring the Ancient mod to depend on ChooseTheAncient.

## Current recommended baseline: convention properties

For now, the safest no-dependency path is:

```csharp
public float ChooseTheAncientPortalScale => 3.0f;

public Vector2 ChooseTheAncientPortalSourceAnchor01 => new(0.5000f, 0.2500f);

public Vector2 ChooseTheAncientPortalExtraOffset01 => new(0.0f, 0.50f);

public string ChooseTheAncientAccentHex => "#78DBFA";

public string ChooseTheAncientDialogueColorHex => "#27213F";

public string ChooseTheAncientSecondRoundDialoguePrefix =>
    "my_mod.choose_the_ancient.second_round.MY_ANCIENT.";

public string ChooseTheAncientFinalRevealBannerKey =>
    "my_mod.choose_the_ancient.final_reveal.MY_ANCIENT";
```

This compiles even when ChooseTheAncient is not installed because the custom Ancient is only adding normal C# properties to its own class. ChooseTheAncient reads them by reflection when it is present.

## Which API/interop design is best

| Question | Method | Properties | Soft Register |
|---|---:|---:|---:|
| Compiles without CTA installed? | yes | yes | yes if reflected |
| Easy for author to copy? | medium | easiest | medium |
| Easy to discover in IDE? | low | low | medium |
| Keeps values grouped? | yes | no | yes |
| Typo risk? | medium | medium | high-ish |
| Good for compatibility mods? | no | no | yes |
| Good for authors who own Ancient class? | yes | yes | okay |
| Best no-dependency default? | maybe | yes | no |
| Best explicit override? | no | no | yes |

## Design 1: convention properties

### What the Ancient author writes

```csharp
public float ChooseTheAncientPortalScale => 3.0f;

public Vector2 ChooseTheAncientPortalSourceAnchor01 => new(0.5000f, 0.2500f);

public Vector2 ChooseTheAncientPortalExtraOffset01 => new(0.0f, 0.50f);

public string ChooseTheAncientAccentHex => "#78DBFA";

public string ChooseTheAncientDialogueColorHex => "#27213F";

public string ChooseTheAncientSecondRoundDialoguePrefix =>
    "my_mod.choose_the_ancient.second_round.MY_ANCIENT.";

public string ChooseTheAncientFinalRevealBannerKey =>
    "my_mod.choose_the_ancient.final_reveal.MY_ANCIENT";
```

### What ChooseTheAncient needs to support

ChooseTheAncient should reflect these exact property names:

```text
ChooseTheAncientPortalScale
ChooseTheAncientPortalSourceAnchor01
ChooseTheAncientPortalExtraOffset01
ChooseTheAncientAccentHex
ChooseTheAncientDialogueColorHex
ChooseTheAncientSecondRoundDialoguePrefix
ChooseTheAncientFinalRevealBannerKey
```

Use this as the stable v1 path.

### Why this is the best default

Properties are the easiest for custom Ancient authors to copy. They require no dependency on ChooseTheAncient, and they are simple to validate in logs.

The main downside is that values are spread across several properties, and typos are only caught at runtime.

## Design 2: convention methods

Convention methods are similar to convention properties, but authors write no-argument or context-aware methods instead.

### Simple method version

```csharp
public float ChooseTheAncientGetPortalScale() => 3.0f;

public Vector2 ChooseTheAncientGetPortalSourceAnchor01() => new(0.5000f, 0.2500f);

public Vector2 ChooseTheAncientGetPortalExtraOffset01() => new(0.0f, 0.50f);

public string ChooseTheAncientGetAccentHex() => "#78DBFA";

public string ChooseTheAncientGetDialogueColorHex() => "#27213F";
```

### Context-aware dialogue method version

This is the main reason to keep method support around.

```csharp
public string ChooseTheAncientGetSecondRoundDialoguePrefix(
    string characterId,
    int nextActIndex,
    string? suppressedAncientId)
{
    if (characterId == "MYMOD-MY_CHARACTER")
        return "my_mod.choose_the_ancient.second_round.MY_ANCIENT.MY_CHARACTER.";

    return "my_mod.choose_the_ancient.second_round.MY_ANCIENT.ANY.";
}

public string ChooseTheAncientGetFinalRevealBannerKey(
    string characterId,
    int nextActIndex,
    string? suppressedAncientId)
{
    return "my_mod.choose_the_ancient.final_reveal.MY_ANCIENT";
}
```

### What ChooseTheAncient needs to support

ChooseTheAncient should check both property names and method names:

```text
ChooseTheAncientPortalScale
ChooseTheAncientGetPortalScale

ChooseTheAncientPortalSourceAnchor01
ChooseTheAncientGetPortalSourceAnchor01

ChooseTheAncientPortalExtraOffset01
ChooseTheAncientGetPortalExtraOffset01

ChooseTheAncientAccentHex
ChooseTheAncientGetAccentHex

ChooseTheAncientDialogueColorHex
ChooseTheAncientGetDialogueColorHex
```

For dialogue, support both simple property strings and context methods:

```text
ChooseTheAncientSecondRoundDialoguePrefix
ChooseTheAncientGetSecondRoundDialoguePrefix(string characterId, int nextActIndex, string? suppressedAncientId)

ChooseTheAncientFinalRevealBannerKey
ChooseTheAncientGetFinalRevealBannerKey(string characterId, int nextActIndex, string? suppressedAncientId)
```

### How to test this design

1. Make a test Ancient with only method conventions.
2. Remove or comment out the property conventions.
3. Add logging inside ChooseTheAncient’s resolver:
   ```text
   CTA presentation source for <ancient id>: convention method ChooseTheAncientGetPortalScale = 3.0
   ```
4. Verify the value changes in the selection screen.
5. Verify a typo falls back cleanly instead of crashing.

### Why methods are useful

Methods allow context. This matters for character-specific CTA dialogue, modded characters, suppressed Ancient-specific lines, or future conditions.

### Why methods are not the default

Reflection method signatures are more fragile than reading simple properties. A wrong method name, wrong return type, or wrong parameter order makes the method invisible to ChooseTheAncient.

## Design 3: soft registration

Soft registration lets another mod register presentation data by Ancient ID without owning the Ancient class.

This is useful for compatibility mods.

### What a compatibility mod wants to do

```csharp
ChooseTheAncientApi.RegisterAncientPresentation(
    ancientId: "OTHER_MOD-MY_ANCIENT",
    portalScale: 3.0f,
    portalAnchorX01: 0.5000f,
    portalAnchorY01: 0.2500f,
    portalOffsetX01: 0.0f,
    portalOffsetY01: 0.50f,
    accentHex: "#78DBFA",
    dialogueColorHex: "#27213F",
    secondRoundDialoguePrefix: "compat.choose_the_ancient.second_round.OTHER_MOD_MY_ANCIENT.",
    finalRevealBannerKey: "compat.choose_the_ancient.final_reveal.OTHER_MOD_MY_ANCIENT"
);
```

### Hard reference version

This is simple, but it makes ChooseTheAncient a real dependency:

```csharp
using ChooseTheAncient.ChooseTheAncientCode.Interop;

ChooseTheAncientApi.RegisterAncientPresentation(...);
```

Use this only if the mod author is okay requiring ChooseTheAncient.

### Soft/reflected version

This compiles without ChooseTheAncient installed:

```csharp
using System.Reflection;
using HarmonyLib;

public static class ChooseTheAncientSoftRegister
{
    public static void TryRegister()
    {
        Type? apiType = AccessTools.TypeByName(
            "ChooseTheAncient.ChooseTheAncientCode.Interop.ChooseTheAncientApi");

        if (apiType == null)
            return;

        MethodInfo? register = AccessTools.Method(
            apiType,
            "RegisterAncientPresentation",
            new[]
            {
                typeof(string),
                typeof(float?),
                typeof(float?),
                typeof(float?),
                typeof(float?),
                typeof(float?),
                typeof(string),
                typeof(string),
                typeof(string),
                typeof(string)
            });

        if (register == null)
            return;

        try
        {
            register.Invoke(
                null,
                new object?[]
                {
                    "OTHER_MOD-MY_ANCIENT",
                    3.0f,
                    0.5000f,
                    0.2500f,
                    0.0f,
                    0.50f,
                    "#78DBFA",
                    "#27213F",
                    "compat.choose_the_ancient.second_round.OTHER_MOD_MY_ANCIENT.",
                    "compat.choose_the_ancient.final_reveal.OTHER_MOD_MY_ANCIENT"
                });
        }
        catch (TargetInvocationException ex)
        {
            GD.PrintErr(
                "CTA soft registration failed: " +
                $"{ex.InnerException?.GetType().FullName}: {ex.InnerException?.Message}");
        }
        catch (Exception ex)
        {
            GD.PrintErr(
                "CTA soft registration failed: " +
                $"{ex.GetType().FullName}: {ex.Message}");
        }
    }
}
```

### What ChooseTheAncient needs to support

A simple public method with primitive/nullable parameters:

```csharp
public static void RegisterAncientPresentation(
    string ancientId,
    float? portalScale = null,
    float? portalAnchorX01 = null,
    float? portalAnchorY01 = null,
    float? portalOffsetX01 = null,
    float? portalOffsetY01 = null,
    string? accentHex = null,
    string? dialogueColorHex = null,
    string? secondRoundDialoguePrefix = null,
    string? finalRevealBannerKey = null)
```

Internally, ChooseTheAncient can store that as a `ChooseTheAncientPresentation`.

### How to test this design

1. Make a test Ancient with no CTA convention properties.
2. Add a soft register helper that runs during the mod initializer.
3. Log before and after registration:
   ```text
   Trying CTA soft registration for <ancient id>
   CTA soft registration succeeded for <ancient id>
   ```
4. In ChooseTheAncient, log:
   ```text
   CTA presentation source for <ancient id>: explicit registration
   ```
5. If it fails, unwrap `TargetInvocationException.InnerException`.

### Why soft registration is useful

It is the only design that works well for compatibility mods. A third-party mod can fix another Ancient’s CTA presentation without editing that Ancient’s source.

### Why soft registration should not be the default

It has the highest typo/signature risk. Optional parameters do not make reflection calls easier; reflected calls must pass the exact parameter list or find the exact overload.

## Recommended resolver priority

The intended priority should be:

```text
1. Explicit registration
2. Convention methods/properties on the Ancient
3. Native Ancient data
4. ChooseTheAncient built-in profiles
5. Generic fallback
```

A practical implementation can read convention first, then overlay explicit registration:

```csharp
ChooseTheAncientPresentation resolved = ReadConventionPresentation(ancient);

if (ChooseTheAncientApi.TryGetPresentation(ancient.Id.Entry, out var registered))
    resolved = Merge(resolved, registered);
```

That gives explicit registration final override power.

## Recommended v1 public docs

For v1, document properties as the stable path:

```csharp
public float ChooseTheAncientPortalScale => 3.0f;
public Vector2 ChooseTheAncientPortalSourceAnchor01 => new(0.5000f, 0.2500f);
public Vector2 ChooseTheAncientPortalExtraOffset01 => new(0.0f, 0.50f);
public string ChooseTheAncientAccentHex => "#78DBFA";
public string ChooseTheAncientDialogueColorHex => "#27213F";
public string ChooseTheAncientSecondRoundDialoguePrefix =>
    "my_mod.choose_the_ancient.second_round.MY_ANCIENT.";
public string ChooseTheAncientFinalRevealBannerKey =>
    "my_mod.choose_the_ancient.final_reveal.MY_ANCIENT";
```

Then add this note:

> ChooseTheAncient may support method conventions and soft registration in later versions. Properties are the most stable no-dependency integration path today.

## Suggested future tests

Make three separate Ancients:

```text
Focus 1: property conventions only
Focus 2: method conventions only
Focus 3: soft registration only
```

Then run a CTA selection screen with all three visible and log:

```text
Ancient ID
resolved scale
resolved source anchor
resolved extra offset
presentation source
```

That will make it obvious whether each path is being consumed.
