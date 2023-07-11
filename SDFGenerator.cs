using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public class SDFGenerator : EditorWindow
{
    [MenuItem("Tools/SDF Generator")]
    public static void ShowWindow()
    {
        var window = GetWindow<SDFGenerator>();
        window.minSize = new Vector2(200, 200);
        window.Show();
    }

    enum SwichModel
    {
        Single = 0,
        Multi = 1
    }

    enum ChannelType
    {
        RGB = 0,
        A = 3
    }

    private Texture2D tex;
    private List<Texture2D> texs = new List<Texture2D>();
    private string dataPath = "";
    private SwichModel sm = SwichModel.Single;
    private ChannelType channel = ChannelType.RGB;
    private ComputeShader cs;
    private int spread = 16;
    private float smooth = 0.1f;
    private RenderTexture texArray;
    RenderTexture rt;
    private float slider = 0.5f;
    private Vector2 scrollPosition = Vector2.zero;

        public void OnGUI()
    {
        sm = (SwichModel)EditorGUILayout.EnumPopup("选择文件", sm);
        switch (sm)
        {
            case SwichModel.Single:
                channel = (ChannelType)EditorGUILayout.EnumPopup("生成通道", channel);
                tex = EditorGUILayout.ObjectField("图片", tex, typeof(Texture2D), false) as Texture2D;
                if (tex != null)
                {
                    dataPath = AssetDatabase.GetAssetPath(tex);
                }
                texs.Clear();
                break;

            case SwichModel.Multi:
                if (GUILayout.Button("选择目录"))
                {
                    dataPath = EditorUtility.OpenFolderPanel("", Application.dataPath, "");
                    LoadTextures(dataPath);
                }
                smooth = EditorGUILayout.Slider("Smooth", smooth, 0, 2);

                break;
        }

        cs = EditorGUILayout.ObjectField("Shader", cs, typeof(ComputeShader)) as ComputeShader;
        spread = EditorGUILayout.IntField("Spread", spread);
        
        if (GUILayout.Button("生成"))
        {
            GenerateSDF();
        }

        if (texs.Count > 0)
        {
            slider = EditorGUILayout.Slider("缩放", slider, 0, 1);
            scrollPosition = GUILayout.BeginScrollView(scrollPosition);
            for (int i = 0; i < texs.Count; i++)
            {
                GUILayout.BeginHorizontal();
                // 显示图片
                GUILayout.Box(texs[i], GUILayout.Width(texs[i].width * slider), GUILayout.Height(texs[i].height * slider));

                GUILayout.EndHorizontal();

            }
            GUILayout.EndScrollView();

            if (GUILayout.Button("clear"))
            {
                texs.Clear();
            }
        }
      
    }
        
         
    bool IsImageFile(string file)
    {
        if (Path.GetFileNameWithoutExtension(file).EndsWith("_SDF")) return false;
        string extension = Path.GetExtension(file).ToLower();
        return extension == ".png" || extension == ".jpg" || extension == ".jpeg" || extension == ".bmp";
    }

    private void LoadTextures(string path)
    {
        if (!Directory.Exists(path)) return;
        var files = Directory.GetFiles(path);
        if (files.Length < 1) return;
        texs.Clear();
        foreach (var file in files)
        {
            if (IsImageFile(file))
            {
                var data = File.ReadAllBytes(file);
                var img = new Texture2D(2, 2);
                img.LoadImage(data);
                texs.Add(img); 
            }
        }
    }
        
    private void GenerateSDF()
    {
        Vector2 size = Vector2.one;
        int kernel = 0;
        switch (sm)
        {
            case SwichModel.Single:
                size.x = tex.width;
                size.y = tex.height;
                rt = RenderTexture.GetTemporary(new RenderTextureDescriptor((int)size.x, (int)size.y, RenderTextureFormat.ARGB32));
                Graphics.Blit(tex, rt);
                
                kernel = cs.FindKernel("CSMain");
                cs.SetTexture(kernel, "_MainTex", rt);
                cs.SetVector("_MainTex_Size", new Vector4(size.x, size.y, 0, 0));

                RenderTexture ot = new RenderTexture((int)size.x, (int)size.y, 32, RenderTextureFormat.ARGB32);
                ot.enableRandomWrite = true;
                ot.Create();
                if (channel == ChannelType.A)
                {
                    cs.EnableKeyword("CHANNEL_A");
                }
                else
                {
                    cs.DisableKeyword("CHANNEL_A");
                }
                
                cs.SetInt("_HalfRange", spread / 2);
        
                cs.SetTexture(kernel, "_OutputTexture", ot);

                uint x, y, z;
                cs.GetKernelThreadGroupSizes(kernel, out x, out y, out z);
                cs.Dispatch(kernel, (int)(size.x / x), (int)(size.y / y), 1);
                SaveTex(ot, dataPath);
                
                if(rt != null)rt=null;
                if(ot != null)ot=null;
                
                break;

            case SwichModel.Multi:
                size.x = texs[0].width;
                size.y = texs[0].height;

                kernel = cs.FindKernel("CSMain");
                
                //texArray = new Texture2DArray((int)size.x, (int)size.y, texs.Count, TextureFormat.ARGB32, false, false);
                texArray = new RenderTexture((int) size.x, (int)size.y, 32, RenderTextureFormat.ARGB32);
                texArray.dimension = TextureDimension.Tex2DArray;
                texArray.volumeDepth = texs.Count;
                texArray.enableRandomWrite = true;
                texArray.Create();
                
                for (int i = 0; i <texs.Count; i++)
                {
                    var rt1 = RenderTexture.GetTemporary(new RenderTextureDescriptor((int)size.x, (int)size.y, RenderTextureFormat.ARGB32));

                    Graphics.Blit(texs[i], rt1);
                    cs.SetTexture(kernel, "_MainTex", rt1);
                    cs.SetVector("_MainTex_Size", new Vector4(size.x, size.y, 0, 0));

                    if (channel == ChannelType.A)
                    {
                        cs.EnableKeyword("CHANNEL_A");
                        //Shader.EnableKeyword("CHANNEL_A");
                    }
                    else
                    {
                        //Shader.DisableKeyword("CHANNEL_A");
                        cs.DisableKeyword("CHANNEL_A");
                    }
                 
                    RenderTexture ot1 = new RenderTexture((int)size.x, (int)size.y, 32, RenderTextureFormat.ARGB32);
                    ot1.enableRandomWrite = true;
                    ot1.Create();
                    
                    cs.SetTexture(kernel, "_OutputTexture", ot1);
                    cs.SetInt("_HalfRange", spread / 2);
        
                    uint x1, y1, z1;
                    cs.GetKernelThreadGroupSizes(kernel, out x1, out y1, out z1);
                    cs.Dispatch(kernel, (int)(size.x / x1), (int)(size.y / y1), 1);
                    
                   Graphics.CopyTexture(ot1, 0,0,texArray, i, 0);
                   if(ot1 != null)ot1 = null;
                   if(rt1 != null)rt1 = null;
                }
                
                RenderTexture ot2 = new RenderTexture((int)size.x, (int)size.y, 32, RenderTextureFormat.ARGB32);
                ot2.enableRandomWrite = true;
                ot2.Create();
                
                kernel = cs.FindKernel("MultiTexture");
                cs.SetInt("Count", texArray.volumeDepth);
                cs.SetFloat("Smooth", smooth);
                                
                cs.SetTexture(kernel, "TextureArray", texArray);
                cs.SetTexture(kernel, "_OutputTexture", ot2);

                uint x2, y2, z2;
                cs.GetKernelThreadGroupSizes(kernel, out x2, out y2, out z2);
                cs.Dispatch(kernel, (int)(size.x / x2), (int)(size.y / y2), 1);
                
                SaveTex(ot2, dataPath);
                
                if(ot2 != null)ot2 = null;
                break;
        }

    }

    public void SaveTex(RenderTexture rt, string savePath)
    {
        savePath = dataPath == null ? "Assets" : dataPath.Replace(Application.dataPath, "Assets");

        Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.ARGB32, false);
        
        var original = RenderTexture.active;
        RenderTexture.active = rt;
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = original;
        string save = "";
        switch (sm)
        {
            case SwichModel.Single:
                     
                var directory = Path.GetDirectoryName(savePath);
                var fileName = Path.GetFileNameWithoutExtension(savePath);

                if (!string.IsNullOrEmpty(directory))
                {
                    if (!Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }
                }
                else
                {
                    Debug.LogErrorFormat("savePath directory no exist {0}", savePath);
                    return;
                }

                save = directory + "\\" + fileName + "_SDF.png";
                break;
            case SwichModel.Multi:
                save = savePath + "\\" + "SDFCombin" + "_SDF.png";
                break;
        }
        
        using (FileStream fileStream = new FileStream(save, FileMode.Create))
        {
            fileStream.Write(tex.EncodeToPNG(), 0,tex.EncodeToPNG().Length);
        }
        
        AssetDatabase.Refresh();
    }
}
