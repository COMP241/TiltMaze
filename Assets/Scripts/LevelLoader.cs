﻿using System;
using System.Collections;
using System.Linq;
using Assets.Scripts.CSG;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Rendering;

public class LevelLoader : MonoBehaviour
{
    private static LevelLoader instance;

    private ImageMap map;
    private bool loading;

    // Generated Fields
    private float horizontalScale;
    private float verticalScale;
    private Vector3 adjust;
    private GameObject floor;

    // Editor Fields
    [Header("Function")]
    [SerializeField] private Transform levelContainer;
    [SerializeField] private GameObject playContainer;
    [SerializeField] private float allScale = 10f;

    [Header("Prefabs")]
    [SerializeField] private GameObject goalPrefab;

    [Header("Aesthetic")]
    [SerializeField] private Material lineMaterial;
    [SerializeField] private Material floorMaterial;

    private void Start()
    {
        if (instance == null)
            instance = GetComponent<LevelLoader>();
        else
            Destroy(gameObject);
    }

    public void Load(string id)
    {
        if (loading)
            return;

        try
        {
            StartCoroutine(LoadLevel(int.Parse(id)));
        }
        catch (FormatException)
        {
            GameStartCanvas.DisplayError("Level ID in wrong format.");
        }
    }
    
    private IEnumerator LoadLevel(int id)
    {
        loading = true;
        GameStartCanvas.DisplayInfo("Loading...");
        using (UnityWebRequest www = UnityWebRequest.Get("http://papermap.tk/api/map/" + id))
        {
            yield return www.Send();

            if (!www.isError)
            {
                map = ImageMap.FromJson(www.downloadHandler.text);
                GenerateLevel();
            }
            else
            {
                Debug.Log(www.error);
                GameStartCanvas.DisplayError("Failed to get map.");
            }
        }
        loading = false;
    }

    private void GenerateLevel()
    {
        try
        {
            SetConstants();
            MakeSpawn();
            MakeFloor();
            MakeWalls();
            MakeObstacles();
            MakeGoals();
            playContainer.SetActive(true);
            GameController.Begin();
        }
        catch (InvalidOperationException)
        {
            GameStartCanvas.DisplayError("Map missing spawn.");
        }
        catch (NullReferenceException)
        {
            GameStartCanvas.DisplayError("Failed to get map.");
        }
    }

    private void SetConstants()
    {
        if (map.Ratio >= 1f)
        {
            horizontalScale = map.Ratio;
            verticalScale = 1f;
        }
        else
        {
            horizontalScale = 1f;
            verticalScale = 1f / map.Ratio;
        }
        
        adjust = new Vector3(-horizontalScale * allScale / 2f, 0, verticalScale * allScale / 2f);
    }

    private void MakeSpawn()
    {
        Line spawnLine = map.Lines.First(l => l.Color == MapColor.Blue);
        Point averagePoint = spawnLine.AveragePoint();
        GameController.SetSpawn(PointToWorldSpace(averagePoint) + Vector3.up * 0.5f);
        Player p = playContainer.GetComponentInChildren<Player>();
        p.transform.localScale = Vector3.one * 2f * PointScaleToWorldScale(Mathf.Sqrt(spawnLine.AverageSqrDistanceFrom(averagePoint)));
    }

    private void MakeFloor()
    {
        floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.transform.parent = levelContainer;
        floor.transform.position = Vector3.down * 0.25f;
        floor.transform.localScale = new Vector3(horizontalScale * allScale, 0.5f, verticalScale * allScale);
    }

    private void MakeWalls()
    {
        foreach (Line line in map.Lines.Where(l => l.Color == MapColor.Black))
        {
            GameObject wall = new GameObject("Wall", typeof (MeshCollider), typeof(LineRenderer));

            // Set up GameObject
            Mesh mesh = LineToMeshComponents(line, Vector3.zero);
            wall.GetComponent<MeshCollider>().sharedMesh = mesh;
            wall.transform.parent = levelContainer;
            wall.transform.position = Vector3.zero;
            SetUpLineRenderer(wall, line, Color.black);
        }
    }

    private void MakeObstacles()
    {
        // TODO: Bore holes

        foreach (Line line in map.Lines.Where(l => l.Color == MapColor.Red))
        {
            Mesh mesh = Triangulator.PolygonExtrude(line.Points.Select(PointToWorldSpace).Select(w => new Vector2(w.x, w.z)).ToArray(), Vector3.down, 2f);

            // Set up GameObject
            GameObject obstacle = new GameObject();
            obstacle.AddComponent<MeshFilter>().sharedMesh = mesh;

            Mesh newFloor = CSG.Subtract(floor, obstacle);
            GameObject composite = new GameObject("Floor");
            composite.AddComponent<MeshFilter>().sharedMesh = newFloor;

            Destroy(obstacle);
            Destroy(floor);
            floor = composite;
        }

        // Finalise floor
        floor.AddComponent<MeshCollider>().sharedMesh = floor.GetComponent<MeshFilter>().mesh;
        floor.AddComponent<MeshRenderer>().material = floorMaterial;
        floor.transform.parent = levelContainer;
    }

    private void MakeGoals()
    {
        foreach (Line line in map.Lines.Where(l => l.Color == MapColor.Green))
        {
            Instantiate(goalPrefab, PointToWorldSpace(line.AveragePoint()), Quaternion.identity, levelContainer);
        }
    }

    private void SetUpLineRenderer(GameObject o, Line line, Color color)
    {
        LineRenderer renderer = o.GetComponent<LineRenderer>();
        renderer.positionCount = line.Points.Length;
        renderer.SetPositions(line.Points.Select(p => PointToWorldSpace(p) + Vector3.up * 0.5f).ToArray());
        renderer.startWidth = 0.1f;
        renderer.endWidth = 0.1f;
        renderer.loop = line.Loop;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.material = lineMaterial;
        renderer.startColor = color;
        renderer.endColor = color;
    }

    private Vector3 PointToWorldSpace(Point p)
    {
        return new Vector3(p.X * horizontalScale * allScale, 0f, -p.Y * verticalScale * allScale) + adjust;
    }

    private float PointScaleToWorldScale(float f)
    {
        return f * allScale * Mathf.Max(verticalScale, horizontalScale);
    }

    private Mesh LineToMeshComponents(Line line, Vector3 offset, float height = 1f)
    {
        Mesh mesh = new Mesh();
        Point[] points = line.Points;

        // Set up required collections for mesh
        Vector3[] vertices = new Vector3[line.Points.Length * 2];
        int[] triangles = new int[(vertices.Length - (line.Loop ? 0 : 2)) * 6];

        // Vertices generation. Grey magic. Probably don't touch.
        for (int p = 0; p < points.Length; p++)
        {
            Vector3 floorVector = PointToWorldSpace(points[p]) + offset;
            vertices[p * 2] = floorVector;
            vertices[p * 2 + 1] = floorVector + Vector3.up * height;
        }

        // Triangles generation. Black magic. Definitely don't touch.
        int len = vertices.Length; // Any modulus will *only* occur for looping lines
        for (int i = 0; i < triangles.Length; i += 12)
        {
            int start = i / 6;
            // "Lower left" triangle [0,1,2]
            triangles[i] = start;
            triangles[i + 1] = start + 1;
            triangles[i + 2] = (start + 2) % len;

            // "Upper right" triangle [2,1,3]
            triangles[i + 3] = (start + 2) % len;
            triangles[i + 4] = start + 1;
            triangles[i + 5] = (start + 3) % len;

            // Reverse of previous two (so both sides of mesh are working)
            triangles[i + 6] = triangles[i + 2];
            triangles[i + 7] = triangles[i + 1];
            triangles[i + 8] = triangles[i];

            triangles[i + 9] = triangles[i + 5];
            triangles[i + 10] = triangles[i + 4];
            triangles[i + 11] = triangles[i + 3];
        }

        mesh.vertices = vertices;
        mesh.triangles = triangles;

        return mesh;
    }

    public static void Unload()
    {
        Destroy(instance.levelContainer.gameObject);
        GameObject newContainer = new GameObject("Level Container");
        newContainer.transform.parent = instance.playContainer.transform;
        instance.levelContainer = newContainer.transform;
    }

    public static void SetActive(bool value)
    {
        instance.playContainer.SetActive(value);
    }
}