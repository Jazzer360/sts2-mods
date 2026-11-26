using System.Runtime.CompilerServices;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// Decisions #387 and #391: Harmony's emitted code goes through MonoMod's IL generator proxy (DynamicMethodDefinition.GetILGenerator), an assembly
/// MonoMod builds and loads the first time any thread asks for it, with nothing stopping two threads doing that at once: the second load fails
/// ("Assembly with same name is already loaded"). The catalog tests run in parallel, and the first of them to read a game method's code
/// (GameCode.Instructions) could meet another's first field accessor (AccessTools.FieldRefAccess, MethodDelegate), so one failed now and then
/// (OfferedCardsTests.AGridOfCardsShowsItsHoldersAgainAsItScrolls, at half a second in). The proxy is made here, once, before any test of this
/// assembly runs, by the same call the mod makes first at load (GameCode.MakeEmitReady): a module's initializer runs before any of its code, and
/// every other thread waits for it.
/// </summary>
internal static class HarmonyWarmUp
{
    [ModuleInitializer]
    internal static void MakeTheIlGeneratorProxy() => SpireQoL.Diagnostics.GameCode.MakeEmitReady();
}
