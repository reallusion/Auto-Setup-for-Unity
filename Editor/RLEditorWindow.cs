using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

using RLPlugin;
namespace RLPlugin
{
    public class RLEditorWindow : EditorWindow
    {
        static bool bIsAuto = true;
        static string strAutoKey = "";
        static string strKeyWord = "isAuto";
        // Use this for initialization
        [MenuItem( "Tools/Character Creator && iClone Auto Setup", false, 0 )]
        static void Init()
        {
            strAutoKey = Application.dataPath + strKeyWord;
            RLEditorWindow settingWindow = ( RLEditorWindow )EditorWindow.GetWindow( typeof( RLEditorWindow ), false, "Settings" );
            // settingWindow.autoRepaintOnSceneChange = true;
            if ( EditorPrefs.HasKey( strAutoKey ) )
            {
                bIsAuto = EditorPrefs.GetBool( strAutoKey );
            }
            else
            {
                EditorPrefs.SetBool( strAutoKey, true );
                bIsAuto = true;
            }
            RLEditor.setAuto( bIsAuto );
            settingWindow.Show();
        }

        void Start()
        {
            bIsAuto = EditorPrefs.GetBool( strAutoKey );
            RLEditor.setAuto( bIsAuto );
        }

        void OnGUI()
        {
            bIsAuto = EditorPrefs.GetBool( strAutoKey );
            bIsAuto = EditorGUILayout.ToggleLeft( "Auto-Processing", bIsAuto );
            EditorPrefs.SetBool( strAutoKey, bIsAuto );
            RLEditor.setAuto( bIsAuto );
        }

        void OnDestroy()
        {
            EditorPrefs.SetBool( strAutoKey, bIsAuto );
        }
    }
}
