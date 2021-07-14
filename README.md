# Auto Setup for Unity Script Readme file   

Copyright (c) 2021, Reallusion, Inc. All rights reserved.   

<a href="https://www.reallusion.com/character-creator/" target="_blank">Character Creator</a>, <a href="https://www.reallusion.com/iclone/default.html" target="_blank">iClone</a> and <a href="https://actorcore.reallusion.com/" target="_blank">ActorCore</a> provide quality characters, animations, and assets to game developers.  
To save time in the complicated and routine works of import, Reallusion offers tools to automate the process of shader assignment and characterization for Unity.      

![workflow](https://www.reallusion.com/character-creator/includes/images/unity-auto-setup/unity-auto-setup.png "Logo")

-------------------------------------------------
<a href="https://manual.reallusion.com/CC_and_IC_Auto_Setup_Plugin/ENU/CC_and_iC_Auto_Setup/1.0/03_for_Unity/Unity_Importing_Character_FBX_File.htm" target="_blank">How to install and Use</a>
-------------------------------------------------
1. Move the extracted files into Unity’s Asset folder (manually create this folder in the Assets directory, if it’s missing).
2. Export Character using Unity preset from Reallusion Character Creator.
3. Open the CC Assets folder in the Unity Project.
4. Drag and drop the folder in which your exported FBX files are.

----------------------
**Main Program Workflow**
----------------------
**Format Pre-processing**

Every imported object will undergo pre-processing, mostly to set the starting parameter values with the following commands:

    1. OnPreprocessModel > CreateHumanoidPre
    2. OnPreprocessAsset > CheckAutoSetupVersion
    3. OnPreprocessTexture
    4. OnPreprocessAnimation > SetAnimation

**Format Post-processing**

Every imported object will undergo post-processing, the main settings are applied at this stage:

    1. OnPostprocessMaterial
    2. OnPostprocessModel > CreateHumanoidPost & SetAnimation
    3. OnPostprocessAllAssets: Main procedure (see notes).

Notes:  
Relocating textures: MoveImageToTopFolder & RemoveEmptyTextureFolder  
Deploying Animator: AutoCreateAnimator  
Deploying Materials: CreateMaterials  
Deploying Prefabs: CreateOneLODPrefabFromModel or CreatePrefabFromModel  
Pay special attention to Auto Setup importing the FBX twice. This is because the first import for the previous version tends to create erroneous data within the materials or create unforeseen problems during operation. The new version runs through the import process twice to mitigate the occurrence of these mistakes, however, you will still need to make sure the problems are resolved.

**Creating Materials**

    1. Extract material.
    2. Set the Diffuse Profile in HDRP.
    3. Find the corresponding JSON file.
    4. Set the texture and material properties according to the shader name.

----------------------
**External Library**
----------------------
- <a href="https://github.com/LitJSON/litjson" target="_blank">LitJSON</a>        
- <a href="https://gitlab.com/-/snippets/2026367" target="_blank">A solution to detecting Unity's active RenderPipeline</a>

----------------------
**Compatible Version**
----------------------
Unity 2021.1, 2020.3(LTS) and 2019.4(LTS) with HDRP and URP.
