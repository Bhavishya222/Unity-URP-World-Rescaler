#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class WorldRescalerWindow : EditorWindow
{
    private enum PivotMode
    {
        WorldOrigin,
        CustomPosition
    }

    [SerializeField] private float scaleFactor = 0.2f;
    [SerializeField] private PivotMode pivotMode = PivotMode.WorldOrigin;
    [SerializeField] private Vector3 customPivot = Vector3.zero;
    [SerializeField] private string terrainAssetFolder = "Assets/WorldRescalerGenerated";

    [SerializeField] private bool scaleTerrainData = true;
    [SerializeField] private bool scaleTerrainLayerTiling = true;
    [SerializeField] private bool scaleTerrainTreeSizes = true;
    [SerializeField] private bool scaleTerrainDetailSizes = true;
    [SerializeField] private bool scaleTerrainRenderDistances = true;
    [SerializeField] private bool scalePointAndSpotLightRanges = true;
    [SerializeField] private bool scalePointAndSpotLightIntensity = false;
    [SerializeField] private bool scaleCameraClipPlanes = true;

    private int previewRootCount;
    private int previewTerrainCount;
    private int previewLightCount;
    private int previewCameraCount;
    private readonly List<string> previewErrors = new List<string>();
    private bool hasPreview;

    [MenuItem("Tools/World Rescaler")]
    public static void Open()
    {
        WorldRescalerWindow window = GetWindow<WorldRescalerWindow>("World Rescaler");
        window.minSize = new Vector2(430f, 590f);
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("World Rescaler", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Scales the active scene around a pivot. Supports nested multi-tile Terrain groups, updates TerrainColliders, and clones TerrainData/TerrainLayers safely.",
            MessageType.Info);

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("World Scale", EditorStyles.boldLabel);
        scaleFactor = EditorGUILayout.FloatField(
            new GUIContent("Scale Factor", "0.05 shrinks a 20x oversized world back to 1x. 0.2 shrinks to one-fifth."),
            scaleFactor);
        scaleFactor = Mathf.Clamp(scaleFactor, 0.001f, 1000f);

        pivotMode = (PivotMode)EditorGUILayout.EnumPopup("Scale Around", pivotMode);
        if (pivotMode == PivotMode.CustomPosition)
            customPivot = EditorGUILayout.Vector3Field("Custom Pivot", customPivot);

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Terrain", EditorStyles.boldLabel);
        scaleTerrainData = EditorGUILayout.ToggleLeft("Clone and scale TerrainData & TerrainCollider dimensions", scaleTerrainData);
        using (new EditorGUI.DisabledScope(!scaleTerrainData))
        {
            EditorGUI.indentLevel++;
            scaleTerrainLayerTiling = EditorGUILayout.ToggleLeft("Clone and scale TerrainLayer texture tiling", scaleTerrainLayerTiling);
            scaleTerrainTreeSizes = EditorGUILayout.ToggleLeft("Scale Terrain tree instance sizes", scaleTerrainTreeSizes);
            scaleTerrainDetailSizes = EditorGUILayout.ToggleLeft("Scale Terrain grass/detail sizes", scaleTerrainDetailSizes);
            scaleTerrainRenderDistances = EditorGUILayout.ToggleLeft("Scale Terrain tree/detail render distances", scaleTerrainRenderDistances);
            EditorGUI.indentLevel--;
            terrainAssetFolder = EditorGUILayout.TextField("Generated asset folder", terrainAssetFolder);
        }

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Lighting & Cameras", EditorStyles.boldLabel);
        scalePointAndSpotLightRanges = EditorGUILayout.ToggleLeft("Scale Point/Spot Light ranges with the world", scalePointAndSpotLightRanges);
        scalePointAndSpotLightIntensity = EditorGUILayout.ToggleLeft("Scale Point/Spot Light intensity by factor² (optional)", scalePointAndSpotLightIntensity);
        scaleCameraClipPlanes = EditorGUILayout.ToggleLeft("Scale camera near/far clip distances", scaleCameraClipPlanes);

        EditorGUILayout.HelpBox(
            "Tip: If your lights were struggling to reach across the oversized scene, keep 'Scale ranges' enabled and leave 'Scale intensity' UNCHECKED so your lights keep their brightness in the smaller world.",
            MessageType.Info);

        EditorGUILayout.Space(10);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Scan Active Scene", GUILayout.Height(30)))
                ScanScene();

            using (new EditorGUI.DisabledScope(!hasPreview || previewErrors.Count > 0))
            {
                if (GUILayout.Button("Apply Rescale", GUILayout.Height(30)))
                    ApplyRescale();
            }
        }

        if (hasPreview)
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Active scene", GetActiveSceneLabel());
            EditorGUILayout.LabelField("Scene root hierarchies", previewRootCount.ToString());
            EditorGUILayout.LabelField("Terrain components", previewTerrainCount.ToString());
            EditorGUILayout.LabelField("Point/Spot lights", previewLightCount.ToString());
            EditorGUILayout.LabelField("Cameras", previewCameraCount.ToString());
            EditorGUILayout.LabelField("Example: 20-unit object becomes", (20f * scaleFactor).ToString("0.###") + " units");

            if (previewErrors.Count > 0)
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.HelpBox("The operation is blocked until these issues are fixed:", MessageType.Error);
                foreach (string error in previewErrors)
                    EditorGUILayout.LabelField("• " + error, EditorStyles.wordWrappedLabel);
            }
            else
            {
                EditorGUILayout.HelpBox("Ready to rescale! Make sure you save a backup of your scene first.", MessageType.Info);
            }
        }
    }

    private string GetActiveSceneLabel()
    {
        Scene scene = SceneManager.GetActiveScene();
        return scene.IsValid() ? scene.name : "No valid active scene";
    }

    private void ScanScene()
    {
        hasPreview = true;
        previewErrors.Clear();
        previewRootCount = 0;
        previewTerrainCount = 0;
        previewLightCount = 0;
        previewCameraCount = 0;

        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            previewErrors.Add("There is no valid, loaded active scene.");
            return;
        }

        GameObject[] roots = scene.GetRootGameObjects();
        previewRootCount = roots.Length;
        HashSet<Light> lights = new HashSet<Light>();
        HashSet<Camera> cameras = new HashSet<Camera>();

        foreach (GameObject root in roots)
        {
            Terrain[] rootTerrains = root.GetComponentsInChildren<Terrain>(true);
            previewTerrainCount += rootTerrains.Length;

            foreach (Terrain terrain in rootTerrains)
            {
                if (terrain.terrainData == null)
                    previewErrors.Add("Terrain has no TerrainData asset: " + terrain.name);

                if (Quaternion.Angle(terrain.transform.rotation, Quaternion.identity) > 0.01f)
                    previewErrors.Add("Terrain must have zero rotation: " + terrain.name);
            }

            foreach (Light light in root.GetComponentsInChildren<Light>(true))
            {
                if (light.type == LightType.Point || light.type == LightType.Spot)
                    lights.Add(light);
            }

            foreach (Camera camera in root.GetComponentsInChildren<Camera>(true))
                cameras.Add(camera);
        }

        previewLightCount = lights.Count;
        previewCameraCount = cameras.Count;

        if (previewTerrainCount > 0 && !scaleTerrainData)
            previewErrors.Add("This scene contains Terrain, but TerrainData scaling is disabled. Enable it to keep terrain tiles aligned.");

        Repaint();
    }

    private void ApplyRescale()
    {
        ScanScene();
        if (previewErrors.Count > 0)
        {
            EditorUtility.DisplayDialog("World Rescaler", "The scan found issues. Fix them before applying.", "OK");
            return;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        string summary =
            "This will rescale the active scene by " + scaleFactor.ToString("0.###") + "×.\n\n" +
            "Root hierarchies: " + previewRootCount + "\n" +
            "Terrain components: " + previewTerrainCount + "\n" +
            "Point/Spot lights: " + previewLightCount + "\n" +
            "Cameras: " + previewCameraCount + "\n\n" +
            "Cloned Terrain assets will be saved in:\n" + terrainAssetFolder + "\n\n" +
            "Continue?";

        if (!EditorUtility.DisplayDialog("Confirm World Rescale", summary, "Rescale Active Scene", "Cancel"))
            return;

        try
        {
            PerformRescale();
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            hasPreview = false;
            EditorUtility.DisplayDialog("World Rescaler", "Rescale completed successfully!", "OK");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorUtility.DisplayDialog(
                "World Rescaler Error",
                "An error occurred. Check the Console.\n\n" + exception.Message,
                "OK");
        }
    }

    private void PerformRescale()
    {
        Scene scene = SceneManager.GetActiveScene();
        GameObject[] roots = scene.GetRootGameObjects();
        Vector3 pivot = pivotMode == PivotMode.WorldOrigin ? Vector3.zero : customPivot;
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Rescale World");

        EnsureAssetFolderExists(terrainAssetFolder);

        Dictionary<Transform, Vector3> originalPositions = new Dictionary<Transform, Vector3>();
        foreach (GameObject root in roots)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                originalPositions[t] = t.position;
        }

        Dictionary<TerrainData, TerrainData> clonedTerrainDataMap = new Dictionary<TerrainData, TerrainData>();
        Dictionary<TerrainLayer, TerrainLayer> clonedLayerMap = new Dictionary<TerrainLayer, TerrainLayer>();

        foreach (GameObject root in roots)
        {
            ScaleHierarchyRecursive(
                root.transform,
                pivot,
                originalPositions,
                clonedTerrainDataMap,
                clonedLayerMap);
        }

        foreach (GameObject root in roots)
        {
            foreach (Light light in root.GetComponentsInChildren<Light>(true))
            {
                if (light.type != LightType.Point && light.type != LightType.Spot)
                    continue;

                if (scalePointAndSpotLightRanges || scalePointAndSpotLightIntensity)
                {
                    Undo.RecordObject(light, "Rescale Light Settings");
                    if (scalePointAndSpotLightRanges)
                        light.range *= scaleFactor;
                    if (scalePointAndSpotLightIntensity)
                        light.intensity *= scaleFactor * scaleFactor;
                    EditorUtility.SetDirty(light);
                }
            }

            if (scaleCameraClipPlanes)
            {
                foreach (Camera camera in root.GetComponentsInChildren<Camera>(true))
                {
                    Undo.RecordObject(camera, "Rescale Camera Clip Planes");
                    camera.nearClipPlane = Mathf.Max(0.01f, camera.nearClipPlane * scaleFactor);
                    camera.farClipPlane = Mathf.Max(camera.nearClipPlane + 1f, camera.farClipPlane * scaleFactor);
                    EditorUtility.SetDirty(camera);
                }
            }
        }

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(scene);
        SceneView.RepaintAll();
    }

    private void ScaleHierarchyRecursive(
        Transform current,
        Vector3 pivot,
        Dictionary<Transform, Vector3> originalPositions,
        Dictionary<TerrainData, TerrainData> clonedTerrainDataMap,
        Dictionary<TerrainLayer, TerrainLayer> clonedLayerMap)
    {
        Terrain terrainOnCurrent = current.GetComponent<Terrain>();
        bool hasTerrainInChildren = current.GetComponentInChildren<Terrain>(true) != null;

        Undo.RecordObject(current, "Rescale Transform");

        Vector3 origPos = originalPositions[current];
        current.position = pivot + (origPos - pivot) * scaleFactor;

        if (!hasTerrainInChildren && terrainOnCurrent == null)
        {
            current.localScale *= scaleFactor;
            return;
        }

        current.localScale = Vector3.one;

        if (terrainOnCurrent != null && scaleTerrainData)
        {
            ReplaceWithScaledTerrainData(terrainOnCurrent, clonedTerrainDataMap, clonedLayerMap);
        }

        for (int i = 0; i < current.childCount; i++)
        {
            ScaleHierarchyRecursive(
                current.GetChild(i),
                pivot,
                originalPositions,
                clonedTerrainDataMap,
                clonedLayerMap);
        }
    }

    private void ReplaceWithScaledTerrainData(
        Terrain terrain,
        Dictionary<TerrainData, TerrainData> clonedTerrainDataMap,
        Dictionary<TerrainLayer, TerrainLayer> clonedLayerMap)
    {
        TerrainData source = terrain.terrainData;
        if (source == null) return;

        if (!clonedTerrainDataMap.TryGetValue(source, out TerrainData clone))
        {
            string sourcePath = AssetDatabase.GetAssetPath(source);
            string suffix = "_Scaled_" + scaleFactor.ToString("0.###").Replace('.', '_');
            string targetPath = AssetDatabase.GenerateUniqueAssetPath(
                terrainAssetFolder.TrimEnd('/') + "/" + source.name + suffix + ".asset");

            if (!string.IsNullOrEmpty(sourcePath) && AssetDatabase.CopyAsset(sourcePath, targetPath))
            {
                clone = AssetDatabase.LoadAssetAtPath<TerrainData>(targetPath);
            }
            else
            {
                clone = Instantiate(source);
                AssetDatabase.CreateAsset(clone, targetPath);
            }

            clone.size = source.size * scaleFactor;

            if (scaleTerrainLayerTiling && clone.terrainLayers != null && clone.terrainLayers.Length > 0)
            {
                TerrainLayer[] layers = clone.terrainLayers;
                for (int i = 0; i < layers.Length; i++)
                {
                    TerrainLayer srcLayer = layers[i];
                    if (srcLayer == null) continue;

                    if (!clonedLayerMap.TryGetValue(srcLayer, out TerrainLayer clonedLayer))
                    {
                        string layerPath = AssetDatabase.GetAssetPath(srcLayer);
                        string newLayerPath = AssetDatabase.GenerateUniqueAssetPath(
                            terrainAssetFolder.TrimEnd('/') + "/" + srcLayer.name + suffix + ".terrainlayer");

                        if (!string.IsNullOrEmpty(layerPath) && AssetDatabase.CopyAsset(layerPath, newLayerPath))
                            clonedLayer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(newLayerPath);
                        else
                        {
                            clonedLayer = Instantiate(srcLayer);
                            AssetDatabase.CreateAsset(clonedLayer, newLayerPath);
                        }

                        clonedLayer.tileSize = srcLayer.tileSize * scaleFactor;
                        clonedLayer.tileOffset = srcLayer.tileOffset * scaleFactor;
                        EditorUtility.SetDirty(clonedLayer);
                        clonedLayerMap[srcLayer] = clonedLayer;
                    }

                    layers[i] = clonedLayer;
                }
                clone.terrainLayers = layers;
            }

            if (scaleTerrainTreeSizes)
            {
                TreeInstance[] trees = clone.treeInstances;
                for (int i = 0; i < trees.Length; i++)
                {
                    TreeInstance tree = trees[i];
                    tree.widthScale *= scaleFactor;
                    tree.heightScale *= scaleFactor;
                    trees[i] = tree;
                }
                clone.SetTreeInstances(trees, false);
            }

            if (scaleTerrainDetailSizes)
            {
                DetailPrototype[] details = clone.detailPrototypes;
                for (int i = 0; i < details.Length; i++)
                {
                    DetailPrototype detail = details[i];
                    detail.minWidth *= scaleFactor;
                    detail.maxWidth *= scaleFactor;
                    detail.minHeight *= scaleFactor;
                    detail.maxHeight *= scaleFactor;
                    details[i] = detail;
                }
                clone.detailPrototypes = details;
            }

            clone.RefreshPrototypes();
            EditorUtility.SetDirty(clone);
            clonedTerrainDataMap[source] = clone;
        }

        Undo.RecordObject(terrain, "Assign Scaled TerrainData");
        terrain.terrainData = clone;

        TerrainCollider terrainCollider = terrain.GetComponent<TerrainCollider>();
        if (terrainCollider != null)
        {
            Undo.RecordObject(terrainCollider, "Assign Scaled Collider TerrainData");
            terrainCollider.terrainData = clone;
            EditorUtility.SetDirty(terrainCollider);
        }

        if (scaleTerrainRenderDistances)
        {
            terrain.treeDistance *= scaleFactor;
            terrain.treeBillboardDistance *= scaleFactor;
            terrain.detailObjectDistance *= scaleFactor;
            terrain.basemapDistance *= scaleFactor;
        }

        terrain.Flush();
        EditorUtility.SetDirty(terrain);
    }

    private static void EnsureAssetFolderExists(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !folderPath.StartsWith("Assets", StringComparison.Ordinal))
            throw new InvalidOperationException("Generated asset folder must be inside the project's Assets folder.");

        string normalized = folderPath.Replace('\\', '/').TrimEnd('/');
        if (AssetDatabase.IsValidFolder(normalized))
            return;

        string[] parts = normalized.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}
#endif