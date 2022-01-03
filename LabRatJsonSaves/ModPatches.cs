using HarmonyLib;
using Newtonsoft.Json;
using SynapseLabrat.API;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Yurtle.Saving;

namespace LabRatJsonSaves
{
    [HarmonyPatch(typeof(SavingLoading), nameof(SavingLoading.Save))]
    internal static class SavePatch
    {
        [HarmonyPrefix]
        public static bool OnSave(SavingLoading __instance)
        {
            Log.LogMessage("Save Game");
            try
            {
                var objects = UnityEngine.Object.FindObjectsOfType<InstSavableEntity>();
                var safe = new SaveFile();

                foreach(var obj in objects)
                {
                    var par = obj.transform.parent;
                    obj.transform.parent = null;

                    var entity = new SaveFile.SaveEntity
                    {
                        Guid = Guid.NewGuid(),
                        Player = obj.player,
                        Position = obj.transform.position,
                        PrefabPath = obj.prefabPath,
                        Scale = obj.transform.localScale,
                        Rotation = obj.transform.rotation.eulerAngles,
                    };

                    foreach (var mono in obj.saveComponents)
                    {
                        var savecomp = new SaveFile.ComponentSave();
                        savecomp.ComponentType = mono.GetType();

                        foreach (var field in savecomp.ComponentType.GetFields())
                        {
                            if (!SaveFile.IsTypeValid(field.FieldType)) continue;

                            if (field.FieldType.ToString() == "System.Collections.Generic.List`1[System.Boolean]")
                                savecomp.Fields.Add(field.Name, (field.GetValue(mono) as List<bool>)?.ToArray());
                            else
                                savecomp.Fields.Add(field.Name, field.GetValue(mono));
                        }

                        foreach (var prop in savecomp.ComponentType.GetProperties())
                        {
                            if (!SaveFile.IsTypeValid(prop.PropertyType)) continue;
                            if (!prop.CanWrite) continue;

                            if (prop.PropertyType.ToString() == "System.Collections.Generic.List`1[System.Boolean]")
                                savecomp.Properties.Add(prop.Name, (prop.GetValue(mono) as List<bool>)?.ToArray());
                            else
                                savecomp.Properties.Add(prop.Name, prop.GetValue(mono));
                        }

                        entity.SaveComponents.Add(savecomp);
                    }

                    if (!obj.room)
                        safe.Items.Add(entity);
                    else
                        safe.Rooms.Add(entity);

                    obj.transform.parent = par;
                }

                CreateFiles(out var path);
                File.WriteAllText(path, JsonConvert.SerializeObject(safe));

                GameObject.FindWithTag("saveManager").GetComponent<savePlayerInfo>().save();
                var buttonEvent = __instance.onSave;
                buttonEvent?.Invoke();
                __instance.saveCBGAME();

                Log.LogMessage("Saved Game");
            }
            catch (Exception ex)
            {
                Log.LogMessage(ex);
            }
            return false;
        }

        private static void CreateFiles(out string savefilepath)
        {
            //I still create the empty yrtlsv files since the game don't detect the save files otherwise in the menu
            Directory.CreateDirectory(GameObject.FindWithTag("GameOptions").GetComponent<GameOptions>().getSavePath());
            savefilepath = Path.Combine(GameObject.FindWithTag("GameOptions").GetComponent<GameOptions>().getSavePath(), "savefile.json");
            var path2 = Path.Combine(GameObject.FindWithTag("GameOptions").GetComponent<GameOptions>().getSavePath(), "map.yrtlsv");
            var path3 = Path.Combine(GameObject.FindWithTag("GameOptions").GetComponent<GameOptions>().getSavePath(), "newItems.yrtlsv");
            if (!File.Exists(savefilepath)) File.Create(savefilepath).Close();
            if (!File.Exists(path2)) File.Create(path2).Close();
            if (!File.Exists(path3)) File.Create(path3).Close();
        }
    }

    [HarmonyPatch(typeof(SavingLoading), nameof(SavingLoading.Load))]
    internal static class LoadPatch
    {
        [HarmonyPrefix]
        public static bool OnLoad(SavingLoading __instance)
        {
            Log.LogMessage("Load Game");
            try
            {
                var buttonEvent = __instance.onLoad;
                buttonEvent?.Invoke();
                GetAchievement.getAchievement("LOADSAVE");

                var save = JsonConvert.DeserializeObject<SaveFile>(File.ReadAllText(Path.Combine(GameObject.FindWithTag("GameOptions").GetComponent<GameOptions>().getSavePath(), "savefile.json")));
                LoadSaveFile(save, GameObject.FindWithTag("saveManager").GetComponent<saveNewItems>());

                GameObject.FindWithTag("saveManager").GetComponent<savePlayerInfo>().load();
                __instance.loadCBGAME();
                if (GameOptions.difficulty > 1)
                {
                    Directory.Delete(GameOptions.savePath, true);
                }

                Log.LogMessage("Loaded Game!");
            }
            catch (Exception ex)
            {
                Log.LogMessage(ex);
            }
            return false;
        }

        private static void LoadSaveFile(SaveFile save, saveNewItems newItems)
        {
            newItems.DestroyEverything();
            foreach (var room in save.Rooms)
            {
                if (string.IsNullOrEmpty(room.PrefabPath)) continue;

                var obj = UnityEngine.Object.Instantiate(Resources.Load(room.PrefabPath),room.Position,Quaternion.Euler(room.Rotation)) as GameObject;
                foreach(var componentsave in room.SaveComponents)
                    LoadComponents(componentsave, obj);
            }
            newItems.DestroyAllEntities();
            controller966.all966s.Clear();
            foreach (var item in save.Items)
            {
                if (item.Player)
                {
                    newItems.player.transform.position = item.Position;
                    newItems.player.transform.rotation = Quaternion.Euler(item.Rotation);
                    newItems.player.transform.localScale = item.Scale;

                    foreach (var componentsave in item.SaveComponents)
                        LoadComponents(componentsave, newItems.player);

                    newItems.player.SetActive(true);
                    continue;
                }
                try
                {
                    if (string.IsNullOrEmpty(item.PrefabPath)) continue;

                    var obj = UnityEngine.Object.Instantiate(Resources.Load(item.PrefabPath), item.Position, Quaternion.Euler(item.Rotation)) as GameObject;
                    obj.transform.localScale = item.Scale;

                    foreach (var componentsave in item.SaveComponents)
                        LoadComponents(componentsave, obj);
                }
                catch(Exception ex)
                {
                    //So for some Reason there is even a Error in Vanilla that throws while setting the size of a gameobject and therefore I had to add a second try catch here
                    Log.LogMessage("Base Game Error: " + ex);
                }
            }
        }

        private static void LoadComponents(SaveFile.ComponentSave componentSave, GameObject obj)
        {
            var type = componentSave.ComponentType;
            if (type == null || obj.GetComponent(type) == null) return;

            var component = obj.GetComponent(type);

            foreach (var fieldstorage in componentSave.Fields)
            {
                var field = type.GetField(fieldstorage.Key);
                field.SetValue(component, ConvertObject(fieldstorage.Value, field.FieldType));
            }

            foreach (var propertystorage in componentSave.Properties)
            {
                var property = type.GetProperty(propertystorage.Key);
                property.SetValue(component, ConvertObject(propertystorage.Value, property.PropertyType));
            }
        }

        //Since I store the values as objects the deserializer assumes the type and I have to convert them into the right type
        private static object ConvertObject(object obj, Type finaltype)
        {
            switch (finaltype.ToString())
            {
                case "System.Boolean": return bool.Parse(obj.ToString());
                case "System.String": return obj.ToString();
                case "System.Int32": return int.Parse(obj.ToString());
                case "System.Single": return float.Parse(obj.ToString());
                case "UnityEngine.Color": return obj;
                case "System.Collections.Generic.List`1[System.Boolean]":
                    {
                        var array = (obj as bool[]);
                        if (array == null) return new List<bool>();
                        return array.ToList();
                    }
                default: return obj;
            }
        }
    }

    public class SaveFile
    {
        public static bool IsTypeValid(Type type)
        {
            switch (type.ToString())
            {
                case "System.Boolean":
                case "System.String":
                case "System.Int32":
                case "System.Single":
                case "UnityEngine.Color":
                case "System.Collections.Generic.List`1[System.Boolean]":
                    return true;

                default: 
                    return false;
            }
        }

        public List<SaveEntity> Rooms { get; set; } = new List<SaveEntity>();

        public List<SaveEntity> Items { get; set; } = new List<SaveEntity>();

        public class SaveEntity
        {
            public Guid Guid { get; set; }

            public List<ComponentSave> SaveComponents { get; set; } = new List<ComponentSave>();

            public string PrefabPath { get; set; }

            public bool Player { get; set; }

            public SerializedVector3 Position { get; set; }

            public SerializedVector3 Rotation { get; set; }

            public SerializedVector3 Scale { get; set; }
        }

        public class SerializedVector3
        {
            public float X { get; set; }
            public float Y { get; set; }
            public float Z { get; set; }

            public static implicit operator Vector3(SerializedVector3 vec) 
                => new Vector3(vec.X, vec.Y, vec.Z);

            public static implicit operator SerializedVector3(Vector3 vec)
                => new SerializedVector3
                {
                    X = vec.x,
                    Y = vec.y,
                    Z = vec.z,
                };
        }

        public class ComponentSave
        {
            public Type ComponentType { get; set; }

            public Dictionary<string, object> Fields { get; set; } = new Dictionary<string, object>();

            public Dictionary<string, object> Properties { get; set; } = new Dictionary<string, object>();
        }
    }
}
