using System.Reflection;
using System.Runtime.CompilerServices;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// Decision #387: Harmony's emitted code goes through MonoMod's IL generator proxy (DynamicMethodDefinition.GetILGenerator), an assembly MonoMod
/// builds and loads the first time any thread asks for it, with nothing stopping two threads doing that at once: the second load fails ("Assembly with
/// same name is already loaded"). The catalog tests run in parallel, and the first of them to read a game method's code (GameCode.Instructions)
/// could meet another's first field accessor (AccessTools.FieldRefAccess, MethodDelegate), so one failed now and then
/// (OfferedCardsTests.AGridOfCardsShowsItsHoldersAgainAsItScrolls, at half a second in). The proxy is made here, once, before any test of this
/// assembly runs: a module's initializer runs before any of its code, and every other thread waits for it. In the game, Harmony's patches
/// (every mod's, at load) make it before anything reads code on another thread.
/// </summary>
internal static class HarmonyWarmUp
{
    [ModuleInitializer]
    internal static void MakeTheIlGeneratorProxy() =>
        SpireQoL.Diagnostics.GameCode.Instructions(typeof(HarmonyWarmUp).GetMethod(nameof(MakeTheIlGeneratorProxy), BindingFlags.Static | BindingFlags.NonPublic)!);
}
