using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;
using HarmonyLib;
using System.Reflection;
namespace VanillaFurnitureExpandedFactory
{
    [StaticConstructorOnStartup]
    public class Main
    {
        static Main()
        {
            var harmony = new Harmony("com.VanillaFurnitureExpandedFactory");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
        }
    }
}
