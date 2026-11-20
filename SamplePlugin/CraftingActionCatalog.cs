using System.Collections.Generic;

namespace SamplePlugin;

// Catalogo das habilidades JA modeladas no simulador.
// Novas habilidades so devem ser habilitadas depois de implementar e validar
// custos, efeitos, combos, buffs, traits e arredondamentos.
public static class CraftingActionCatalog
{
    private static readonly IReadOnlyDictionary<CraftAction, int> Levels =
        new Dictionary<CraftAction, int>
        {
            [CraftAction.BasicSynthesis] = 1,
            [CraftAction.BasicTouch] = 5,
            [CraftAction.MastersMend] = 7,
            [CraftAction.WasteNot] = 15,
            [CraftAction.Veneration] = 15,
            [CraftAction.GreatStrides] = 21,
            [CraftAction.Innovation] = 26,
            [CraftAction.MuscleMemory] = 54,
            [CraftAction.Groundwork] = 72,
            [CraftAction.StandardTouch] = 18,
            [CraftAction.ByregotsBlessing] = 50,
            [CraftAction.CarefulSynthesis] = 62,
        };

    public static bool IsAvailable(CraftAction action, int level) =>
        Levels.TryGetValue(action, out var requiredLevel) && level >= requiredLevel;

    public static int? UnlockLevel(CraftAction action) =>
        Levels.TryGetValue(action, out var requiredLevel) ? requiredLevel : null;
}
