// Product Verification
#define AutoSetupForUnity

// Unity Version Settings
// UNITY_2019_3_OR_NEWER 2019.3 version and latter
#define UNITY_2019_3_OR_NEWER

using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Linq;
using System;

// Used to parse Json
using LitJson;
using RealCode;

#if USING_URP
using UnityEngine.Experimental.Rendering;
#endif
using UnityEngine.Rendering;
using RLPlugin;
#if USING_HDRP
#if UNITY_2019_3_OR_NEWER
using UnityEditor.Rendering.HighDefinition;
#else
using UnityEditor.Experimental.Rendering.HDPipeline;
using UnityEngine.Experimental.Rendering.HDPipeline;
#endif
#endif

namespace RLPlugin
{
    static class StringTable
    {
        // Json version error message:
        public const string JsonVersionError = "Auto Setup is not supported for your current version of CC/iC. Please update your software. The program will continue to work and may have some unexpected issues.";
    }

    // Current Auto Setup Json version:
    static class VersionInfo
    {
        public const int BigVersion = 1;
        public const int MiddleVersion = 10;
    }

    static class DetailMap
    {
        // Detail texture names:
        public const string strHeadThicknessImage = "Head_Thickness.png";
        public const string strHeadSssMaskImage = "Head_Sss_Mask.png";

        public const string strLegThicknessImage = "Leg_Thickness.png";
        public const string strLegSssMaskImage = "Leg_Sss_Mask.png";

        public const string strBodyThicknessImage = "Body_Thickness.png";
        public const string strBodySssMaskImage = "Body_Sss_Mask.png";

        public const string strArmThicknessImage = "Arm_Thickness.png";
        public const string strArmSssMaskImage = "Arm_Sss_Mask.png";
    }

    // Keywords for material assignment:
    static class MaterialKeyWord
    {
        public const string Hair = "hair";
        public const string Transparency = "_Transparency";
        public const string GaSkinBody = "ga_skin_body";
        public const string Skin = "skin_";
        public const string Cornea = "cornea";

        public const string Scalp = "scalp";
        public const string Skullcap = "skullcap";

        public const string Eyelash = "eyelash";
        public const string Eyemoisture = "eyemoisture";
        public const string Eye = "eye";            //Check if unique to the two materials above.

        public const string Occlusion = "occlusion";
        public const string Tearline = "tearline";

        public const string MergeMaterial = "_Merge";
    }

    public class RLEditor : AssetPostprocessor
    {
        static RLEditor()
        {
            // Product name check:
            const string strAutoSetupSymbol = "AutoSetupForUnity";
            if (!PlayerSettings.GetScriptingDefineSymbolsForGroup(BuildTargetGroup.Standalone).Contains(strAutoSetupSymbol))
            {
                PlayerSettings.SetScriptingDefineSymbolsForGroup(BuildTargetGroup.Standalone, strAutoSetupSymbol);
            }
        }

        // Applicable CC character generation:
        public enum EBaseGeneration
        {
            Unknown,
            GameBase,
            G1,
            G3,
            G3Plus
        };

        // Applicable CC Character Uid EBaseGeneration:
        private static readonly Dictionary<string, EBaseGeneration> kGenerationMap = new Dictionary<string, EBaseGeneration>
        {
            { "RL_CC3_Plus", EBaseGeneration.G3Plus },
            { "RL_CharacterCreator_Base_Game_G1_Divide_Eyelash_UV", EBaseGeneration.GameBase },
            { "RL_CharacterCreator_Base_Game_G1_Multi_UV", EBaseGeneration.GameBase },
            { "RL_CharacterCreator_Base_Game_G1_One_UV", EBaseGeneration.GameBase },
            { "RL_CharacterCreator_Base_Std_G3", EBaseGeneration.G3 },
            { "RL_G6_Standard_Series", EBaseGeneration.G1 }
        };

        // CC related paths:
        private const string CC_RESOURCE_SKIN_PATH = "Assets/CC_Resource/Texture/Skin/";
        private const string CC_DETAIL_MAP_NAME = "skin_detail_map.tif";
        private const string strRootRLFolder = "CC_Assets";
#if USING_HDRP && UNITY_2019_1
        private const string CC_BODY_MATERIAL_PRESET = "SSS_Profile_2019_1.mat";
#else
        private const string CC_BODY_MATERIAL_PRESET = "SSS_Profile.mat";
#endif

        // Whether or not to start Auto Setup:
        private static string autoKey = Application.dataPath + "isAuto";
        private static bool bIsAuto = EditorPrefs.GetBool(Application.dataPath + "isAuto");

        private static List<string> importFbxNameList = new List<string>();

        public static void setAuto(bool bState)
        {
            bIsAuto = bState;
        }
        public static bool getAuto()
        {
            return bIsAuto;
        }

        /* 
         * Retrieve CC character generation from the Json data file. 
         * Resolve backwards compatibility with Auto Setup. If the corresponding data is missing, then verify by checking the bone/material name within the Json data file.
         * [in] kAvatar: used to decide the overall structure when the corresponding data is missing. 
         * [in] kJsonData: Json member
         */
        private static EBaseGeneration GetCharacterType(GameObject kAvatar, JsonData kJsonData)
        {
            JsonData kGeneration = SearchJsonKey(kJsonData, "Generation");
            if (kGeneration.IsString)
            {
                string strGenertion = kGeneration.ToString();
                EBaseGeneration eType;
                if (kGenerationMap.TryGetValue(strGenertion, out eType))
                {
                    return eType;
                }
            }
            else
            {
                if (kAvatar)
                {
                    // Assign according to bone name:
                    Transform[] kChildrenObjects = kAvatar.transform.GetComponentsInChildren<Transform>(true);
                    foreach (Transform kChildren in kChildrenObjects)
                    {
                        if (kChildren.gameObject.name == "CC_Base_L_Pinky3")
                        {
                            return EBaseGeneration.G3;
                        }
                        if (kChildren.gameObject.name == "pinky_03_l")
                        {
                            return EBaseGeneration.GameBase;
                        }
                        if (kChildren.gameObject.name == "CC_Base_L_Finger42")
                        {
                            return EBaseGeneration.G1;
                        }
                    }
                    return EBaseGeneration.G3;
                }
                else
                {
                    // Assign according to the material names in the Json file:
                    foreach (string kKey in kJsonData.Keys)
                    {
                        JsonData kFbxJson = SearchJsonChildKey(kJsonData, kKey);
                        JsonData kObjectJson = SearchJsonChildKey(kFbxJson, "Object");

                        JsonData kRootJson = kObjectJson;
                        if (kObjectJson.Keys.Count > 0)
                        {
                            // The old version root is not the file name, however it must be compatible.
                            kRootJson = kObjectJson[0];
                        }

                        JsonData kMesh = SearchJsonChildKey(kRootJson, "Meshes");
                        JsonData kCC_Base_Body = SearchJsonKey(kMesh, "CC_Base_Body");
                        JsonData kCC_Game_Body = SearchJsonKey(kMesh, "CC_Game_Body");
                        JsonData kCC_Game_Tongue = SearchJsonKey(kMesh, "CC_Game_Tongue");
                        if (kCC_Base_Body.IsObject)
                        {
                            JsonData kMaterials = SearchJsonKey(kCC_Base_Body, "Materials");
                            foreach (string kJsonKey in kMaterials.Keys)
                            {
                                if (kJsonKey.ToString().Contains("Std_Skin_Body"))
                                {
                                    // Version 7.8 will carry Generation data and support G3+ characters.
                                    // Prior to v7.8, the character generation is most certainly G3.
                                    // If the Json file does not have Generation data then:
                                    return EBaseGeneration.G3;
                                }
                                else if (kJsonKey.ToString().ToLower().Contains(MaterialKeyWord.GaSkinBody))
                                {
                                    return EBaseGeneration.GameBase;
                                }
                                else if (kJsonKey.ToString().Contains("Skin_Body"))
                                {
                                    return EBaseGeneration.G1;
                                }
                            }
                        }
                        else if (kCC_Game_Body.IsObject || kCC_Game_Tongue.IsObject)
                        {
                            return EBaseGeneration.GameBase;
                        }
                    }
                }
            }
            return EBaseGeneration.Unknown;
        }

        /* 
         * Check if the GameObject is a CC character by the transform name.
         * [in] kObject GameObject
         */
        private static bool IsCCAvatar(ref GameObject kObject)
        {
            return kObject.transform.Find("CC_Base_BoneRoot") || kObject.transform.Find("root");
        }

        /* 
         * Check if the file path is the default CC FBX directory.
         * [in] strPath: file path
         */
        private static bool IsCCFbxAsset(string strPath)
        {
            return strPath.ToLower().Contains(".fbx") && strPath.Contains(strRootRLFolder);
        }

        /* 
         * Check if the file path is the default CC Json directory.
         * [in] strPath: file path
         */
        private static bool IsCCJsonAsset(string strPath)
        {
            return strPath.ToLower().Contains(".json") && strPath.Contains(strRootRLFolder);
        }

        /* 
         * Check if the Auto Setup version is compatible.
         * [in] strJsonVersion: version string
         */
        private static bool CheckAutoSetupVersion(ref string strJsonVersion)
        {
            string[] kVersionInfo = strJsonVersion.Split('.');
            if (kVersionInfo.Length != 4)
            {
                return false;
            }

            int nBigVersion = int.Parse(kVersionInfo[0]);
            int nMiddleVersion = int.Parse(kVersionInfo[1]);
            return nBigVersion == VersionInfo.BigVersion && nMiddleVersion <= VersionInfo.MiddleVersion;
        }

        /* 
         * Make sure the Auto Setup and Json versions are correct before processig assets.
         */
        public void OnPreprocessAsset()
        {
            autoKey = Application.dataPath + "isAuto";
            if (!EditorPrefs.HasKey(autoKey))
            {
                EditorPrefs.SetBool(autoKey, true);
                bIsAuto = true;
            }
            else
            {
                bIsAuto = EditorPrefs.GetBool(autoKey);
            }

            if (IsCCJsonAsset(assetPath))
            {
                bool bVerifyFail = true;
                JsonData kJsonData = "";
                kJsonData = ReadJsonFile(assetPath);

                if (kJsonData.IsObject)
                {
                    JsonData kVersionJson = SearchJsonKey(kJsonData, "Version");
                    if (kVersionJson.IsString)
                    {
                        string strJsonVersion = kVersionJson.ToString();
                        if (CheckAutoSetupVersion(ref strJsonVersion))
                        {
                            bVerifyFail = false;
                        }
                    }
                }

                if (bVerifyFail)
                {
                    EditorUtility.DisplayDialog("Warning!", StringTable.JsonVersionError, "OK");
                }
            }
        }

        /* 
         * The Humanoid parameter must be set according to data read from the Json file before the model is processed.
         */
        public void OnPreprocessModel()
        {
            if (!bIsAuto)
            {
                return;
            }

            if (IsCCFbxAsset(assetPath))
            {
                string strFbxName = Path.GetFileNameWithoutExtension(assetPath);
                if (!importFbxNameList.Contains(strFbxName))
                {
                    return;
                }
                string strPath = assetPath;
                string strJsonPath = strPath.Substring(0, strPath.LastIndexOf(strFbxName) + strFbxName.Length) + ".json";
                JsonData kJsonData = "";
                if (File.Exists(strJsonPath))
                {
                    kJsonData = ReadJsonFile(strJsonPath);
                }
                else if (!strFbxName.Contains("_Motion"))
                {
                    // Only Motion will not be processed.
                    return;
                }

                CreateHumanoidPre(ref kJsonData);
            }
        }

        /* 
         * Additional settings for the model; CC characters need to be set to Humanoid with adjusted animation parameters.
         * [in] kFbxObject: GameObject
         */
        public void OnPostprocessModel(GameObject kFbxObject)
        {
            if (!bIsAuto)
            {
                return;
            }

            string strFbxName = Path.GetFileNameWithoutExtension(assetPath);
            if (!importFbxNameList.Contains(strFbxName))
            {
                return;
            }

            if (IsCCFbxAsset(assetPath))
            {
                UnityEditor.ModelImporter kImporter = this.assetImporter as UnityEditor.ModelImporter;
                //kImporter.importNormals = ModelImporterNormals.Calculate;
                //kImporter.importTangents = ModelImporterTangents.CalculateMikk;

                try
                {
                    if (IsCCAvatar(ref kFbxObject))
                    {
                        CreateHumanoidPost(ref kFbxObject, ref kImporter);
                    }
                    else
                    {
                        if (!kFbxObject.GetComponent<Animator>())
                        {
                            kFbxObject.AddComponent<Animator>();
                        }
                    }
                }
                catch { }
                SetAnimation(kImporter);
            }
        }

        /* 
         * Process the character's generation and bone name before setting to Humanoid.
         * Also create a default set of HumanDescription.
         * [in] kJsonData: Json data used to retrieve generation information.
         */
        public void CreateHumanoidPre(ref JsonData kJsonData)
        {
            ModelImporter kImporter = (ModelImporter)assetImporter;
            //kImporter.importNormals = ModelImporterNormals.Calculate;
            //kImporter.importTangents = ModelImporterTangents.CalculateMikk;
            kImporter.generateAnimations = UnityEditor.ModelImporterGenerateAnimations.GenerateAnimations;
            kImporter.animationType = ModelImporterAnimationType.Human;

#if UNITY_2019_3_OR_NEWER
            // It was possible to retrieve FBX data in OnPreprocessModel. However, this is no longer possible in version 2019.3 making it not possible to get the correct character generation.
            // The detection method now uses Humanoid; this will be reverted and fixed in the future.
            kImporter.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
#endif

            EBaseGeneration eType = GetCharacterType(null, kJsonData);
            if (eType == EBaseGeneration.Unknown)
            {
                string strFbxName = Path.GetFileNameWithoutExtension(assetPath);
                // Only Motion Fbx
                if (!strFbxName.Contains("_Motion"))
                {
                    kImporter.animationType = ModelImporterAnimationType.Generic;
                }
                return;
            }

            HumanDescription kHuman = kImporter.humanDescription;
            Func<string, string, HumanBone> Bone = (strHumanName, strBoneName) => new HumanBone()
            {
                humanName = strHumanName,
                boneName = strBoneName
            };

            #region HumanBoneDescription
            if (eType == EBaseGeneration.G3 || eType == EBaseGeneration.G3Plus)
            {
                kHuman.human = new[] {
                        Bone("Chest", "CC_Base_Spine01"),
                        Bone("Head", "CC_Base_Head"),
                        Bone("Hips", "CC_Base_Hip"),
                        Bone("Jaw", "CC_Base_JawRoot"),
                        Bone("Left Index Distal", "CC_Base_L_Index3"),
                        Bone("Left Index Intermediate", "CC_Base_L_Index2"),
                        Bone("Left Index Proximal", "CC_Base_L_Index1"),
                        Bone("Left Little Distal","CC_Base_L_Pinky3"),
                        Bone("Left Little Intermediate","CC_Base_L_Pinky2"),
                        Bone("Left Little Proximal","CC_Base_L_Pinky1"),
                        Bone("Left Middle Distal", "CC_Base_L_Mid3"),
                        Bone("Left Middle Intermediate", "CC_Base_L_Mid2"),
                        Bone("Left Middle Proximal", "CC_Base_L_Mid1"),
                        Bone("Left Ring Distal", "CC_Base_L_Ring3"),
                        Bone("Left Ring Intermediate", "CC_Base_L_Ring2"),
                        Bone("Left Ring Proximal", "CC_Base_L_Ring1"),
                        Bone("Left Thumb Distal", "CC_Base_L_Thumb3"),
                        Bone("Left Thumb Intermediate", "CC_Base_L_Thumb2"),
                        Bone("Left Thumb Proximal", "CC_Base_L_Thumb1"),
                        Bone("LeftEye","CC_Base_L_Eye"),
                        Bone("LeftFoot", "CC_Base_L_Foot"),
                        Bone("LeftHand", "CC_Base_L_Hand"),
                        Bone("LeftLowerArm", "CC_Base_L_Forearm"),
                        Bone("LeftLowerLeg", "CC_Base_L_Calf"),
                        Bone("LeftShoulder", "CC_Base_L_Clavicle"),
                        Bone("LeftToes", "CC_Base_L_ToeBase"),
                        Bone("LeftUpperArm", "CC_Base_L_Upperarm"),
                        Bone("LeftUpperLeg", "CC_Base_L_Thigh"),
                        Bone("Neck", "CC_Base_NeckTwist01"),
                        Bone("Right Index Distal", "CC_Base_R_Index3"),
                        Bone("Right Index Intermediate", "CC_Base_R_Index2"),
                        Bone("Right Index Proximal", "CC_Base_R_Index1"),
                        Bone("Right Little Distal","CC_Base_R_Pinky3"),
                        Bone("Right Little Intermediate","CC_Base_R_Pinky2"),
                        Bone("Right Little Proximal","CC_Base_R_Pinky1"),
                        Bone("Right Middle Distal", "CC_Base_R_Mid3"),
                        Bone("Right Middle Intermediate", "CC_Base_R_Mid2"),
                        Bone("Right Middle Proximal", "CC_Base_R_Mid1"),
                        Bone("Right Ring Distal", "CC_Base_R_Ring3"),
                        Bone("Right Ring Intermediate", "CC_Base_R_Ring2"),
                        Bone("Right Ring Proximal", "CC_Base_R_Ring1"),
                        Bone("Right Thumb Distal", "CC_Base_R_Thumb3"),
                        Bone("Right Thumb Intermediate", "CC_Base_R_Thumb2"),
                        Bone("Right Thumb Proximal", "CC_Base_R_Thumb1"),
                        Bone("RightEye","CC_Base_R_Eye"),
                        Bone("RightFoot", "CC_Base_R_Foot"),
                        Bone("RightHand", "CC_Base_R_Hand"),
                        Bone("RightLowerArm", "CC_Base_R_Forearm"),
                        Bone("RightLowerLeg", "CC_Base_R_Calf"),
                        Bone("RightShoulder", "CC_Base_R_Clavicle"),
                        Bone("RightToes", "CC_Base_R_ToeBase"),
                        Bone("RightUpperArm", "CC_Base_R_Upperarm"),
                        Bone("RightUpperLeg", "CC_Base_R_Thigh"),
                        Bone("Spine", "CC_Base_Waist"),
                        Bone("UpperChest", "CC_Base_Spine02"),
                    };
            }
            else if (eType == EBaseGeneration.G1)
            {
                kHuman.human = new[] {
                        Bone("Chest", "CC_Base_Spine01"),
                        Bone("Head", "CC_Base_Head"),
                        Bone("Hips", "CC_Base_Hip"),
                        Bone("Jaw", "CC_Base_JawRoot"),
                        Bone("Left Index Distal", "CC_Base_L_Finger12"),
                        Bone("Left Index Intermediate", "CC_Base_L_Finger11"),
                        Bone("Left Index Proximal", "CC_Base_L_Finger10"),
                        Bone("Left Little Distal","CC_Base_L_Finger42"),
                        Bone("Left Little Intermediate","CC_Base_L_Finger41"),
                        Bone("Left Little Proximal","CC_Base_L_Finger40"),
                        Bone("Left Middle Distal", "CC_Base_L_Finger22"),
                        Bone("Left Middle Intermediate", "CC_Base_L_Finger21"),
                        Bone("Left Middle Proximal", "CC_Base_L_Finger20"),
                        Bone("Left Ring Distal", "CC_Base_L_Finger32"),
                        Bone("Left Ring Intermediate", "CC_Base_L_Finger31"),
                        Bone("Left Ring Proximal", "CC_Base_L_Finger30"),
                        Bone("Left Thumb Distal", "CC_Base_L_Finger02"),
                        Bone("Left Thumb Intermediate", "CC_Base_L_Finger01"),
                        Bone("Left Thumb Proximal", "CC_Base_L_Finger00"),
                        Bone("LeftEye","CC_Base_L_Eye"),
                        Bone("LeftFoot", "CC_Base_L_Foot"),
                        Bone("LeftHand", "CC_Base_L_Hand"),
                        Bone("LeftLowerArm", "CC_Base_L_Forearm"),
                        Bone("LeftLowerLeg", "CC_Base_L_Calf"),
                        Bone("LeftShoulder", "CC_Base_L_Clavicle"),
                        Bone("LeftToes", "CC_Base_L_ToeBase"),
                        Bone("LeftUpperArm", "CC_Base_L_Upperarm"),
                        Bone("LeftUpperLeg", "CC_Base_L_Thigh"),
                        Bone("Neck", "CC_Base_NeckTwist01"),
                        Bone("Right Index Distal", "CC_Base_R_Finger12"),
                        Bone("Right Index Intermediate", "CC_Base_R_Finger11"),
                        Bone("Right Index Proximal", "CC_Base_R_Finger10"),
                        Bone("Right Little Distal","CC_Base_R_Finger42"),
                        Bone("Right Little Intermediate","CC_Base_R_Finger41"),
                        Bone("Right Little Proximal","CC_Base_R_Finger40"),
                        Bone("Right Middle Distal", "CC_Base_R_Finger22"),
                        Bone("Right Middle Intermediate", "CC_Base_R_Finger21"),
                        Bone("Right Middle Proximal", "CC_Base_R_Finger20"),
                        Bone("Right Ring Distal", "CC_Base_R_Finger32"),
                        Bone("Right Ring Intermediate", "CC_Base_R_Finger31"),
                        Bone("Right Ring Proximal", "CC_Base_R_Finger30"),
                        Bone("Right Thumb Distal", "CC_Base_R_Finger02"),
                        Bone("Right Thumb Intermediate", "CC_Base_R_Finger01"),
                        Bone("Right Thumb Proximal", "CC_Base_R_Finger00"),
                        Bone("RightEye","CC_Base_R_Eye"),
                        Bone("RightFoot", "CC_Base_R_Foot"),
                        Bone("RightHand", "CC_Base_R_Hand"),
                        Bone("RightLowerArm", "CC_Base_R_Forearm"),
                        Bone("RightLowerLeg", "CC_Base_R_Calf"),
                        Bone("RightShoulder", "CC_Base_R_Clavicle"),
                        Bone("RightToes", "CC_Base_R_ToeBase"),
                        Bone("RightUpperArm", "CC_Base_R_Upperarm"),
                        Bone("RightUpperLeg", "CC_Base_R_Thigh"),
                        Bone("Spine", "CC_Base_Waist"),
                        Bone("UpperChest", "CC_Base_Spine02"),
                    };
            }
            else if (eType == EBaseGeneration.GameBase)
            {
                kHuman.human = new[] {
                        Bone("Chest", "spine_02"),
                        Bone("Head", "head"),
                        Bone("Hips", "pelvis"),
                        Bone("Jaw", "CC_Base_JawRoot"),
                        Bone("Left Index Distal", "index_03_l"),
                        Bone("Left Index Intermediate", "index_02_l"),
                        Bone("Left Index Proximal", "index_01_l"),
                        Bone("Left Little Distal","pinky_03_l"),
                        Bone("Left Little Intermediate","pinky_02_l"),
                        Bone("Left Little Proximal","pinky_01_l"),
                        Bone("Left Middle Distal", "middle_03_l"),
                        Bone("Left Middle Intermediate", "middle_02_l"),
                        Bone("Left Middle Proximal", "middle_01_l"),
                        Bone("Left Ring Distal", "ring_03_l"),
                        Bone("Left Ring Intermediate", "ring_02_l"),
                        Bone("Left Ring Proximal", "ring_01_l"),
                        Bone("Left Thumb Distal", "thumb_03_l"),
                        Bone("Left Thumb Intermediate", "thumb_02_l"),
                        Bone("Left Thumb Proximal", "thumb_01_l"),
                        Bone("LeftEye","CC_Base_L_Eye"),
                        Bone("LeftFoot", "foot_l"),
                        Bone("LeftHand", "hand_l"),
                        Bone("LeftLowerArm", "lowerarm_l"),
                        Bone("LeftLowerLeg", "calf_l"),
                        Bone("LeftShoulder", "clavicle_l"),
                        Bone("LeftToes", "ball_l"),
                        Bone("LeftUpperArm", "upperarm_l"),
                        Bone("LeftUpperLeg", "thigh_l"),
                        Bone("Neck", "neck_01"),
                        Bone("Right Index Distal", "index_03_r"),
                        Bone("Right Index Intermediate", "index_02_r"),
                        Bone("Right Index Proximal", "index_01_r"),
                        Bone("Right Little Distal","pinky_03_r"),
                        Bone("Right Little Intermediate","pinky_02_r"),
                        Bone("Right Little Proximal","pinky_01_r"),
                        Bone("Right Middle Distal", "middle_03_r"),
                        Bone("Right Middle Intermediate", "middle_02_r"),
                        Bone("Right Middle Proximal", "middle_01_r"),
                        Bone("Right Ring Distal", "ring_03_r"),
                        Bone("Right Ring Intermediate", "ring_02_r"),
                        Bone("Right Ring Proximal", "ring_01_r"),
                        Bone("Right Thumb Distal", "thumb_03_r"),
                        Bone("Right Thumb Intermediate", "thumb_02_r"),
                        Bone("Right Thumb Proximal", "thumb_01_r"),
                        Bone("RightEye","CC_Base_R_Eye"),
                        Bone("RightFoot", "foot_r"),
                        Bone("RightHand", "hand_r"),
                        Bone("RightLowerArm", "lowerarm_r"),
                        Bone("RightLowerLeg", "calf_r"),
                        Bone("RightShoulder", "clavicle_r"),
                        Bone("RightToes", "ball_r"),
                        Bone("RightUpperArm", "upperarm_r"),
                        Bone("RightUpperLeg", "thigh_r"),
                        Bone("Spine", "spine_01"),
                        Bone("UpperChest", "spine_03"),
                    };
            }
            #endregion

            for (int i = 0; i < kHuman.human.Length; ++i)
            {
                kHuman.human[i].limit.useDefaultValues = true;
            }

            kHuman.upperArmTwist = 0.5f;
            kHuman.lowerArmTwist = 0.5f;
            kHuman.upperLegTwist = 0.5f;
            kHuman.lowerLegTwist = 0.5f;
            kHuman.armStretch = 0.05f;
            kHuman.legStretch = 0.05f;
            kHuman.feetSpacing = 0.0f;
            kHuman.hasTranslationDoF = false;
            kImporter.humanDescription = kHuman;
        }

        /* 
         * Additional Humanoid settings: Set HumanDescription according to FBX transform data, which is no longer possible with OnPreprocessModel. 
         * [in] kFbxObject: Fbx object
         * [in] kImporter: Model import settings
         */
        public void CreateHumanoidPost(ref GameObject kFbxObject, ref ModelImporter kImporter)
        {
            string strPath = assetPath;
            string strFbxName = Path.GetFileNameWithoutExtension(assetPath);
            string strJsonPath = strPath.Substring(0, strPath.LastIndexOf(strFbxName) + strFbxName.Length) + ".json";
            JsonData kJsonData = "";
            if (File.Exists(strJsonPath))
            {
                kJsonData = ReadJsonFile(strJsonPath);
            }
            else if (!strFbxName.Contains("_Motion"))
            {
                // Only Motion Fbx
                return;
            }

            EBaseGeneration eType = GetCharacterType(kFbxObject, kJsonData);
            if (eType == EBaseGeneration.Unknown)
            {
                if (!strFbxName.Contains("_Motion"))
                {
                    // Only Motion Fbx
                    kImporter.animationType = ModelImporterAnimationType.Generic;
                }
                return;
            }

            HumanDescription kHuman = kImporter.humanDescription;
            Transform[] kTransforms = kFbxObject.GetComponentsInChildren<Transform>();
            SkeletonBone[] kSkeletonBone = new SkeletonBone[kTransforms.Length];
            for (int i = 0; i < kTransforms.Length; i++)
            {
                kSkeletonBone[i].name = kTransforms[i].name;
                kSkeletonBone[i].position = kTransforms[i].localPosition;
                kSkeletonBone[i].rotation = kTransforms[i].localRotation;
                kSkeletonBone[i].scale = kTransforms[i].localScale;
            }
            kHuman.skeleton = kSkeletonBone;
            kImporter.humanDescription = kHuman;

            if (!kFbxObject.GetComponent<Animator>())
            {
                kFbxObject.AddComponent<Animator>();
                kFbxObject.GetComponent<Animator>().avatar = AvatarBuilder.BuildHumanAvatar(kFbxObject, kImporter.humanDescription);
                kFbxObject.GetComponent<Animator>().applyRootMotion = true;
                kFbxObject.GetComponent<Animator>().cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            }
        }

        /* 
         * Create an Animator and apply an FBX animation to it.
         * [in] kFbxObject: Fbx target object
         * [in] assetPath: Fbx asset path
         */
        public static void AutoCreateAnimator(GameObject kFbxObject, string assetPath)
        {
            string strAnimatorPath = Path.GetDirectoryName(assetPath) + "/" + kFbxObject.name + "_animator.controller";
            ModelImporter kModelImporter = AssetImporter.GetAtPath(assetPath) as ModelImporter;
            ModelImporterClipAnimation[] kClipAnimations = kModelImporter.defaultClipAnimations;

            if (kClipAnimations.Length != 0)
            {
                if (!File.Exists(strAnimatorPath))
                {
                    UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(strAnimatorPath);
                }

                var kController = (UnityEditor.Animations.AnimatorController)AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(strAnimatorPath);//, typeof(UnityEditor.Animations.AnimatorController)));
                var kRootStateMachine = kController.layers[0].stateMachine;

                UnityEngine.Object[] kAssets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
                foreach (var kObject in kAssets)
                {
                    AnimationClip kClip = kObject as AnimationClip;
                    if (!kClip || kClip.name.IndexOf("__preview__") != -1)
                    {
                        continue;
                    }

                    bool bIsSameClips = false;
                    for (int k = 0; k < kController.animationClips.GetLength(0); ++k)
                    {
                        if (kClip.name.Contains(kRootStateMachine.states[k].state.name))
                        {
                            bIsSameClips = true;
                            kRootStateMachine.states[k].state.motion = kClip;
                            break;
                        }
                    }
                    if (!bIsSameClips && kClip.name.IndexOf("T-Pose") == -1)
                    {
                        kController.AddMotion(kClip);
                        kController.SetStateEffectiveMotion(kRootStateMachine.states[kController.animationClips.GetLength(0) - 1].state, kClip);
                    }
                }
                EditorUtility.SetDirty(kController);
                AssetDatabase.SaveAssets();
            }
        }

        /* 
         * Set the Animation parameters.
         * [in] kImporter: Model import settings
         */
        public void SetAnimation(ModelImporter kImporter)
        {
            ModelImporterClipAnimation[] kAnimations = kImporter.defaultClipAnimations;
            foreach (ModelImporterClipAnimation kAnimation in kAnimations)
            {
                kAnimation.keepOriginalOrientation = true;
                kAnimation.keepOriginalPositionY = true;
                kAnimation.keepOriginalPositionXZ = true;
                kAnimation.lockRootRotation = true;
                kAnimation.lockRootHeightY = true;
                kAnimation.lockRootPositionXZ = true;

                if (kAnimation.name.ToLower().Contains("_loop"))
                {
                    kAnimation.loopTime = true;
                }
            }
            kImporter.clipAnimations = kAnimations;
        }

        /* 
         * Pre-process textures that contain keywords.
         */
        public void OnPreprocessTexture()
        {
            if (!bIsAuto)
            {
                return;
            }

            TextureImporter kTextureImporter = this.assetImporter as TextureImporter;
            if (assetPath.Contains(strRootRLFolder))
            {
                if (assetPath.ToLower().Contains("_metallicalpha.") ||
                     assetPath.ToLower().Contains("_roughness.") ||
                     assetPath.ToLower().Contains("_metallic.") ||
                     assetPath.ToLower().Contains("_hdrp.") ||
                     assetPath.ToLower().Contains("_ao."))
                {
                    kTextureImporter.sRGBTexture = false;
                }
                else if (assetPath.ToLower().Contains("_normal.")
                       || assetPath.ToLower().Contains("_nbmap.")
                       || assetPath.ToLower().Contains("_micron.")
                       || assetPath.ToLower().Contains("_irisn.")
                       || assetPath.ToLower().Contains("_scleran."))
                {
                    kTextureImporter.textureType = TextureImporterType.NormalMap;
                }
                else if (assetPath.ToLower().Contains("_bump."))
                {
                    kTextureImporter.convertToNormalmap = true; // Bump texture
                    kTextureImporter.heightmapScale = 0.008f;
                    kTextureImporter.textureType = TextureImporterType.NormalMap;
                }
            }
        }

        /* 
         * Post-process materials.
         */
        void OnPostprocessMaterial(Material material)
        {
            if (!bIsAuto)
            {
                return;
            }

            material.hideFlags = HideFlags.None;
        }

        /* 
         * Pre-process animations.
         */
        void OnPreprocessAnimation()
        {
            if (!bIsAuto)
            {
                return;
            }

            if (IsCCFbxAsset(assetPath))
            {
                ModelImporter modelImporter = assetImporter as ModelImporter;
                modelImporter.clipAnimations = modelImporter.defaultClipAnimations;

                SetAnimation(modelImporter);
            }
        }

        /*
         * After assets, process the following:
         * 1. Move textures
         * 2. Create Animator
         * 3. Create materials
         * 4. Produce a Prefab
         * Pay attention: Auto Setup will import FBX twice due to unexpected issues and missing data for the material on the first attempt.
         * Still, you will have to make sure the problem does not persist for this newer version.
         */
        public static void OnPostprocessAllAssets(string[] kImportedAsset, string[] kDeletedAssets, string[] kMovedAssets, string[] kMovedFromAssetPaths)
        {
            if (!bIsAuto)
            {
                return;
            }

            // First move the textures to the top level folder.
            // MoveImageToTopFolder(kImportedAsset);
            // RemoveEmptyTextureFolder(kImportedAsset);

            string strFileRLSourceFolder = Application.dataPath + "/" + strRootRLFolder;
            EnsureDirectoryExists(strFileRLSourceFolder);

            var kAssetsToReload = new HashSet<string>();
            foreach (string strPath in kImportedAsset)
            {
                if (IsCCFbxAsset(strPath))
                {
                    // Retreive Json data
                    string strFbxName = Path.GetFileNameWithoutExtension(strPath);
                    string strJsonPath = strPath.Substring(0, strPath.LastIndexOf(strFbxName) + strFbxName.Length) + ".json";
                    JsonData kJsonData = "";
                    if (File.Exists(strJsonPath))
                    {
                        kJsonData = ReadJsonFile(strJsonPath);
                    }
                    else if (!strFbxName.Contains("_Motion"))
                    {
                        return;
                    }

                    GameObject go = AssetDatabase.LoadAssetAtPath(strPath, typeof(GameObject)) as GameObject;
                    EBaseGeneration eBaseType = EBaseGeneration.Unknown;
                    try
                    {
                        if (IsCCAvatar(ref go))
                        {
                            eBaseType = GetCharacterType(go, kJsonData);
                        }
                    }
                    catch
                    {
                        Debug.Log("Load Avatar Error.");
                        return;
                    }

                    if (!strFbxName.Contains("_Motion"))
                    {
                        // Auto Setup Animator & Material
                        AutoCreateAnimator(go, strPath);
                        CreateMaterials(kAssetsToReload, strPath, eBaseType, kJsonData);
                    }
                }
            }

            // Reload Fbx
            foreach (string strPath in kAssetsToReload)
            {
                AssetDatabase.WriteImportSettingsIfDirty(strPath);
                AssetDatabase.ImportAsset(strPath, ImportAssetOptions.ForceUpdate);
            }

            foreach (string strPath in kImportedAsset)
            {
                if (IsCCFbxAsset(strPath))
                {
                    GameObject kAsset = AssetDatabase.LoadAssetAtPath<GameObject>(strPath);

                    string strFbxName = Path.GetFileNameWithoutExtension(strPath);
                    bool bNoMotion = !strFbxName.Contains("_Motion");

                    if (bNoMotion)
                    {
                        if (importFbxNameList.Contains(strFbxName))
                        {
                            // Set the Prefab
                            if (strPath.ToLower().Contains("_lod"))
                            {
                                CreateOneLODPrefabFromModel(strPath, kAsset);
                            }
                            else
                            {
                                CreatePrefabFromModel(strPath, kAsset);
                            }
                        }
                    }
                }
            }

            foreach (string strPath in kImportedAsset)
            {
                if (IsCCFbxAsset(strPath))
                {
                    string strFbxName = Path.GetFileNameWithoutExtension(strPath);
                    if (!importFbxNameList.Contains(strFbxName))
                    {
                        importFbxNameList.Add(strFbxName);
                        AssetDatabase.WriteImportSettingsIfDirty(strPath);
                        AssetDatabase.ImportAsset(strPath, ImportAssetOptions.ForceUpdate);
                    }
                    else
                    {
                        importFbxNameList.Remove(strFbxName);
                    }
                }
            }
        }

        /*
         * Create the material read/save folder.
         */
        private static void CreateMaterialFolder(string strFilePath, ref string strMaterialsPath)
        {
            string strFbxName = Path.GetFileNameWithoutExtension(strFilePath);
            strMaterialsPath = Path.GetDirectoryName(strFilePath) + "/Materials" + "/" + strFbxName;
            EnsureDirectoryExists(strMaterialsPath);
        }

        /*
         * Set default material values based on the type of shaders and materials according to the Json parameters.
         * Materials are assessed by their names, therefore the order by which they are handled is very important.
         * [in] assetsToReload: files that fail to import will need to try again. 
         * [in] strAssetPath: CC Fbx file path
         * [in] eBaseType: Character generation
         * [in] kJsonData: Json data
         */
        private static void CreateMaterials(HashSet<string> assetsToReload, string strAssetPath, EBaseGeneration eBaseType, JsonData kJsonData)
        {
            // Create the write material write folder
            string strMaterialsPath = "";
            CreateMaterialFolder(strAssetPath, ref strMaterialsPath);

            // Get all of the materials
            ModelImporter kImporter = AssetImporter.GetAtPath(strAssetPath) as ModelImporter;
            var kMaterials = AssetDatabase.LoadAllAssetsAtPath(kImporter.assetPath).Where(x => x.GetType() == typeof(Material));

            foreach (Material kMaterial in kMaterials)
            {
                string newAssetPath = strMaterialsPath + "/" + kMaterial.name + ".mat";
                var error = AssetDatabase.ExtractAsset(kMaterial, newAssetPath);
                if (string.IsNullOrEmpty(error))
                {
                    assetsToReload.Add(kImporter.assetPath);
                }
                AssetDatabase.SaveAssets();
            }

            // Take the material data that corresbond with the Json data.
            var strFileName = Path.GetFileNameWithoutExtension(strAssetPath);
            JsonData kFbxJson = SearchJsonChildKey(kJsonData, strFileName);
            JsonData kObjectJson = SearchJsonChildKey(kFbxJson, "Object");

            JsonData kRootJson = kObjectJson;
            if (kObjectJson.Keys.Count > 0)
            {
                // The legacy root will not be the file name, however it needs to be backwards compatible.
                kRootJson = kObjectJson[0];
            }
            JsonData kMeshJson = SearchJsonChildKey(kRootJson, "Meshes");

#if USING_HDRP
            bool isMulti = false;
#endif
            if (Directory.Exists(strMaterialsPath))
            {
                var kDirectoryInfo = new DirectoryInfo(strMaterialsPath);
                var kFileInfo = kDirectoryInfo.GetFiles();

                foreach (FileInfo kInfo in kFileInfo)
                {
                    if (kInfo.Name.Contains(".mat") && !kInfo.Name.Contains(".meta") && kInfo.Name.ToLower().Contains("head"))
                    {
#if USING_HDRP
                        isMulti = true;
#endif
                        break;
                    }
                }

#if USING_HDRP && UNITY_2019_1
                HDRenderPipelineAsset kPipeline = Resources.Load< HDRenderPipelineAsset >( "HDRenderPipelineAsset" );
                GraphicsSettings.renderPipelineAsset = kPipeline;
                EditorUtility.SetDirty( GraphicsSettings.renderPipelineAsset );
#endif

                foreach (FileInfo kInfo in kFileInfo)
                {
                    if (kInfo.Name.Contains(".mat") && !kInfo.Name.Contains(".meta"))
                    {
                        string strMaterialName = Path.GetFileNameWithoutExtension(kInfo.Name);
                        Material rlMaterail = (Material)(AssetDatabase.LoadAssetAtPath(strMaterialsPath + "/" + kInfo.Name, typeof(Material)));
                        if (!rlMaterail) continue;
                        string strShaderName = rlMaterail.shader.name;
                        if (strShaderName == "HDRenderPipeline/Lit" || strShaderName == "HDRP/Lit")
                        {
                            if (rlMaterail.name.ToLower().Contains(MaterialKeyWord.Skin))
                            {
                                // HDRP body material need to adopt properly configured Diffuse Profile.
                                Material kPresetMaterail = (Material)(AssetDatabase.LoadAssetAtPath(CC_RESOURCE_SKIN_PATH + CC_BODY_MATERIAL_PRESET, typeof(Material)));
                                rlMaterail.CopyPropertiesFromMaterial(kPresetMaterail);
                            }
                        }

                        JsonData kMaterialJson = SearchJsonKey(kMeshJson, strMaterialName);
                        JsonData kTextureJson = SearchJsonKey(kMaterialJson, "Textures");
                        string kJsonPathKey = "Texture Path";

                        /*
                         * Put the Json directory under "Asset"
                         */
                        Func<string, string> ConvertToAssetPath = (string strJsonPath) =>
                        {
                            if (strJsonPath.Length == 0)
                            {
                                return "";
                            }
                            return Path.GetDirectoryName(strAssetPath) + strJsonPath.Substring(1);
                        };


                        /*
                         * Adjust the material texture according to the Json keyword and path settings.
                         */
                        Func<string, string, bool> fnSetTexture = (string strJsonKey, string strUnityChannel) =>
                        {
                            JsonData kTextureData = SearchJsonKey(kTextureJson, strJsonKey);
                            if (kTextureData.IsObject)
                            {
                                string strTexturePath = kTextureData[kJsonPathKey].ToString();
                                strTexturePath = ConvertToAssetPath(strTexturePath);
                                /*
                                if (strTexturePath.Contains("textures/"))
                                {
                                    strTexturePath = ChangeImageTexturePath(strTexturePath);
                                }
                                */
                                if (File.Exists(strTexturePath))
                                {
                                    rlMaterail.SetTexture(strUnityChannel, LoadTexture(strTexturePath));
                                    rlMaterail.SetTextureScale(strUnityChannel, new Vector2(1, 1));
                                    rlMaterail.SetTextureOffset(strUnityChannel, new Vector2(0, 0));
                                    return true;
                                }
                            }
                            return false;
                        };

                        // Set the texture and parameter settings according to Json data.
                        JsonData kDiffuseColorData = SearchJsonKey(kMaterialJson, "Diffuse Color");
                        Color kBaseColor = Color.white;
                        if (kDiffuseColorData.IsArray)
                        {
                            kBaseColor.r = float.Parse(kDiffuseColorData[0].ToString()) / 255.0f;
                            kBaseColor.g = float.Parse(kDiffuseColorData[1].ToString()) / 255.0f;
                            kBaseColor.b = float.Parse(kDiffuseColorData[2].ToString()) / 255.0f;
                        }
                        rlMaterail.SetColor("_BaseColor", kBaseColor);

                        JsonData kTwoSideData = SearchJsonKey(kMaterialJson, "Two Side");
                        if (kTwoSideData.IsBoolean)
                        {
                            float fTwoSide = bool.Parse(kTwoSideData.ToString()) ? 1.0f : 0.0f;
                            rlMaterail.SetFloat("_DoubleSidedEnable", fTwoSide);
                        }

                        if (strShaderName == "Standard"
                            || strShaderName == "LightweightPipeline/Standard (Physically Based)"
                            || strShaderName == "Lightweight Render Pipeline/Lit"
                            || strShaderName == "Universal Render Pipeline/Lit")
                        {
                            //Diffuse
#if USING_URP
                            rlMaterail.SetFloat("_WorkflowMode", 1f);
                            string strDiffuseChannel = "_BaseMap";
                            string strColorChannel = "_BaseColor";
#else
                            string strDiffuseChannel = "_MainTex";
                            string strColorChannel = "_Color";
#endif

                            bool bSetDiffuse = fnSetTexture("Base Color", strDiffuseChannel);
                            if (!bSetDiffuse)
                            {
                                if (rlMaterail.name.ToLower().Contains(MaterialKeyWord.Cornea)
                                  || rlMaterail.name.ToLower().Contains(MaterialKeyWord.Eyemoisture)
                                  || rlMaterail.name.ToLower().Contains(MaterialKeyWord.Occlusion)
                                  || rlMaterail.name.ToLower().Contains(MaterialKeyWord.Tearline))
                                {
                                    rlMaterail.SetColor(strColorChannel, new Color(1.0f, 1.0f, 1.0f, 0.0f));
                                    //rlMaterail.SetColor("_Color", new Color(1.0f, 1.0f, 1.0f, 0.0f));
                                }
                            }

                            //Normal
                            fnSetTexture("Normal", "_BumpMap");

                            //Bump
                            bool bSetBumpMap = fnSetTexture("Bump", "_BumpMap");
                            if (bSetBumpMap)
                            {
                                rlMaterail.EnableKeyword("_NORMALMAP");
                            }

                            //MetallicAlpha
                            bool bSetMetallicAlpha = fnSetTexture("MetallicAlpha", "_MetallicGlossMap");
                            if (bSetMetallicAlpha)
                            {
                                rlMaterail.EnableKeyword("_METALLICGLOSSMAP");

                                float fGlossScale = rlMaterail.name.ToLower().Contains(MaterialKeyWord.Skin) ? 0.88f : 0.7f;
                                rlMaterail.SetFloat("_GlossMapScale", fGlossScale);
                            }

                            bool bSetSpecular = false;
                            //Specular
                            if (rlMaterail.name.ToLower().Contains("_tra") && !rlMaterail.name.ToLower().Contains("_pbr"))
                            {
                                bSetSpecular = fnSetTexture("Specular", "_SpecGlossMap");
                                if (bSetSpecular)
                                {
                                    if (strShaderName == "Standard")
                                    {
                                        rlMaterail.shader = Shader.Find("Standard (Specular setup)");
                                        fnSetTexture("Specular", "_SpecGlossMap");
                                    }
                                }
                            }

                            //AO
                            fnSetTexture("AO", "_OcclusionMap");

                            // The Unity's displacement map is different from ours, therefore we do not support it for now.
                            // fnSetTexture( "Displacement", "_ParallaxMap" );

                            //Glow
                            bool bSetGlow = fnSetTexture("Glow", "_EmissionMap");
                            if (bSetGlow)
                            {
                                rlMaterail.DisableKeyword("_EMISSION");
                                rlMaterail.EnableKeyword("_EMISSION");
                            }

                            // Set the material parameters according to the Render Template and specific materials names.
                            if (strShaderName == "Standard" || strShaderName == "Standard (Specular setup)")
                            {
                                if (rlMaterail.name.ToLower().Contains(MaterialKeyWord.Hair) || rlMaterail.name.ToLower().Contains(MaterialKeyWord.Eyelash) || rlMaterail.name.Contains(MaterialKeyWord.Transparency))
                                {
                                    rlMaterail.SetFloat("_Mode", 2f);
                                    rlMaterail.SetOverrideTag("RenderType", "Transparent");
                                    rlMaterail.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                                    rlMaterail.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                                    rlMaterail.SetInt("_ZWrite", 0);
                                    rlMaterail.DisableKeyword("_ALPHATEST_ON");
                                    rlMaterail.EnableKeyword("_ALPHABLEND_ON");
                                    rlMaterail.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                                    rlMaterail.renderQueue = 3000;
                                }
                                else if (rlMaterail.name.ToLower().Contains(MaterialKeyWord.GaSkinBody))
                                {
                                    rlMaterail.SetFloat("_Mode", 1f);
                                    rlMaterail.SetFloat("_Cutoff", 1f);
                                    rlMaterail.SetOverrideTag("RenderType", "TransparentCutout");
                                    rlMaterail.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                                    rlMaterail.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                                    rlMaterail.SetInt("_ZWrite", 1);
                                    rlMaterail.EnableKeyword("_ALPHATEST_ON");
                                    rlMaterail.DisableKeyword("_ALPHABLEND_ON");
                                    rlMaterail.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                                    rlMaterail.renderQueue = 2450;
                                }
                                else
                                {
                                    if (rlMaterail.name.ToLower().Contains(MaterialKeyWord.Skin))
                                    {
                                        rlMaterail.SetFloat("_GlossMapScale", 0.88f);
                                        rlMaterail.SetFloat("_Mode", 0f);
                                        rlMaterail.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                                        rlMaterail.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                                        rlMaterail.SetInt("_ZWrite", 1);
                                        rlMaterail.DisableKeyword("_ALPHATEST_ON");
                                        rlMaterail.DisableKeyword("_ALPHABLEND_ON");
                                        rlMaterail.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                                        rlMaterail.renderQueue = -1;
                                    }
                                    else if (rlMaterail.name.ToLower().Contains(MaterialKeyWord.Cornea)
                                           || rlMaterail.name.ToLower().Contains(MaterialKeyWord.Eyemoisture)
                                           || rlMaterail.name.ToLower().Contains(MaterialKeyWord.Occlusion)
                                           || rlMaterail.name.ToLower().Contains(MaterialKeyWord.Tearline))
                                    {
                                        rlMaterail.SetFloat("_Mode", 3f);
                                        rlMaterail.SetOverrideTag("RenderType", "Transparent");
                                        rlMaterail.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                                        rlMaterail.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                                        rlMaterail.SetInt("_ZWrite", 0);
                                        rlMaterail.DisableKeyword("_ALPHATEST_ON");
                                        rlMaterail.DisableKeyword("_ALPHABLEND_ON");
                                        rlMaterail.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                                        rlMaterail.renderQueue = 3000;
                                    }
                                    else
                                    {
                                        rlMaterail.SetFloat("_GlossMapScale", 0.7f);
                                        rlMaterail.SetFloat("_Mode", 0f);
                                        rlMaterail.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                                        rlMaterail.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                                        rlMaterail.SetInt("_ZWrite", 1);
                                        rlMaterail.DisableKeyword("_ALPHATEST_ON");
                                        rlMaterail.DisableKeyword("_ALPHABLEND_ON");
                                        rlMaterail.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                                        rlMaterail.renderQueue = -1;
                                    }
                                }

                                if (bSetSpecular)
                                {
                                    rlMaterail.SetFloat("_GlossMapScale", 0.2f);
                                }

                                if (rlMaterail.name.Contains(MaterialKeyWord.MergeMaterial))
                                {
                                    rlMaterail.SetFloat("_Mode", 1f);
                                    rlMaterail.SetFloat("_Cutoff", 0.5f);
                                }
                            }
                            else if (strShaderName == "LightweightPipeline/Standard (Physically Based)"
                                || strShaderName == "Lightweight Render Pipeline/Lit"
                                || strShaderName == "Universal Render Pipeline/Lit")
                            {
#if USING_URP
                                if (rlMaterail.name.ToLower().Contains(MaterialKeyWord.Hair) || rlMaterail.name.ToLower().Contains(MaterialKeyWord.Eyelash) || rlMaterail.name.Contains(MaterialKeyWord.Transparency))
                                {
                                    rlMaterail.SetFloat("_Surface", 1f);
                                    rlMaterail.SetInt("_Cull", 0);
                                    rlMaterail.SetOverrideTag("RenderType", "Transparent");
                                    rlMaterail.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                                    rlMaterail.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                                    rlMaterail.SetInt("_ZWrite", 0);
                                    rlMaterail.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                                    rlMaterail.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                                    rlMaterail.SetShaderPassEnabled("ShadowCaster", false);
                                }
                                else if (rlMaterail.name.ToLower().Contains(MaterialKeyWord.GaSkinBody))
                                {
                                    rlMaterail.SetFloat("_Surface", 0f);
                                    rlMaterail.SetFloat("_AlphaClip", 1f);
                                    rlMaterail.SetFloat("_Cutoff", 1f);
                                    //rlMaterail.SetOverrideTag("RenderType", "TransparentCutout");
                                    rlMaterail.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                                    rlMaterail.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                                    rlMaterail.SetInt("_ZWrite", 1);
                                    rlMaterail.EnableKeyword("_ALPHATEST_ON");
                                    rlMaterail.DisableKeyword("_ALPHABLEND_ON");
                                    rlMaterail.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                                    rlMaterail.renderQueue = 2450;
                                }
                                else
                                {
                                    if (rlMaterail.name.ToLower().Contains(MaterialKeyWord.Cornea)
                                           || rlMaterail.name.ToLower().Contains(MaterialKeyWord.Eyemoisture)
                                           || rlMaterail.name.ToLower().Contains(MaterialKeyWord.Occlusion)
                                           || rlMaterail.name.ToLower().Contains(MaterialKeyWord.Tearline))
                                    {
                                        rlMaterail.SetFloat("_Surface", 1f);
                                        rlMaterail.SetInt("_Cull", 0);
                                        rlMaterail.SetOverrideTag("RenderType", "Transparent");
                                        rlMaterail.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                                        rlMaterail.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                                        rlMaterail.SetInt("_ZWrite", 0);
                                        rlMaterail.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                                        rlMaterail.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                                        rlMaterail.SetShaderPassEnabled("ShadowCaster", false);
                                    }
                                    else
                                    {
                                        rlMaterail.SetFloat("_Surface", 0f);
                                        rlMaterail.SetInt("_Cull", 0);
                                        rlMaterail.SetOverrideTag("RenderType", "");
                                        rlMaterail.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                                        rlMaterail.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                                        rlMaterail.SetInt("_ZWrite", 1);
                                        rlMaterail.DisableKeyword("_ALPHATEST_ON");
                                        rlMaterail.DisableKeyword("_ALPHABLEND_ON");
                                        rlMaterail.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                                        rlMaterail.DisableKeyword("_ALPHAMODULATE_ON");
                                        rlMaterail.renderQueue = -1;
                                        rlMaterail.SetShaderPassEnabled("ShadowCaster", true);
                                    }

                                }

                                SetLWRPMaterialParameter(rlMaterail);

                                if (bSetSpecular)
                                {
                                    rlMaterail.SetFloat("_Smoothness", 0.2f);
                                    rlMaterail.SetFloat("_WorkflowMode", 0f);
                                    rlMaterail.EnableKeyword("_SPECULAR_SETUP");
                                }


                                if (rlMaterail.name.Contains(MaterialKeyWord.MergeMaterial))
                                {
                                    rlMaterail.SetFloat("_AlphaClip", 1f);
                                    rlMaterail.SetFloat("_Cutoff", 0.5f);
                                }

                                SetLWRPMaterialKeywords(rlMaterail);

#endif
                            }
                        }
                        else if (strShaderName == "HDRenderPipeline/Lit" || strShaderName == "HDRP/Lit")
                        {
#if USING_HDRP
                            if (strShaderName == "HDRenderPipeline/Lit")// HDRP 1 ~ 4.7
                            {
                                rlMaterail.shader = Shader.Find("HDRenderPipeline/Lit");
                            }
                            else if (strShaderName == "HDRP/Lit")// after HDRP 4.8
                            {
                                rlMaterail.shader = Shader.Find("HDRP/Lit");
                            }

                            // Set the Detail Map.
                            if (rlMaterail.name.ToLower().Contains("skin") && eBaseType != EBaseGeneration.Unknown)
                            {
                                SetMaterialDetailMap(rlMaterail, eBaseType, isMulti);
                            }

                            // Adjust the textures and parameters according to the Json data.
                            //Diffuse
                            bool bSetDiffuse = fnSetTexture("Base Color", "_BaseColorMap");
                            if (!bSetDiffuse)
                            {
                                if (rlMaterail.name.ToLower().Contains(MaterialKeyWord.Cornea) || rlMaterail.name.ToLower().Contains(MaterialKeyWord.Eyemoisture) || rlMaterail.name.ToLower().Contains(MaterialKeyWord.Occlusion) || rlMaterail.name.ToLower().Contains(MaterialKeyWord.Tearline))
                                {
                                    rlMaterail.SetColor("_BaseColor", new Color(1.0f, 1.0f, 1.0f, 0.0f));
                                }
                            }

                            //Normal
                            fnSetTexture("Normal", "_NormalMap");

                            //Bump
                            fnSetTexture("Bump", "_NormalMap");

                            //Displacement is different from ours, so do not use for now.
                            //fnSetTexture( "Displacement", "_ParallaxMap" );

                            //Glow
                            bool bSetGlow = fnSetTexture("Glow", "_EmissiveColorMap");
                            if (bSetGlow)
                            {
                                rlMaterail.EnableKeyword("_EMISSION");
                                rlMaterail.SetFloat("_AlbedoAffectEmissive", 1f);
                                if (rlMaterail.name.ToLower().Contains(MaterialKeyWord.Skin))
                                {
                                    rlMaterail.SetColor("_EmissiveColor", Color.black);
                                }
                                else
                                {
                                    //rlMaterail.SetColor( "_EmissiveColor", Color.white * 16 );
                                    rlMaterail.SetColor("_EmissiveColor", Color.black);
                                }
                            }

                            //Hdrp
                            fnSetTexture("HDRP", "_MaskMap");

                            //Specular
                            bool bSetSpecular = false;
                            if (rlMaterail.name.ToLower().Contains("_tra") && !rlMaterail.name.ToLower().Contains("_pbr"))
                            {
                                bSetSpecular = fnSetTexture("Specular", "_SpecularColorMap");
                            }

                            // Adjust each material parameter according to the Render Template and Specific Material Name.
                            if (rlMaterail.name.ToLower().Contains(MaterialKeyWord.Hair) || rlMaterail.name.ToLower().Contains(MaterialKeyWord.Eyelash) || rlMaterail.name.Contains(MaterialKeyWord.Transparency))
                            {
                                rlMaterail.SetFloat("_SurfaceType", 1f);
                                rlMaterail.SetFloat("_DoubleSidedEnable", 1f);
                                rlMaterail.SetFloat("_AlphaCutoff", 0.25f);
                                rlMaterail.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                                rlMaterail.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                                rlMaterail.SetInt("_ZWrite", 0);
                                rlMaterail.DisableKeyword("_ALPHATEST_ON");
                                rlMaterail.EnableKeyword("_ALPHABLEND_ON");
                                rlMaterail.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                                rlMaterail.renderQueue = 3000;
                                rlMaterail.SetFloat("_BlendMode", 0);
                                rlMaterail.SetFloat("_AlphaCutoffEnable", 1);
                                rlMaterail.SetFloat("_EnableBlendModePreserveSpecularLighting", 1);
                                rlMaterail.SetFloat("_TransparentBackfaceEnable", 1);
                                rlMaterail.SetFloat("_TransparentDepthPostpassEnable", 1);
                            }
                            else if (rlMaterail.name.ToLower().Contains(MaterialKeyWord.Occlusion) || rlMaterail.name.ToLower().Contains(MaterialKeyWord.Tearline))
                            {
                                rlMaterail.SetFloat("_SurfaceType", 1f);
                                rlMaterail.SetFloat("_TransparentDepthPostpassEnable", 1);
                                rlMaterail.SetFloat("_TransparentDepthPrepassEnable", 1);
                                rlMaterail.renderQueue = 3000;
                            }
                            else if (rlMaterail.name.ToLower().Contains(MaterialKeyWord.Cornea) || rlMaterail.name.ToLower().Contains(MaterialKeyWord.Eyemoisture))
                            {
                                rlMaterail.SetFloat("_SurfaceType", 1f);
                                rlMaterail.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                                rlMaterail.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                                rlMaterail.SetInt("_ZWrite", 0);
                                rlMaterail.DisableKeyword("_ALPHATEST_ON");
                                rlMaterail.EnableKeyword("_ALPHABLEND_ON");
                                rlMaterail.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                                rlMaterail.renderQueue = 3000;
                            }
                            else
                            {
                                rlMaterail.SetFloat("_SurfaceType", 0f);
                                rlMaterail.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                                rlMaterail.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                                rlMaterail.SetInt("_ZWrite", 1);
                                rlMaterail.DisableKeyword("_ALPHATEST_ON");
                                rlMaterail.DisableKeyword("_ALPHABLEND_ON");
                                rlMaterail.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                                rlMaterail.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
                                rlMaterail.SetFloat("_LinkDetailsWithBase", 0);
                                rlMaterail.SetFloat("_SurfaceType", 0);
                                rlMaterail.SetFloat("_BlendMode", 0);
                                rlMaterail.SetFloat("_AlphaCutoffEnable", 0);
                                rlMaterail.SetFloat("_EnableBlendModePreserveSpecularLighting", 1);
                                rlMaterail.SetFloat("_Metallic", 0f);
                                if (rlMaterail.name.ToLower().Contains(MaterialKeyWord.Skin))
                                {
                                    rlMaterail.SetFloat("_MaterialID", 0f);
                                    rlMaterail.SetFloat("_DiffusionProfile", 1f);
                                }

                                if (rlMaterail.name.ToLower().Contains(MaterialKeyWord.GaSkinBody))
                                {
                                    rlMaterail.SetFloat("_AlphaCutoffEnable", 1);
                                    rlMaterail.SetFloat("_AlphaCutoff", 1f);
                                }
                            }

                            SetHDRPMaterialParameter(rlMaterail);

                            if (bSetSpecular)
                            {
                                rlMaterail.SetFloat("_MaterialID", 4f);
                                rlMaterail.SetFloat("_Smoothness", 0.2f);
                                rlMaterail.SetFloat("_SmoothnessRemapMax", 0.5f);
                            }

                            if (rlMaterail.name.ToLower().Contains(MaterialKeyWord.Scalp) || rlMaterail.name.ToLower().Contains(MaterialKeyWord.Skullcap))
                            {
                                rlMaterail.SetFloat("_TransparentSortPriority", -1);
                            }

                            if (rlMaterail.name.Contains(MaterialKeyWord.MergeMaterial))
                            {
                                rlMaterail.SetFloat("_AlphaCutoffEnable", 1);
                                rlMaterail.SetFloat("_AlphaCutoff", 0.5f);
                            }
#if UNITY_2019_3_OR_NEWER
                            HDShaderUtils.ResetMaterialKeywords(rlMaterail);

#else
                            HDEditorUtils.ResetMaterialKeywords( rlMaterail );
#endif
#endif
                        }
                        EditorUtility.SetDirty(rlMaterail);
                        AssetDatabase.SaveAssets();
                    }
                }
            }
        }

        /*
         * Applies the preconfigured kFbxObject to the scene, deploys an Animator, and save as a Prefab.
         * [in] path: FBX Path
         * [in] kFbxObject: FBX GameObject
         */
        private static void CreatePrefabFromModel(string path, GameObject kFbxObject)
        {
            // Create a Prefab folder:
            string strPathDir = Path.GetDirectoryName(path) + "/";
            strPathDir = strPathDir.Replace("\\", "/");
            string strPrefabPath = strPathDir + "Prefabs/";
            EnsureDirectoryExists(strPrefabPath);

            string strDestinationPath = strPrefabPath + Path.GetFileNameWithoutExtension(path) + ".prefab";
            var strController = strPathDir + kFbxObject.name + "_animator.controller";

            // Apply to the scene: 
            GameObject kSceneObject = GameObject.Instantiate<GameObject>(kFbxObject, null);
            if (File.Exists(strDestinationPath))
            {
                try
                {
                    var kPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(strDestinationPath) as GameObject;
                    kSceneObject = GameObject.Instantiate<GameObject>(kPrefab, null);
                }
                catch { }
            }

            // Apply Animator:
            if (!kSceneObject.GetComponent<Animator>().runtimeAnimatorController)
            {
                kSceneObject.GetComponent<Animator>().runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(strController);
                kSceneObject.GetComponent<Animator>().applyRootMotion = true;
                kSceneObject.GetComponent<Animator>().cullingMode = AnimatorCullingMode.CullUpdateTransforms;

                PrefabUtility.SaveAsPrefabAsset(kSceneObject, strDestinationPath);
            }
            UnityEngine.Object.DestroyImmediate(kSceneObject);
        }

        /*
         * Applies the preconfigured kFbxObject to the scene, sets the LODGroup, and save as Prefab.
         * Handle cases without Lod0 (Export Lod Fbx with Keep Origin Avatar)
         * [in] strPath: Fbx file path
         * [in] kModelAsset: Fbx GameObject
         */
        private static void CreateOneLODPrefabFromModel(string strPath, GameObject kModelAsset)
        {
            GameObject kLodObject = new GameObject();
            LODGroup kLodGroup = kLodObject.AddComponent<LODGroup>();

            string kModelFileName = Path.GetFileNameWithoutExtension(strPath);
            string strPrefabPath = Path.GetDirectoryName(strPath) + "/Prefabs/";
            EnsureDirectoryExists(strPrefabPath);
            string strDestinationPath = strPrefabPath + kModelFileName + ".prefab";

            Renderer[] kChildrenObjects = kModelAsset.transform.GetComponentsInChildren<Renderer>(true);
            int nLodLevel = 0;
            foreach (Renderer children in kChildrenObjects)
            {
                if (children.name.Contains("_LOD"))
                {
                    string strLevel = children.name.Substring((children.name.Length - 1), 1);
                    nLodLevel = Math.Max(nLodLevel, int.Parse(strLevel));
                }
            }

            if (kChildrenObjects.Length == nLodLevel)
            {
                LOD[] kLods = new LOD[nLodLevel];
                GameObject kLodFbxObject = AssetDatabase.LoadAssetAtPath<GameObject>(strPath);
                GameObject kLodPrefabTemp = PrefabUtility.InstantiatePrefab(kLodFbxObject) as GameObject;
                kLodPrefabTemp.transform.SetParent(kLodObject.transform, false);
                Renderer[] kRenderers = kLodPrefabTemp.transform.GetComponentsInChildren<Renderer>(true);

                for (int i = 0; i < nLodLevel; ++i) // Does not process LOD0
                {
                    string LODLevel = "_LOD" + (i + 1);
                    for (int j = 0; j < kRenderers.Length; j++)
                    {
                        if (kRenderers[j].name.Contains(LODLevel))
                        {
                            Renderer[] rendererLOD = new Renderer[1];
                            rendererLOD[0] = kRenderers[j];
                            kLods[i] = new LOD(1.0F / (i + 2), rendererLOD);
                        }

                        if (i == nLodLevel - 1)
                        {
                            kLods[i].screenRelativeTransitionHeight = (0.02f);
                        }
                    }
                }

                kLodGroup.SetLODs(kLods);
                kLodGroup.RecalculateBounds();
            }
            else
            {
                nLodLevel++;
                LOD[] lods = new LOD[nLodLevel];
                GameObject lodGo = AssetDatabase.LoadAssetAtPath<GameObject>(strPath);
                GameObject lodPrefabTemp = PrefabUtility.InstantiatePrefab(lodGo) as GameObject;
                lodPrefabTemp.transform.SetParent(kLodObject.transform, false);
                Renderer[] renderers = lodPrefabTemp.transform.GetComponentsInChildren<Renderer>(true);
                lodPrefabTemp.GetComponent<Animator>().runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(Path.GetDirectoryName(strPath) + "/" + kModelFileName + "_animator.controller");
                List<Renderer> renderersListLOD0 = new List<Renderer>();
                for (int i = 0; i < renderers.Length; i++) // Process LOD0
                {
                    if (!renderers[i].name.Contains("_LOD"))
                    {
                        renderersListLOD0.Add(renderers[i]);
                    }
                }
                Renderer[] renderersLOD0 = renderersListLOD0.ToArray();
                lods[0] = new LOD((1.0F / (2)), renderersLOD0);
                for (int i = 1; i < nLodLevel; i++)
                {
                    string LODLevel = "_LOD" + i;
                    for (int j = 0; j < renderers.Length; j++)
                    {
                        if (renderers[j].name.Contains(LODLevel))
                        {
                            Renderer[] rendererLOD = new Renderer[1];
                            rendererLOD[0] = renderers[j];
                            lods[i] = new LOD(1.0F / (i + 2), rendererLOD);
                        }
                        if (i == nLodLevel - 1)
                        {
                            lods[i].screenRelativeTransitionHeight = (0.02f);
                        }
                    }
                }
                kLodGroup.SetLODs(lods);
                kLodGroup.RecalculateBounds();
            }

            PrefabUtility.SaveAsPrefabAsset(kLodObject, strDestinationPath);
            UnityEngine.Object.DestroyImmediate(kLodObject);
        }

        /*
         * Check for the existence of the file folder and create a new one if it doesn't exist.
         * [in] Directory Path
         */
        private static void EnsureDirectoryExists(string directory)
        {
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        /*
         * Read Texture
         * [in] filePath: Texture file path.
         */
        public static Texture2D LoadTexture(string filePath)
        {
            Texture2D tex = null;
            if (File.Exists(filePath))
            {
                tex = AssetDatabase.LoadAssetAtPath(filePath, typeof(Texture2D)) as Texture2D;
            }
            return tex;
        }

        /*
         * Put the original file path under the texture folder.
         * [in] strPath: file path
         */
        private static string ChangeImageTexturePath(string strPath)
        {
            const string strTextureFolder = "textures/";
            int nSearchStartIndex = strPath.IndexOf(strTextureFolder) + strTextureFolder.Length;
            int nObjectNameIndex = strPath.IndexOf("/", nSearchStartIndex);

            string strFileName = Path.GetFileName(strPath);
            return strPath.Remove(nObjectNameIndex) + "/" + strFileName;
        }

        /*
         * Move all the images to the top level folder.
         * [in] kImportedPath: file path
         */
        private static void MoveImageToTopFolder(string[] kImportedPath)
        {
            foreach (string strSourcePath in kImportedPath)
            {
                bool bIsFromRLFolder = strSourcePath.Contains(strRootRLFolder);
                bool bIsImagePath = IsImagePath(strSourcePath);
                bool bEmbedTexture = strSourcePath.ToLower().Contains(".fbm");
                if (bIsFromRLFolder && bIsImagePath && !bEmbedTexture)
                {
                    string strTargetPath = ChangeImageTexturePath(strSourcePath);
                    if (!strSourcePath.Equals(strTargetPath))
                    {
                        if (File.Exists(strTargetPath))
                        {
                            AssetDatabase.DeleteAsset(strTargetPath);
                        }
                        AssetDatabase.MoveAsset(strSourcePath, strTargetPath);
                    }
                }
            }
        }

        /*
         * Check if the file is an image.
         * [in] strPath: file path
         */
        private static bool IsImagePath(string strPath)
        {
            bool bImagePath = false;
            string[] kImageSubName = { ".png", ".jpg", "tga", "bmp", "tif" };

            foreach (string strSubName in kImageSubName)
            {
                if (strPath.ToLower().Contains("textures") && strPath.ToLower().Contains(strSubName))
                {
                    bImagePath = true;
                    break;
                }
            }
            return bImagePath;
        }

        /*
         * Once the images have been moved, remove all of the empty file folders.
         * [in] kImportedPath: file path
         */
        private static void RemoveEmptyTextureFolder(string[] kImportedPath)
        {
            foreach (string strSourcePath in kImportedPath)
            {
                bool bIsFromRLFolder = strSourcePath.Contains(strRootRLFolder);
                bool bIsImagePath = IsImagePath(strSourcePath);
                bool bEmbedTexture = strSourcePath.ToLower().Contains(".fbm");
                if (bIsFromRLFolder && bIsImagePath && !bEmbedTexture)
                {
                    string strTargetPath = ChangeImageTexturePath(strSourcePath);
                    var nIndex = strTargetPath.LastIndexOf("/");
                    var strSourceFolder = strSourcePath.Substring(0, nIndex);
                    var kDirs = Directory.GetDirectories(strSourceFolder);
                    foreach (string strDir in kDirs)
                    {
                        AssetDatabase.DeleteAsset(strDir);
                    }
                }
            }
        }

        /*
         * Read Json file
         * [in] strPath: file path
         */
        private static JsonData ReadJsonFile(string strPath)
        {
            string strJsonString = File.ReadAllText(strPath);
            return JsonMapper.ToObject<JsonData>(strJsonString);
        }

        /*
         * Find the strKeyWord key in the Json data.
         * [in] kSourceData: Json data
         * [in] strKeyWord: keyword
         */
        private static JsonData SearchJsonKey(JsonData kSourceData, string strKeyWord)
        {
            // Export the default default value even When the default type is changed to something that Json doesn't handle.
            JsonData kResult = (long)0.0f;
            if (!kSourceData.IsObject)
            {
                return kResult;
            }

            foreach (string kJsonKey in kSourceData.Keys)
            {
                JsonData kValue = kSourceData[kJsonKey];

                if (kJsonKey == strKeyWord)
                {
                    return kValue;
                }

                JsonData kTemp = SearchJsonKey(kValue, strKeyWord);
                if (kTemp.IsObject || kTemp.IsString || kTemp.IsDouble)
                {
                    kResult = kTemp;
                    break;
                }
            }
            return kResult;
        }

        /*
         * Only look for the key under kSourceData.
         * [in] kSourceData: Json Data
         * [in] strKeyWord: keyword
         */
        private static JsonData SearchJsonChildKey(JsonData kSourceData, string strKeyWord)
        {
            // Export the default default value even When the default type is changed to something that Json doesn't handle.
            JsonData kResult = (long)0.0f;
            if (!kSourceData.IsObject)
            {
                return kResult;
            }

            foreach (string kJsonKey in kSourceData.Keys)
            {
                JsonData kValue = kSourceData[kJsonKey];

                if (kJsonKey == strKeyWord)
                {
                    return kValue;
                }
            }
            return kResult;
        }

        /*
         * Set the Detail map according to the character's generation.
         * [in] kMaterial: material
         * [in] eBaseType: character generation
         * [in] bMultiBase: Even though Game Base has 3 types, EBaseGeneration is only set to one type of Game Base, therefore bMultiBase is used to detect for multi-UV Game Bases.
         * A check can be done with Uid instead of material name, it just hasn't been implemented. 
         */
        private static void SetMaterialDetailMap(Material kMaterial, EBaseGeneration eBaseType, bool bMultiBase)
        {
            string strDetailMapPath = CC_RESOURCE_SKIN_PATH + CC_DETAIL_MAP_NAME;
            if (File.Exists(strDetailMapPath))
            {
                kMaterial.SetTexture("_DetailMap", LoadTexture(strDetailMapPath));
                kMaterial.SetFloat("_DetailAlbedoScale", 0.0001f);
                kMaterial.SetFloat("_DetailNormalScale", 0.548f);
                kMaterial.SetFloat("_DetailSmoothnessScale", 0.157f);

                if (eBaseType == EBaseGeneration.GameBase && !bMultiBase)
                {
                    kMaterial.SetTextureScale("_DetailMap", new Vector2(240, 120f));
                }
                else
                {
                    if (kMaterial.name.ToLower().Contains("head"))
                    {
                        kMaterial.SetTextureScale("_DetailMap", new Vector2(60, 30f));
                    }
                    else
                    {
                        kMaterial.SetTextureScale("_DetailMap", new Vector2(120, 60));
                    }
                }
            }

            string strBaseFolder = "";
            if (eBaseType == EBaseGeneration.G1)
            {
                strBaseFolder = "G1/";
            }
            else if (eBaseType == EBaseGeneration.G3)
            {
                strBaseFolder = "G3/";
            }
            else if (eBaseType == EBaseGeneration.G3Plus)
            {
                strBaseFolder = "G3Plus/";
            }
            else if (eBaseType == EBaseGeneration.GameBase)
            {
                strBaseFolder = bMultiBase ? "GameBase/Multi/" : "GameBase/Single/";
            }

            string strBaseResourceFolder = CC_RESOURCE_SKIN_PATH + strBaseFolder;
            Action<string, string> SetDetailMap = (string strThicknessImage, string strMaskImage) =>
            {
                string strThicknessMapPath = strBaseResourceFolder + strThicknessImage;
                if (File.Exists(strThicknessMapPath))
                {
                    kMaterial.SetTexture("_ThicknessMap", LoadTexture(strThicknessMapPath));
                }

                string strSssMaskMapPath = strBaseResourceFolder + strMaskImage;
                if (File.Exists(strSssMaskMapPath))
                {
                    kMaterial.SetTexture("_SubsurfaceMaskMap", LoadTexture(strSssMaskMapPath));
                }
            };

            if (kMaterial.name.ToLower().Contains("head"))
            {
                SetDetailMap(DetailMap.strHeadThicknessImage, DetailMap.strHeadSssMaskImage);
            }
            else if (kMaterial.name.ToLower().Contains("leg"))
            {
                SetDetailMap(DetailMap.strLegThicknessImage, DetailMap.strLegSssMaskImage);
            }
            else if (kMaterial.name.ToLower().Contains("body"))
            {
                SetDetailMap(DetailMap.strBodyThicknessImage, DetailMap.strBodySssMaskImage);
            }
            else if (kMaterial.name.ToLower().Contains("arm"))
            {
                SetDetailMap(DetailMap.strArmThicknessImage, DetailMap.strArmSssMaskImage);
            }
        }

#if USING_HDRP
        /*
         * Adjust HDRP material according to material name.
         * Currently processes Smoothness only, however CreateMaterials settings can be partially moved here.
         * [in] kMaterial: Material
         */
        private static void SetHDRPMaterialParameter(Material kMaterial)
        {
            var kMaterialName = kMaterial.name.ToLower();
            bool bContainHair = kMaterialName.Contains(MaterialKeyWord.Hair);
            bool bContainEyelash = kMaterialName.Contains(MaterialKeyWord.Eyelash);
            bool bContainTransparency = kMaterialName.Contains(MaterialKeyWord.Transparency);

            bool bContainScalp = kMaterialName.Contains(MaterialKeyWord.Scalp);
            bool bContainSkullcap = kMaterialName.Contains(MaterialKeyWord.Skullcap);

            bool bContainGaSkinBody = kMaterialName.Contains(MaterialKeyWord.GaSkinBody);

            bool bContainOcclusion = kMaterialName.Contains(MaterialKeyWord.Occlusion);
            bool bContainTearline = kMaterialName.Contains(MaterialKeyWord.Tearline);
            bool bContainCornea = kMaterialName.Contains(MaterialKeyWord.Cornea);
            bool bContainEyemoisture = kMaterialName.Contains(MaterialKeyWord.Eyemoisture);

            bool bContainEye = kMaterialName.Contains(MaterialKeyWord.Eye) && !bContainEyemoisture && !bContainEyelash;

            bool bContainSkin = kMaterialName.Contains(MaterialKeyWord.Skin) && !bContainGaSkinBody;

            // Defualt
            kMaterial.SetFloat("_Smoothness", 0.5f);

            // Special
            if (bContainHair || bContainEyelash || bContainTransparency)
            {
            }
            else if (bContainScalp || bContainSkullcap)
            {
            }
            else if (bContainGaSkinBody)
            {
                kMaterial.SetFloat("_Smoothness", 0.5f);
                kMaterial.SetFloat("_SmoothnessRemapMin", 0.2f);
                kMaterial.SetFloat("_SmoothnessRemapMax", 0.8f);
            }
            else if (bContainEyemoisture || bContainCornea || bContainOcclusion || bContainTearline)
            {
                kMaterial.SetFloat("_Smoothness", 0.8f);
                kMaterial.SetFloat("_SmoothnessRemapMin", 0.82f);
                kMaterial.SetFloat("_SmoothnessRemapMax", 0.88f);
            }
            else if (bContainEye)
            {
                kMaterial.SetFloat("_Smoothness", 0.8f);
                kMaterial.SetFloat("_SmoothnessRemapMin", 0.82f);
                kMaterial.SetFloat("_SmoothnessRemapMax", 0.88f);
            }
            else if (bContainSkin)
            {
                kMaterial.SetFloat("_Smoothness", 0.5f);
                kMaterial.SetFloat("_SmoothnessRemapMin", 0.2f);
                kMaterial.SetFloat("_SmoothnessRemapMax", 0.8f);
            }
        }
#endif

#if USING_URP
        /*
         * Adjust LWRP material according to material name. 
         * Currently processes Smoothness only, however CreateMaterials settings can be partially moved here.
         * [in] kMaterial: Material
         */
        private static void SetLWRPMaterialParameter(Material kMaterial)
        {
            var kMaterialName = kMaterial.name.ToLower();
            bool bContainHair = kMaterialName.Contains(MaterialKeyWord.Hair);
            bool bContainEyelash = kMaterialName.Contains(MaterialKeyWord.Eyelash);
            bool bContainTransparency = kMaterialName.Contains(MaterialKeyWord.Transparency);

            bool bContainScalp = kMaterialName.Contains(MaterialKeyWord.Scalp);
            bool bContainSkullcap = kMaterialName.Contains(MaterialKeyWord.Skullcap);

            bool bContainGaSkinBody = kMaterialName.Contains(MaterialKeyWord.GaSkinBody);

            bool bContainOcclusion = kMaterialName.Contains(MaterialKeyWord.Occlusion);
            bool bContainTearline = kMaterialName.Contains(MaterialKeyWord.Tearline);
            bool bContainCornea = kMaterialName.Contains(MaterialKeyWord.Cornea);
            bool bContainEyemoisture = kMaterialName.Contains(MaterialKeyWord.Eyemoisture);

            bool bContainEye = kMaterialName.Contains(MaterialKeyWord.Eye) && !bContainEyemoisture && !bContainEyelash;

            bool bContainSkin = kMaterialName.Contains(MaterialKeyWord.Skin) && !bContainGaSkinBody;

            // Defualt
            kMaterial.SetFloat("_Smoothness", 0.88f);

            // Special
            if (bContainHair || bContainEyelash || bContainTransparency)
            {
                kMaterial.SetFloat("_Smoothness", 0.5f);
            }
            else if (bContainScalp || bContainSkullcap)
            {
            }
            else if (bContainGaSkinBody)
            {
                kMaterial.SetFloat("_Smoothness", 0.5f);
            }
            else if (bContainEyemoisture || bContainCornea || bContainOcclusion || bContainTearline)
            {
                kMaterial.SetFloat("_Smoothness", 0.5f);
            }
            // The eyes need to be processed last, otherwise there will be naming conflict.
            else if (bContainEye)
            {
            }
            else if (bContainSkin)
            {
                kMaterial.SetFloat("_Smoothness", 0.7f);
            }
        }

        /*
         * LWRP needs to adjust the core material parameters, othewise the UI will not be in sync with the core data.
         * [in] kMaterial: Material
         */
        private static void SetLWRPMaterialKeywords(Material material)
        {
            bool isSpecularWorkFlow = material.GetFloat("_WorkflowMode") == 0f;
            bool hasGlossMap = false;
            if (isSpecularWorkFlow)
                hasGlossMap = material.GetTexture("_SpecGlossMap");
            else
                hasGlossMap = material.GetTexture("_MetallicGlossMap");

            CoreUtils.SetKeyword(material, "_SPECULAR_SETUP", isSpecularWorkFlow);
            CoreUtils.SetKeyword(material, "_METALLICSPECGLOSSMAP", hasGlossMap);
            CoreUtils.SetKeyword(material, "_SPECGLOSSMAP", hasGlossMap && isSpecularWorkFlow);
            CoreUtils.SetKeyword(material, "_METALLICGLOSSMAP", hasGlossMap && !isSpecularWorkFlow);
            CoreUtils.SetKeyword(material, "_NORMALMAP", material.GetTexture("_BumpMap"));
            CoreUtils.SetKeyword(material, "_SPECULARHIGHLIGHTS_OFF", material.GetFloat("_SpecularHighlights") == 0.0f);
            CoreUtils.SetKeyword(material, "_GLOSSYREFLECTIONS_OFF", material.GetFloat("_GlossyReflections") == 0.0f);
            CoreUtils.SetKeyword(material, "_OCCLUSIONMAP", material.GetTexture("_OcclusionMap"));
            MaterialEditor.FixupEmissiveFlag(material);
            bool shouldEmissionBeEnabled = (material.globalIlluminationFlags & MaterialGlobalIlluminationFlags.EmissiveIsBlack) == 0;
            CoreUtils.SetKeyword(material, "_EMISSION", shouldEmissionBeEnabled);
        }
#endif

        #region UnusedCode definition
        // Used to retrieve the humanoid reference avatar.
        public void CopyHumanoidPre(ModelImporter importer)
        {
            importer.animationType = ModelImporterAnimationType.Human;
            importer.sourceAvatar = AssetDatabase.LoadAssetAtPath("Assets/tPOSE.Fbx", typeof(UnityEngine.Avatar)) as Avatar;
        }
        public void CopyHumanoidPost(GameObject go, ModelImporter importer)
        {
            if (!go.GetComponent<Animator>())
            {
                go.AddComponent<Animator>();
                go.GetComponent<Animator>().avatar = importer.sourceAvatar;
                go.GetComponent<Animator>().applyRootMotion = true;
            }
        }
        #endregion
    }
}
