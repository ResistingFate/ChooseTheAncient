using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace ChooseTheAncient.ChooseTheAncientCode.Interop;

internal static class SlayTheStreamerInterop
{
    private const string VoterTypeName = "SlayTheStreamer2.Ti.Voting.Voter";
    private const string VoteTallyLabelTypeName = "SlayTheStreamer2.Ti.Ui.VoteTallyLabel";
    private const string ModSettingsTypeName = "SlayTheStreamer2.Game.Bootstrap.ModSettings";

    private const int DefaultVoteDurationSeconds = 30;
    private const int MinimumOptionCount = 1;
    private const int MaximumOptionCount = 10;

    private static bool _loggedMissingMod;
    private static bool _loggedCompatibleApi;

    internal static bool TryStartVote(
        string label,
        IReadOnlyList<string> options,
        out SlayTheStreamerVote? vote,
        out string unavailableReason)
    {
        vote = null;
        unavailableReason = "";

        if (string.IsNullOrWhiteSpace(label))
        {
            unavailableReason = "the vote label was empty";
            return false;
        }

        if (options.Count is < MinimumOptionCount or > MaximumOptionCount)
        {
            unavailableReason =
                $"the ballot contained {options.Count} options; Slay the Streamer supports {MinimumOptionCount}..{MaximumOptionCount}";
            return false;
        }

        try
        {
            Type? voterType = FindLoadedType(VoterTypeName);
            if (voterType == null)
            {
                unavailableReason = "Slay the Streamer 2 is not loaded";

                if (!_loggedMissingMod)
                {
                    _loggedMissingMod = true;
                    ModLog.Debug(
                        "Slay the Streamer 2 was not detected. " +
                        "ChooseTheAncient will keep using its normal selection controls.");
                }

                return false;
            }

            PropertyInfo? defaultCoordinatorProperty = voterType.GetProperty(
                "Default",
                BindingFlags.Public | BindingFlags.Static);

            object? coordinator = defaultCoordinatorProperty?.GetValue(null);
            if (coordinator == null)
            {
                unavailableReason = "its vote coordinator is not initialized (usually missing/unconfigured chat credentials)";
                return false;
            }

            PropertyInfo? currentSessionProperty = coordinator.GetType().GetProperty(
                "CurrentSession",
                BindingFlags.Public | BindingFlags.Instance);

            if (currentSessionProperty?.GetValue(coordinator) != null)
            {
                unavailableReason = "it already has another vote open";
                return false;
            }

            MethodInfo? startMethod = FindVoterStartMethod(voterType);
            if (startMethod == null)
            {
                unavailableReason = "the expected public Voter.Start API was not found";
                return false;
            }

            SlayTheStreamerDisplaySettings displaySettings = ReadDisplaySettings();

            string[] normalizedOptions = options
                .Select((option, index) =>
                    string.IsNullOrWhiteSpace(option)
                        ? $"Option {index}"
                        : option.Trim())
                .ToArray();

            object? session = InvokeAndUnwrap(
                startMethod,
                target: null,
                arguments:
                [
                    label.Trim(),
                    normalizedOptions,
                    TimeSpan.FromSeconds(displaySettings.VoteDurationSeconds),
                    displaySettings.ShowVoteTag,
                    null,
                    null,
                    null,
                    CancellationToken.None,
                ]);

            if (session == null)
            {
                unavailableReason = "Voter.Start returned null";
                return false;
            }

            SlayTheStreamerVote createdVote;
            try
            {
                createdVote = new SlayTheStreamerVote(session);
            }
            catch
            {
                TryCancelRawSession(session);
                throw;
            }

            TryAttachTally(session, displaySettings.VoteTallyOnLeft);
            vote = createdVote;

            if (!_loggedCompatibleApi)
            {
                _loggedCompatibleApi = true;
                ModLog.Info(
                    "Slay the Streamer 2 interop is active through its runtime public voting API ");
            }

            return true;
        }
        catch (Exception ex)
        {
            unavailableReason = $"runtime interop failed: {ex.GetBaseException().Message}";
            ModLog.Warn(
                "Slay the Streamer 2 interop could not start this ballot. " +
                "ChooseTheAncient will fall back to manual selection. " +
                ex);
            return false;
        }
    }

    private static MethodInfo? FindVoterStartMethod(Type voterType)
    {
        return voterType
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => string.Equals(method.Name, "Start", StringComparison.Ordinal))
            .FirstOrDefault(method =>
            {
                ParameterInfo[] parameters = method.GetParameters();
                return parameters.Length == 8
                       && parameters[0].ParameterType == typeof(string)
                       && parameters[1].ParameterType.IsAssignableFrom(typeof(string[]))
                       && parameters[2].ParameterType == typeof(TimeSpan)
                       && parameters[3].ParameterType == typeof(bool)
                       && parameters[7].ParameterType == typeof(CancellationToken);
            });
    }

    private static SlayTheStreamerDisplaySettings ReadDisplaySettings()
    {
        int voteDurationSeconds = DefaultVoteDurationSeconds;
        bool showVoteTag = true;
        bool voteTallyOnLeft = false;

        Type? settingsType = FindLoadedType(ModSettingsTypeName);
        object? currentSettings = settingsType?
            .GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?
            .GetValue(null);

        if (currentSettings != null)
        {
            voteDurationSeconds = ReadIntProperty(
                currentSettings,
                "VoteDurationSeconds",
                DefaultVoteDurationSeconds);

            showVoteTag = ReadBoolProperty(currentSettings, "ShowVoteTag", fallback: true);
            voteTallyOnLeft = ReadBoolProperty(currentSettings, "VoteTallyOnLeft", fallback: false);
        }

        voteDurationSeconds = Math.Clamp(voteDurationSeconds, 1, 120);

        return new SlayTheStreamerDisplaySettings(
            voteDurationSeconds,
            showVoteTag,
            voteTallyOnLeft);
    }

    private static int ReadIntProperty(object instance, string propertyName, int fallback)
    {
        object? value = instance.GetType()
            .GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)?
            .GetValue(instance);

        return value is int intValue ? intValue : fallback;
    }

    private static bool ReadBoolProperty(object instance, string propertyName, bool fallback)
    {
        object? value = instance.GetType()
            .GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)?
            .GetValue(instance);

        return value is bool boolValue ? boolValue : fallback;
    }

    private static void TryAttachTally(object session, bool placeOnLeft)
    {
        try
        {
            Type? tallyType = FindLoadedType(VoteTallyLabelTypeName);
            MethodInfo? attachMethod = tallyType?
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(method =>
                {
                    if (!string.Equals(method.Name, "AttachTo", StringComparison.Ordinal))
                        return false;

                    ParameterInfo[] parameters = method.GetParameters();
                    return parameters.Length == 4
                           && parameters[0].ParameterType.IsInstanceOfType(session)
                           && parameters[2].ParameterType == typeof(bool);
                });

            if (attachMethod == null)
            {
                ModLog.Debug(
                    "Slay the Streamer vote started, but VoteTallyLabel.AttachTo was not found. " +
                    "Chat voting will still work without its tally overlay.");
                return;
            }

            InvokeAndUnwrap(
                attachMethod,
                target: null,
                arguments: [session, null, placeOnLeft, null]);
        }
        catch (Exception ex)
        {
            ModLog.Warn(
                "Slay the Streamer vote started, but its tally overlay could not be attached. " +
                $"Chat voting will continue. {ex.GetBaseException().Message}");
        }
    }

    private static Type? FindLoadedType(string fullName)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type? type = assembly.GetType(fullName, throwOnError: false, ignoreCase: false);
            if (type != null)
                return type;
        }

        return null;
    }

    private static object? InvokeAndUnwrap(
        MethodInfo method,
        object? target,
        object?[] arguments)
    {
        try
        {
            return method.Invoke(target, arguments);
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private static void TryCancelRawSession(object session)
    {
        try
        {
            session.GetType()
                .GetMethod("Cancel", BindingFlags.Public | BindingFlags.Instance)?
                .Invoke(session, null);
        }
        catch
        {
            // Add any cleanup.
        }
    }

    private readonly record struct SlayTheStreamerDisplaySettings(
        int VoteDurationSeconds,
        bool ShowVoteTag,
        bool VoteTallyOnLeft);
}

internal sealed class SlayTheStreamerVote
{
    private readonly object _session;
    private readonly MethodInfo _tryCloseNowMethod;
    private readonly MethodInfo _cancelMethod;

    internal SlayTheStreamerVote(object session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));

        Type sessionType = session.GetType();

        MethodInfo awaitWinnerMethod =
            sessionType.GetMethod(
                "AwaitWinnerAsync",
                BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                types: [typeof(CancellationToken)],
                modifiers: null)
            ?? throw new MissingMethodException(sessionType.FullName, "AwaitWinnerAsync(CancellationToken)");

        _tryCloseNowMethod =
            sessionType.GetMethod(
                "TryCloseNow",
                BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                types: [typeof(int)],
                modifiers: null)
            ?? throw new MissingMethodException(sessionType.FullName, "TryCloseNow(int)");

        _cancelMethod =
            sessionType.GetMethod(
                "Cancel",
                BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null)
            ?? throw new MissingMethodException(sessionType.FullName, "Cancel()");

        object? winnerTask = InvokeAndUnwrap(
            awaitWinnerMethod,
            _session,
            [CancellationToken.None]);

        WinnerTask = winnerTask as Task<int>
            ?? throw new InvalidOperationException(
                $"{sessionType.FullName}.AwaitWinnerAsync did not return Task<int>.");
    }

    internal Task<int> WinnerTask { get; }

    internal bool TryForceWinner(int optionIndex)
    {
        object? result = InvokeAndUnwrap(
            _tryCloseNowMethod,
            _session,
            [optionIndex]);

        return result is bool didClose && didClose;
    }

    internal void Cancel()
    {
        InvokeAndUnwrap(_cancelMethod, _session, []);
    }

    private static object? InvokeAndUnwrap(
        MethodInfo method,
        object target,
        object?[] arguments)
    {
        try
        {
            return method.Invoke(target, arguments);
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }
}
