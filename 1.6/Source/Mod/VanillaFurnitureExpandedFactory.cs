using HarmonyLib;
using System.Reflection;
using UnityEngine;
using Verse;

namespace VanillaFurnitureExpandedFactory
{
    public class VanillaFurnitureExpandedFactoryMod : Mod
    {
        public VanillaFurnitureExpandedFactoryMod(ModContentPack content) : base(content)
        {
            harmonyInstance = new Harmony("com.VanillaFurnitureExpandedFactory");
            harmonyInstance.PatchAll(Assembly.GetExecutingAssembly());
            
        }

        public static Harmony harmonyInstance;
    }

    
}