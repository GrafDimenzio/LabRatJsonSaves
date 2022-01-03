using SynapseLabrat.API.Mods;
using HarmonyLib;

namespace LabRatJsonSaves
{
    [ModAttributes(
        Author = "Dimenzio",
        Description = "A simple Mod that replaces the saves with json saves",
        LoadPriority = 0,
        Name = "LabRatJsonSaves"
        )]
    public class ModClass : Mod
    {
        public override void Load()
        {
            base.Load();

            var harmony = new Harmony("JsonSaves");
            harmony.PatchAll();
        }
    }
}
