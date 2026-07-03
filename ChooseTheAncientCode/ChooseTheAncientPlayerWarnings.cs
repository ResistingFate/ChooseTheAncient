using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Modding;

namespace ChooseTheAncient.ChooseTheAncientCode;

internal static class ChooseTheAncientPlayerWarnings
{
    private const string WarningLocTable = "settings_ui";

    private const string NeowSyncWarningKey =
        "CHOOSETHEANCIENT.mod_warning.neow_multiplayer_identity_sync_unavailable";

    private static bool _registered;
    private static bool _neowMultiplayerIdentitySyncUnavailable;
    private static Mod? _chooseTheAncientMod;

    public static void Register()
    {
        if (_registered)
            return;

        _registered = true;
        ModManager.OnModDetected += OnModDetected;
    }

    public static void ReportNeowMultiplayerIdentitySyncUnavailable()
    {
        _neowMultiplayerIdentitySyncUnavailable = true;

        if (_chooseTheAncientMod != null)
        {
            FlushToModErrors(_chooseTheAncientMod);
        }
    }

    private static void OnModDetected(Mod mod)
    {
        if (!string.Equals(mod.manifest?.id, MainFile.ModId, StringComparison.Ordinal))
            return;

        _chooseTheAncientMod = mod;
        FlushToModErrors(mod);

        // We only need to handle CTA's own load event once. Later runtime reports can use the cached Mod instance.
        ModManager.OnModDetected -= OnModDetected;
    }

    private static void FlushToModErrors(Mod mod)
    {
        if (!_neowMultiplayerIdentitySyncUnavailable)
            return;

        mod.errors ??= new List<LocString>();

        bool alreadyAdded = mod.errors.Any(error =>
            error.LocTable == WarningLocTable &&
            error.LocEntryKey == NeowSyncWarningKey);

        if (alreadyAdded)
            return;

        mod.errors.Add(new LocString(WarningLocTable, NeowSyncWarningKey));
    }
}
