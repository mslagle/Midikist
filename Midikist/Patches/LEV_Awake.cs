using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Midikist.Patches
{
    [HarmonyPatch(typeof(LEV_LevelEditorCentral), "Awake")]
    public static class LEV_Awake
    {
        public static void Postfix(LEV_LevelEditorCentral __instance) => Plugin.LevelEditorInstance = __instance;
    }
}
