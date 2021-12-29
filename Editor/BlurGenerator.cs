using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.IO;

public class BlurGenerator : EditorWindow
{
    //ブラー対象のテクスチャ
    private Texture2D _source;
    //生成テクスチャの名前周り
    private bool _autoName = true;
    private string _textureName = "Target_Blur";
    private bool _autoPath = true;
    private string _pathName = "Assets/BlurGenerator/Editor/";
    //ブラーの度合い
    private float _blurRate = 0;
    //ブラーテクスチャの上にベーステクスチャを上書きするか
    private bool _stackBaseTex = false;
    private float _stackBaseTexRate = 1;

    //質問をスキップ
    private bool _skipQuestion = false;

    private const TextureFormat _format = TextureFormat.RGBA32;

    [MenuItem("Window/GaussBlurGenerator")]
    private static void Create()
    {
        // ウィンドウの生成
        GetWindow<BlurGenerator>("GaussBlurGenerator");
    }

    private void OnGUI()
    {
        using (new GUILayout.HorizontalScope())
        {
            _source = (Texture2D) EditorGUILayout.ObjectField("Texture", _source, typeof(Texture2D), false);
        }

        using (new GUILayout.HorizontalScope())
        {
            _autoName = EditorGUILayout.Toggle("AutoName", _autoName);
            if (_source != null && _autoName == true)
            {
                //任意の名前を入力する場合、デフォルトの名前が表示
                _textureName = _source.name + "_blur";
            }
        }

        if (!_autoName)
        {
            using (new GUILayout.HorizontalScope())
            {
                EditorGUILayout.BeginHorizontal();
                _textureName = EditorGUILayout.TextField("TextureName", _textureName);
                EditorGUILayout.LabelField(".png", GUILayout.Width(30));
                EditorGUILayout.EndHorizontal();
            }
            
        }

        using (new GUILayout.HorizontalScope())
        {
            _autoPath = EditorGUILayout.Toggle("AutoPath", _autoPath);
            if (_source != null && _autoPath == true)
            {
                //任意の名前を入力する場合、デフォルトの名前が表示
                System.String assetPath = UnityEditor.AssetDatabase.GetAssetPath(_source);
                int namePos = assetPath.LastIndexOf("/");

                //名前部分を削除する必要あり
                _pathName = assetPath.Substring(0, namePos) + "/";
            }
        }

        if (!_autoPath)
        {
            using (new GUILayout.HorizontalScope())
            {
                _pathName = EditorGUILayout.TextField("Path", _pathName);
            }
        }

        using (new GUILayout.HorizontalScope())
        {
            _blurRate = EditorGUILayout.Slider("BlurRate", _blurRate, 0, 1);
        }

        using (new GUILayout.HorizontalScope())
        {
            _stackBaseTex = EditorGUILayout.Toggle("StackBaseTex", _stackBaseTex);
        }

        if (_stackBaseTex)
        {
            using (new GUILayout.HorizontalScope())
            {
                _stackBaseTexRate = EditorGUILayout.Slider("BlurRate", _stackBaseTexRate, 0, 1);
            }
        }

        using (new GUILayout.HorizontalScope())
        {
            _skipQuestion = EditorGUILayout.Toggle("SkipPermissionWindow", _skipQuestion);
        }

        using (new GUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Generate"))
            {
                TextureGenerator();
            }
        }
    }

    private void TextureGenerator()
    {
        //ターゲットのテクスチャがアタッチされてなかった場合、ダイアログを出して終了
        if (_source == null)
        {
            EditorUtility.DisplayDialog("GaussBlurGenerator", "Error : Texture is not Exist", "OK");
            return;
        }
        if (_source.height >= 1024)
        {
            bool isOK = EditorUtility.DisplayDialog("GaussBlurGenerator", "元画像が1024pixel以上です。実行しますか？\n(処理に時間がかかります。)", "OK", "Cancel");
            if (!isOK)
            {
                EditorUtility.DisplayDialog("GaussBlurGenerator", "Error : Canceled", "OK");
                return;
            }
        }

        //テクスチャのEnableReadの是非(許可しないならリターン)
        // インポート元のパスを取得
        System.String assetPath = UnityEditor.AssetDatabase.GetAssetPath(_source);
        // パスからインポート設定にアクセス
        UnityEditor.TextureImporter importer = UnityEditor.AssetImporter.GetAtPath(assetPath) as UnityEditor.TextureImporter;
        //手動のアプライし忘れてるとエラーが起こる可能性あり
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

        bool readableFlag = false;
        if(importer.isReadable == false)
        {
            bool isOK = true;
            if(!_skipQuestion) isOK = EditorUtility.DisplayDialog("GaussBlurGenerator", "元画像をスクリプトからアクセスすることをを許可しますか？\n(処理後、元に戻ります。)", "OK", "Cancel");
            if (!isOK)
            {
                FinishProcess(true, assetPath, importer, false, false, 0);
                return;
            }
            readableFlag = true;
            importer.isReadable = true;
        }
        //GetPixelするためには、クランチ圧縮を無効にしないといけない
        bool compressFlag = false;
        int compressRate = 0;
        if (importer.crunchedCompression)
        {
            bool isOK = true;
            if (!_skipQuestion) isOK = EditorUtility.DisplayDialog("GaussBlurGenerator", "元画像のクランチ圧縮をオフにすることをを許可しますか？\n(処理後、元に戻ります。)", "OK", "Cancel");
            if (!isOK)
            {
                FinishProcess(true, assetPath, importer, readableFlag, false, 0);
                return;
            }
            compressFlag = true;
            compressRate = importer.compressionQuality;
            importer.crunchedCompression = false;
        }
        if (readableFlag || compressFlag) AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

        //テクスチャの生成(ガウスフィルタ)
        var texture = GaussFilter(_source, _blurRate);
        //中断した場合、終了
        if (texture == null)
        {
            FinishProcess(true, assetPath, importer, readableFlag, compressFlag, compressRate);
            return;
        }
        texture.Apply();

        //ブラーテクスチャの上にベーステクスチャを上書き
        if (_stackBaseTex) texture = StackLayerBlurTexture(texture, _source, _stackBaseTexRate);
        texture.Apply();

        //pngにエンコード
        var bytes = texture.EncodeToPNG();

        //名前がかぶる場合、新しいパスを生成
        var uniquePath = AssetDatabase.GenerateUniqueAssetPath(_pathName + _textureName + ".png");
        //保存(パスが存在しない場合、エラー)
        try
        {
            File.WriteAllBytes(uniquePath, bytes);
        }
        catch(System.ArgumentException)
        {
            EditorUtility.DisplayDialog("GaussBlurGenerator", "Error : Path not Found\n元画像と同じディレクトリに保存します。", "OK");
            //任意の名前を入力する場合、デフォルトの名前が表示
            int namePos = assetPath.LastIndexOf("/");
            //名前部分を削除する必要あり
            string _pathName_tmp = assetPath.Substring(0, namePos) + "/";
            uniquePath = AssetDatabase.GenerateUniqueAssetPath(_pathName_tmp + _textureName + ".png");
            File.WriteAllBytes(uniquePath, bytes);
        }

        //削除処理
        AssetDatabase.Refresh();

        //テクスチャの設定
        UnityEditor.TextureImporter blur_importer = UnityEditor.AssetImporter.GetAtPath(uniquePath) as UnityEditor.TextureImporter;
        blur_importer.alphaIsTransparency = true;
        blur_importer.maxTextureSize = importer.maxTextureSize;
        if (importer.crunchedCompression == true)
        {
            blur_importer.crunchedCompression = true;
            blur_importer.compressionQuality = importer.compressionQuality;
        }
        AssetDatabase.ImportAsset(uniquePath, ImportAssetOptions.ForceUpdate);

        FinishProcess(false, assetPath, importer, readableFlag, compressFlag, compressRate);
        return;
    }

    //終了フェーズ
    private static void FinishProcess(bool isCancel, string assetPath, UnityEditor.TextureImporter importer, bool readableFlag, bool compressFlag, int compressRate)
    {
        //ベーステクスチャの設定を戻す
        if (readableFlag) importer.isReadable = false;
        if (compressFlag)
        {
            importer.crunchedCompression = true;
            importer.compressionQuality = compressRate;
        }
        if (readableFlag || compressFlag) AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        AssetDatabase.Refresh();

        //処理完了後、ダイアログを出して終了
        if (isCancel) EditorUtility.DisplayDialog("GaussBlurGenerator", "Error : Progress Break", "OK");
        else EditorUtility.DisplayDialog("GaussBlurGenerator", "Complete : Texture Generated", "OK");
        return;
    }

    //ガウシアンフィルタ本体
    private static Texture2D GaussFilter(Texture2D target, float blurRate)
    {
        int h = target.height;
        int w = target.width;
        
        Color[,] targetColor = new Color[w, h];
        var texture = new Texture2D(w, h, _format, false);

        //テクスチャをコピー
        //texture.SetPixels32(target.GetPixels32());

        //3*3のカーネルの出力
        if (EditorUtility.DisplayCancelableProgressBar(
                    "GaussBlurGenerator",
                    "GaussKernel Progressing... " + 0.0f.ToString() + "%",
                    0))
        {
            EditorUtility.ClearProgressBar();
            return null;
        }
        float[,] kernel = KernelFunction(blurRate, h);
        int kernelSize = kernel.GetLength(0);
        
        //ガウシアンフィルタ本体
        for (int i = 0; i < w; i++)
        {
            for(int j = 1; j < h; j++)
            {
                targetColor[i, j] = target.GetPixel(i, j);
            }

        }

        for (int i = 0; i < target.height; i++)
        {
            for(int j = 0; j < target.width; j++)
            {
                Color sum = new Color(0, 0, 0, 0);
                for (int k = -kernelSize / 2; k <= kernelSize / 2; k++)
                {
                    for (int n = -kernelSize / 2; n <= kernelSize / 2; n++)
                    {
                        if (j + n >= 0 && j + n < w && i + k >= 0 && i + k < h)
                        {
                            sum += targetColor[j + n, i + k] * kernel[n + kernelSize / 2, k + kernelSize / 2];
                        }
                    }
                }
                texture.SetPixel(j, i, sum);
                // プログレスバー描画
            }
            if (EditorUtility.DisplayCancelableProgressBar(
                    "GaussBlurGenerator",
                    "GaussFilter Progressing... " + ((float)i / (float)w*100).ToString() + "%",
                    (float)i / (float)w))
            {
                EditorUtility.ClearProgressBar();
                return null;
            }
        }

        EditorUtility.ClearProgressBar();
        texture.Apply();
        return texture;
    }

    //カーネルの生成
    private static float[,] KernelFunction(float blurRate, int targetPixel)
    {
        //画像サイズの10%をカーネルサイズにする(入力を0-1にするため2で割ってる)
        int kernelSize = (int)(targetPixel * blurRate / 2.0f);
        if(kernelSize < 3) kernelSize = 3;
        else if (kernelSize % 2 == 0) kernelSize += 1;

        var combs= new List<float>();
        combs.Add(1);

        for(int i = 1; i < kernelSize; i++)
        {
            float ratio = (kernelSize - i) / (float)i;
            combs.Add(combs[combs.Count - 1] * ratio);
        }
        float[] array = combs.ToArray();

        float[,] matrixA = new float[array.Length, 1];
        float[,] matrixB = new float[1, array.Length];
        for(int i = 0;i < array.Length; i++)
        {
            matrixA[i, 0] = array[i] / Mathf.Pow(2, kernelSize - 1);
            matrixB[0, i] = array[i] / Mathf.Pow(2, kernelSize - 1); 
        }

        float[,] kernel = CalcMatrixMul(matrixA, matrixB);
        return kernel;
    }

    //行列の掛け算
    private static float[,] CalcMatrixMul(float[,] matrixA,float[,] matrixB)
    {
        int row_a = matrixA.GetLength(0);
        int col_a = matrixA.GetLength(1);
        int col_b = matrixB.GetLength(1);

        float[,] ResultMatrix = new float[row_a, col_b];
        for (int i = 0; i < row_a; i++)
        {
            for(int j= 0; j < col_b; j++)
            {
                for(int k= 0; k < col_a; k++)
                {
                    ResultMatrix[i, j] += matrixA[i, k] * matrixB[k, j];
                }
            }
        }
        return ResultMatrix;
    }

    //元画像との重ね合わせ
    private static Texture2D StackLayerBlurTexture(Texture2D blurTex, Texture2D baseTex, float addRate)
    {
        int w = blurTex.width;
        int h = blurTex.height;

        Color[,] blurTexColor = new Color[w, h];
        Color[,] baseTexColor = new Color[w, h];

        for (int i = 0; i < w; i++)
        {
            for (int j = 1; j < h; j++)
            {
                blurTexColor[i, j] = blurTex.GetPixel(i, j);
                baseTexColor[i, j] = baseTex.GetPixel(i, j);
            }

        }

        var texture = new Texture2D(w, h, _format, false);
        for(int i = 0; i < w; i++)
        {
            for(int j = 0; j < h; j++)
            {
                texture.SetPixel(i, j, baseTexColor[i, j] * baseTexColor[i, j].a + blurTexColor[i, j] * (1 - baseTexColor[i, j].a) * addRate);
            }
        }
        texture.Apply();

        return texture;
    }
}
