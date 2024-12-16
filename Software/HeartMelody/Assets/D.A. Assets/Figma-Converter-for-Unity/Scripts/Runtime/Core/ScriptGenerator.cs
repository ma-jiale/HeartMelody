﻿using DA_Assets.FCU.Extensions;
using DA_Assets.DAI;
using DA_Assets.Extensions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using DA_Assets.UI;
using System.Threading.Tasks;
using System.Reflection;
using DA_Assets.Logging;
using DA_Assets.Tools;
using DA_Assets.FCU.Model;
using DA_Assets.FCU.Attributes;

#if UITK_LINKER_EXISTS
using DA_Assets.UEL;
#endif

#if TextMeshPro
using TMPro;
#endif

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace DA_Assets.FCU
{
    [Serializable]
    public class ScriptGenerator : MonoBehaviourLinkerRuntime<FigmaConverterUnity>
    {
        public void GenerateScripts()
        {
            _ = GenerateScriptsAsync();
        }

        internal void SerializeObjects()
        {
            _ = SerializeObjectsAsync();
        }

        private async Task SerializeObjectsAsync()
        {
            bool backuped = SceneBackuper.TryBackupActiveScene();

            if (!backuped)
            {
                DALogger.LogError(FcuLocKey.log_cant_execute_because_no_backup.Localize());
                return;
            }

            string frameName1 = null;
            string frameName2 = null;

            string objName1 = null;
            string objName2 = null;

            GameObject rootFrameGO = null;
            FObjectAttribute attribute = null;
            MonoBehaviour rootComponent = null;
            Type rootType = null;
            FieldInfo[] fields = null;

            try
            {
                SyncHelper[] syncHelpers = monoBeh.SyncHelpers.GetAllSyncHelpers();
                monoBeh.SyncHelpers.RestoreRootFrames(syncHelpers);

                if (syncHelpers == null || syncHelpers.Length == 0)
                {
                    Debug.LogError("No SyncHelpers found.");
                    return;
                }

                IEnumerable<GroupedSyncHelpers> rootFrameGroups = syncHelpers
                    .GroupBy(x => x.Data.RootFrame)
                    .Select(group => new GroupedSyncHelpers
                    {
                        RootFrame = group.Key,
                        SyncHelpers = group.ToList()
                    });

                Type[] screenTypes = GetScriptTypes();

                foreach (Type screenType in screenTypes)
                {
                    foreach (GroupedSyncHelpers rootFrameGroup in rootFrameGroups)
                    {
                        if (rootFrameGroup.RootFrame == null)
                        {
                            Debug.LogError($"RootFrame is null.");
                            continue;
                        }

                        rootFrameGO = rootFrameGroup.RootFrame.GameObject;

                        if (rootFrameGO == null)
                        {
                            Debug.LogError($"GameObject for RootFrame '{rootFrameGroup.RootFrame.Id}' is null.");
                            continue;
                        }

                        frameName1 = null;
                        frameName2 = null;

                        switch (monoBeh.Settings.ScriptGeneratorSettings.SerializationMode)
                        {
                            case FieldSerializationMode.SyncHelpers:
                                {
                                    frameName1 = rootFrameGroup.RootFrame.Names.ClassName;
                                    frameName2 = screenType.Name;
                                }
                                break;
                            case FieldSerializationMode.Attributes:
                                {
                                    attribute = screenType.GetCustomAttribute<FObjectAttribute>();

                                    if (attribute != null)
                                    {
                                        frameName1 = rootFrameGroup.RootFrame.Names.FigmaName;
                                        frameName2 = attribute.Name;
                                    }
                                }
                                break;
                            case FieldSerializationMode.GameObjectNames:
                                {
                                    frameName1 = rootFrameGO.name;
                                    frameName2 = screenType.Name;
                                }
                                break;
                            default:
                                throw new NotImplementedException();
                        }

                        if (!frameName1.IsEmpty() && !frameName2.IsEmpty() && frameName1 == frameName2)
                        {
                            rootComponent = rootFrameGO.GetComponent(screenType) as MonoBehaviour;

                            if (rootComponent == null)
                            {
                                rootComponent = rootFrameGO.AddComponent(screenType) as MonoBehaviour;
                                rootFrameGO.SetDirtyExt();
                                Debug.Log($"Added {screenType.Name} component to {rootFrameGO.name}");
                            }

                            rootType = rootComponent.GetType();
                            fields = rootType.GetFields(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);

                            foreach (FieldInfo fieldInfo in fields)
                            {
                                foreach (SyncHelper fieldSH in rootFrameGroup.SyncHelpers)
                                {
                                    if (fieldSH.gameObject == null)
                                        continue;

                                    objName1 = null;
                                    objName2 = null;

                                    switch (monoBeh.Settings.ScriptGeneratorSettings.SerializationMode)
                                    {
                                        case FieldSerializationMode.SyncHelpers:
                                            {
                                                objName1 = fieldSH.Data.Names.FieldName;
                                                objName2 = fieldInfo.Name;
                                            }
                                            break;
                                        case FieldSerializationMode.Attributes:
                                            {
                                                attribute = fieldInfo.GetCustomAttribute<FObjectAttribute>();

                                                if (attribute != null)
                                                {
                                                    objName1 = fieldSH.Data.Names.FigmaName;
                                                    objName2 = attribute.Name;
                                                }
                                            }
                                            break;
                                        case FieldSerializationMode.GameObjectNames:
                                            {
                                                objName1 = fieldSH.gameObject.name;
                                                objName2 = fieldInfo.Name;
                                            }
                                            break;
                                        default:
                                            throw new NotImplementedException();
                                    }

                                    if (!objName1.IsEmpty() && !objName2.IsEmpty() && objName1 == objName2)
                                    {
                                        AssignValue(rootComponent, fieldInfo, fieldSH);
                                        break;
                                    }
                                }
                            }

                            rootFrameGO.SetDirtyExt();

                            await Task.Yield();
                        }
                    }
                }

#if UNITY_EDITOR
                AssetDatabase.SaveAssets();
#endif

                Debug.Log("Serialization by names completed successfully.");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        private void AssignValue(MonoBehaviour rootComponent, FieldInfo fieldInfo, SyncHelper fieldSyncHelper)
        {
            GameObject fieldGameObject = fieldSyncHelper.gameObject;

            if (fieldGameObject == null)
            {
                Debug.LogError($"GameObject for SyncHelper with name {fieldSyncHelper.name} is null.");
                return;
            }

            Type fieldType = fieldInfo.FieldType;

            if (typeof(Component).IsAssignableFrom(fieldType))
            {
                Component component = fieldGameObject.GetComponent(fieldType);

                if (component == null)
                {
                    Debug.LogWarning($"Component for {fieldInfo.Name} is null.");
                    return;
                }

                fieldInfo.SetValue(rootComponent, component);
                rootComponent.SetDirtyExt();

                Debug.Log($"Assigned component {component.GetType().Name} to field {fieldInfo.Name}");
            }
            else if (fieldType == typeof(GameObject))
            {
                fieldInfo.SetValue(rootComponent, fieldGameObject);
                rootComponent.SetDirtyExt();

                Debug.Log($"Assigned GameObject {fieldGameObject.name} to field {fieldInfo.Name}");
            }
            else
            {
                Debug.LogWarning($"Unsupported field type {fieldType.Name} for field {fieldInfo.Name}");
            }
        }

        private async Task GenerateScriptsAsync()
        {
            bool backuped = SceneBackuper.TryBackupActiveScene();

            if (!backuped)
            {
                DALogger.LogError(FcuLocKey.log_cant_execute_because_no_backup.Localize());
                return;
            }

            try
            {
                SyncHelper[] syncHelpers = monoBeh.SyncHelpers.GetAllSyncHelpers();
                monoBeh.SyncHelpers.RestoreRootFrames(syncHelpers);

                var grouped = syncHelpers
                    .GroupBy(item => item.Data.RootFrame)
                    .Select(group => new GroupedSyncHelpers
                    {
                        RootFrame = group.Key,
                        SyncHelpers = group.ToList()
                    });

                foreach (GroupedSyncHelpers group in grouped)
                {
                    string script = GenerateScript(group);
                    Debug.Log(script);
                    string className = group.RootFrame.Names.ClassName;
                    string folderPath = monoBeh.Settings.ScriptGeneratorSettings.OutputPath;
                    Directory.CreateDirectory(folderPath);
                    string filePath = Path.Combine(folderPath, $"{className}.cs");
                    File.WriteAllText(filePath, script.ToString());
                    await Task.Yield();
                }

#if UNITY_EDITOR
                AssetDatabase.Refresh();
#endif
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        public string GenerateUsings()
        {
            List<string> usings = new List<string>
            {

            };

            if (monoBeh.IsUGUI())
            {
                usings.Add("using UnityEngine.UI;");

                if (monoBeh.UsingTextMesh())
                {
                    usings.Add("#if TextMeshPro");
                    usings.Add("using TMPro;");
                    usings.Add("#endif");
                }
            }
            else
            {
                usings.Add("using UnityEngine.UIElements;");
            }

            return string.Join(Environment.NewLine, usings);
        }

        private string GenerateScript(GroupedSyncHelpers group)
        {
            string className = group.RootFrame.Names.ClassName;
            string usings = GenerateUsings();
            string baseClass = FcuConfig.Instance.BaseClass.text;
            string fields = GetFields(group.SyncHelpers);

            string script = string.Format(baseClass,
               usings,
               monoBeh.Settings.ScriptGeneratorSettings.Namespace,
               className,
               monoBeh.Settings.ScriptGeneratorSettings.BaseClass,
               fields);

            return script;
        }

        private string GetFields(List<SyncHelper> syncHelpers)
        {
            StringBuilder elemsSb = new StringBuilder();
            StringBuilder labelsSb = new StringBuilder();

            string sfAtt = $"[{nameof(SerializeField)}]";
            string tab = "        ";

            var syncHelpersWithComponents = syncHelpers.Select(syncHelper =>
            {
                string componentName = DetermineComponentName(syncHelper);
                return new { SyncHelper = syncHelper, ComponentName = componentName };
            });

            var sortedSyncHelpers = syncHelpersWithComponents
                .OrderBy(item => item.ComponentName)
                .ThenBy(item => item.SyncHelper.Data.Names.FieldName)
                .ToList();

            foreach (var item in sortedSyncHelpers)
            {
                var syncHelper = item.SyncHelper;
                string fieldName = syncHelper.Data.Names.FieldName;
                string componentName = item.ComponentName;
                labelsSb.AppendLine($"{tab}{sfAtt} {componentName} {fieldName};");
            }

            return $"{elemsSb}\n{labelsSb}";
        }

        private string DetermineComponentName(SyncHelper syncHelper)
        {
            if (monoBeh.IsUGUI())
            {
                if (syncHelper.gameObject.TryGetComponentSafe(out Text c1))
                {
                    return nameof(Text);
                }
#if TextMeshPro
                else if (syncHelper.gameObject.TryGetComponentSafe(out TMP_Text c2))
                {
                    return nameof(TMP_Text);
                }
                else if (syncHelper.gameObject.TryGetComponentSafe(out TMP_InputField c7))
                {
                    return nameof(TMP_InputField);
                }
#endif
                else if (syncHelper.gameObject.TryGetComponentSafe(out Button c3))
                {
                    return nameof(Button);
                }
                else if (syncHelper.gameObject.TryGetComponentSafe(out FcuButton c4))
                {
                    return nameof(FcuButton);
                }
                else if (syncHelper.gameObject.TryGetComponentSafe(out InputField c6))
                {
                    return nameof(InputField);
                }
                else
                {
                    return nameof(GameObject);
                }
            }
            else
            {
#if UITK_LINKER_EXISTS && UNITY_2021_3_OR_NEWER
                if (syncHelper.gameObject.TryGetComponentSafe(out UitkLabel c1))
                {
                    return nameof(UitkLabel);
                }
                else if (syncHelper.gameObject.TryGetComponentSafe(out UitkButton c2))
                {
                    return nameof(UitkButton);
                }
                else if (syncHelper.gameObject.TryGetComponentSafe(out UitkVisualElement c7))
                {
                    return nameof(UitkVisualElement);
                }
                else
                {
                    return nameof(GameObject);
                }
#else
                return nameof(GameObject);
#endif
            }
        }

        private static Type[] GetScriptTypes()
        {
            try
            {
                Assembly assembly = Assembly.Load("Assembly-CSharp");

                if (assembly == null)
                {
                    Debug.LogError("Failed to load Assembly-CSharp.");
                    return Array.Empty<Type>();
                }

                Type[] allTypes = assembly.GetTypes();

                Type[] componentTypes = allTypes
                    .Where(t => t.IsClass &&
                                t.IsPublic &&
                                typeof(MonoBehaviour).IsAssignableFrom(t))
                    .ToArray();

                return componentTypes;
            }
            catch (Exception ex)
            {
                Debug.LogError("Unexpected error: " + ex.Message);
                return Array.Empty<Type>();
            }
        }
    }
}