using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;

namespace ChooseTheAncient.ChooseTheAncientCode.Compatibility;

/// <summary>
/// Keeps CTA from binding directly to seed-related game API signatures that differ between
/// the 0.107.1 stable branch and the 0.109.0 beta branch.
/// </summary>
internal static class SeedCompatibility
{
    private const BindingFlags InstanceFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private const BindingFlags StaticFlags =
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly PropertyInfo RunSeedProperty =
        typeof(RunRngSet).GetProperty("Seed", InstanceFlags)
        ?? throw new MissingMemberException(typeof(RunRngSet).FullName, "Seed");

    private static readonly MethodInfo DeterministicHashMethod =
        typeof(StringHelper).GetMethod(
            "GetDeterministicHashCode",
            StaticFlags,
            binder: null,
            types: [typeof(string)],
            modifiers: null)
        ?? throw new MissingMethodException(
            typeof(StringHelper).FullName,
            "GetDeterministicHashCode(string)");

    private static readonly ConstructorInfo? RngSeedConstructor =
        FindRngConstructor(named: false);

    private static readonly ConstructorInfo? NamedRngSeedConstructor =
        FindRngConstructor(named: true);

    private static readonly MethodInfo? SetMapMethod =
        FindSetMapMethod();

    public static bool Uses64BitSeeds =>
        GetIntegralBitWidth(RunSeedProperty.PropertyType) > 32;

    public static ulong GetRunSeed(RunState runState)
    {
        object rawSeed = RunSeedProperty.GetValue(runState.Rng)
            ?? throw new InvalidOperationException("RunRngSet.Seed returned null.");

        return ConvertIntegralToUInt64(rawSeed);
    }

    public static ulong GetDeterministicHash64(string value)
    {
        return ConvertIntegralToUInt64(InvokeDeterministicHash(value));
    }

    public static uint GetDeterministicHash32(string value)
    {
        return unchecked((uint)ConvertIntegralToUInt64(InvokeDeterministicHash(value)));
    }

    public static Rng CreateRng(ulong seed)
    {
        ConstructorInfo constructor = RngSeedConstructor
            ?? throw new MissingMethodException(
                typeof(Rng).FullName,
                $"compatible seed constructor. Available constructors: {DescribeRngConstructors()}");

        ParameterInfo[] parameters = constructor.GetParameters();
        object seedArgument = ConvertSeedArgument(seed, parameters[0].ParameterType);

        // STS2 0.109.0: Rng(ulong seed)
        if (parameters.Length == 1)
            return (Rng)constructor.Invoke([seedArgument]);

        // STS2 0.107.1: Rng(uint seed, int counter)
        // A newly-created RNG starts at counter zero.
        if (parameters.Length == 2 && parameters[1].ParameterType == typeof(int))
            return (Rng)constructor.Invoke([seedArgument, 0]);

        throw new MissingMethodException(
            typeof(Rng).FullName,
            $"unsupported seed constructor {DescribeConstructor(constructor)}");
    }

    public static Rng CreateNamedRng(ulong seed, string name)
    {
        ConstructorInfo constructor = NamedRngSeedConstructor
            ?? throw new MissingMethodException(
                typeof(Rng).FullName,
                $"compatible seed-and-name constructor. Available constructors: {DescribeRngConstructors()}");

        ParameterInfo seedParameter = constructor.GetParameters()[0];
        object seedArgument = ConvertSeedArgument(seed, seedParameter.ParameterType);

        return (Rng)constructor.Invoke([seedArgument, name]);
    }

    public static ulong AddSignedOffset(ulong seed, int offset)
    {
        if (offset == 0)
            return seed;

        return Uses64BitSeeds
            ? unchecked(seed + (ulong)(long)offset)
            : unchecked((uint)seed + (uint)offset);
    }

    public static void SetMap(
        NMapScreen mapScreen,
        ActMap map,
        ulong seed,
        bool clearDrawings)
    {
        MethodInfo method = SetMapMethod
            ?? throw new MissingMethodException(
                typeof(NMapScreen).FullName,
                "SetMap(ActMap, integral seed, bool)");

        ParameterInfo seedParameter = method.GetParameters()[1];
        object seedArgument = ConvertSeedArgument(seed, seedParameter.ParameterType);

        method.Invoke(mapScreen, [map, seedArgument, clearDrawings]);
    }

    private static ConstructorInfo? FindRngConstructor(bool named)
    {
        return typeof(Rng)
            .GetConstructors(InstanceFlags)
            .Where(constructor =>
            {
                ParameterInfo[] parameters = constructor.GetParameters();

                if (parameters.Length == 0
                    || !IsSupportedIntegralType(parameters[0].ParameterType))
                {
                    return false;
                }

                if (named)
                {
                    return parameters.Length == 2
                        && parameters[1].ParameterType == typeof(string);
                }

                // STS2 0.109.0 exposes Rng(ulong seed).
                if (parameters.Length == 1)
                    return true;

                // STS2 0.107.1 exposes Rng(uint seed, int counter).
                return parameters.Length == 2
                    && parameters[1].ParameterType == typeof(int);
            })
            .OrderByDescending(constructor =>
                constructor.GetParameters()[0].ParameterType == RunSeedProperty.PropertyType)
            .ThenBy(constructor => constructor.GetParameters().Length)
            .ThenByDescending(constructor =>
                GetIntegralBitWidth(constructor.GetParameters()[0].ParameterType))
            .FirstOrDefault();
    }

    private static MethodInfo? FindSetMapMethod()
    {
        return typeof(NMapScreen)
            .GetMethods(InstanceFlags)
            .Where(method => method.Name == "SetMap")
            .Where(method =>
            {
                ParameterInfo[] parameters = method.GetParameters();

                return parameters.Length == 3
                    && parameters[0].ParameterType.IsAssignableFrom(typeof(ActMap))
                    && IsSupportedIntegralType(parameters[1].ParameterType)
                    && parameters[2].ParameterType == typeof(bool);
            })
            .OrderByDescending(method =>
                method.GetParameters()[1].ParameterType == RunSeedProperty.PropertyType)
            .ThenByDescending(method =>
                GetIntegralBitWidth(method.GetParameters()[1].ParameterType))
            .FirstOrDefault();
    }

    private static object ConvertSeedArgument(ulong seed, Type targetType)
    {
        if (targetType == typeof(ulong))
            return seed;

        if (targetType == typeof(long))
            return unchecked((long)seed);

        if (targetType == typeof(uint))
            return unchecked((uint)seed);

        if (targetType == typeof(int))
            return unchecked((int)seed);

        if (targetType == typeof(ushort))
            return unchecked((ushort)seed);

        if (targetType == typeof(short))
            return unchecked((short)seed);

        if (targetType == typeof(byte))
            return unchecked((byte)seed);

        if (targetType == typeof(sbyte))
            return unchecked((sbyte)seed);

        throw new InvalidOperationException(
            $"Unsupported seed parameter type: {targetType.FullName}");
    }

    private static ulong ConvertIntegralToUInt64(object value)
    {
        return value switch
        {
            ulong typed => typed,
            long typed => unchecked((ulong)typed),
            uint typed => typed,
            int typed => unchecked((uint)typed),
            ushort typed => typed,
            short typed => unchecked((ushort)typed),
            byte typed => typed,
            sbyte typed => unchecked((byte)typed),
            _ => Convert.ToUInt64(value, CultureInfo.InvariantCulture)
        };
    }

    private static bool IsSupportedIntegralType(Type type)
    {
        return type == typeof(ulong)
            || type == typeof(long)
            || type == typeof(uint)
            || type == typeof(int)
            || type == typeof(ushort)
            || type == typeof(short)
            || type == typeof(byte)
            || type == typeof(sbyte);
    }

    private static int GetIntegralBitWidth(Type type)
    {
        if (type == typeof(ulong) || type == typeof(long))
            return 64;

        if (type == typeof(uint) || type == typeof(int))
            return 32;

        if (type == typeof(ushort) || type == typeof(short))
            return 16;

        if (type == typeof(byte) || type == typeof(sbyte))
            return 8;

        return 0;
    }

    private static object InvokeDeterministicHash(string value)
    {
        return DeterministicHashMethod.Invoke(null, [value])
            ?? throw new InvalidOperationException(
                "StringHelper.GetDeterministicHashCode returned null.");
    }

    private static string DescribeRngConstructors()
    {
        string[] signatures = typeof(Rng)
            .GetConstructors(InstanceFlags)
            .Select(DescribeConstructor)
            .ToArray();

        return signatures.Length == 0
            ? "<none>"
            : string.Join("; ", signatures);
    }

    private static string DescribeConstructor(ConstructorInfo constructor)
    {
        string parameters = string.Join(
            ", ",
            constructor.GetParameters()
                .Select(parameter => parameter.ParameterType.Name));

        return $".ctor({parameters})";
    }
}
