using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;

public class Spawner3D : MonoBehaviour
{
    public float spawnDensity = 1f;
    public Vector3 initialVelocity;
    public float jitterStrength;
    public SpawnRegion[] spawnRegions;
    public bool showSpawnBoundsGizmos = true;

    [Header("Debug Info")]
    public int spawnParticleCount;

    public ParticleSpawnData GetSpawnData()
    {
        var rng = new Unity.Mathematics.Random(42);

        List<float3> allPoints = new();
        List<float3> allVelocities = new();
        List<int> allIndices = new();

        for (int regionIndex = 0; regionIndex < spawnRegions.Length; regionIndex++)
        {
            SpawnRegion region = spawnRegions[regionIndex];
            float3[] points = SpawnInRegion(region);

            for (int i = 0; i < points.Length; i++)
            {
                // Random 3D jitter direction.
                float3 dir = RandomDirection3D(ref rng);
                float3 jitter = dir * jitterStrength * ((float)rng.NextDouble() - 0.5f);

                allPoints.Add(points[i] + jitter);
                allVelocities.Add(initialVelocity);
                allIndices.Add(regionIndex);
            }
        }

        return new ParticleSpawnData
        {
            positions = allPoints.ToArray(),
            velocities = allVelocities.ToArray(),
            spawnIndices = allIndices.ToArray(),
        };
    }

    float3[] SpawnInRegion(SpawnRegion region)
    {
        Vector3 centre = region.position;
        Vector3 size = region.size;

        Vector3Int numPerAxis = CalculateSpawnCountPerAxisBox3D(size, spawnDensity);
        float3[] points = new float3[numPerAxis.x * numPerAxis.y * numPerAxis.z];

        int i = 0;

        for (int z = 0; z < numPerAxis.z; z++)
        {
            for (int y = 0; y < numPerAxis.y; y++)
            {
                for (int x = 0; x < numPerAxis.x; x++)
                {
                    float tx = numPerAxis.x <= 1 ? 0.5f : x / (numPerAxis.x - 1f);
                    float ty = numPerAxis.y <= 1 ? 0.5f : y / (numPerAxis.y - 1f);
                    float tz = numPerAxis.z <= 1 ? 0.5f : z / (numPerAxis.z - 1f);

                    float px = (tx - 0.5f) * size.x + centre.x;
                    float py = (ty - 0.5f) * size.y + centre.y;
                    float pz = (tz - 0.5f) * size.z + centre.z;

                    points[i] = new float3(px, py, pz);
                    i++;
                }
            }
        }

        return points;
    }

    static Vector3Int CalculateSpawnCountPerAxisBox3D(Vector3 size, float spawnDensity)
    {
        size = new Vector3(
            Mathf.Max(size.x, 0.0001f),
            Mathf.Max(size.y, 0.0001f),
            Mathf.Max(size.z, 0.0001f)
        );

        float volume = size.x * size.y * size.z;
        int targetTotal = Mathf.Max(1, Mathf.CeilToInt(volume * Mathf.Max(0f, spawnDensity)));

        float lenSum = size.x + size.y + size.z;
        Vector3 t = size / lenSum;

        float m = Mathf.Pow(targetTotal / (t.x * t.y * t.z), 1f / 3f);

        int nx = Mathf.Max(1, Mathf.CeilToInt(t.x * m));
        int ny = Mathf.Max(1, Mathf.CeilToInt(t.y * m));
        int nz = Mathf.Max(1, Mathf.CeilToInt(t.z * m));

        return new Vector3Int(nx, ny, nz);
    }

    static float3 RandomDirection3D(ref Unity.Mathematics.Random rng)
    {
        float z = (float)rng.NextDouble() * 2f - 1f;
        float angle = (float)rng.NextDouble() * Mathf.PI * 2f;
        float radius = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));

        return new float3(
            radius * Mathf.Cos(angle),
            radius * Mathf.Sin(angle),
            z
        );
    }

    public struct ParticleSpawnData
    {
        public float3[] positions;
        public float3[] velocities;
        public int[] spawnIndices;

        public ParticleSpawnData(int num)
        {
            positions = new float3[num];
            velocities = new float3[num];
            spawnIndices = new int[num];
        }
    }

    [System.Serializable]
    public struct SpawnRegion
    {
        public Vector3 position;
        public Vector3 size;
        public Color debugColor;
    }

    void OnValidate()
    {
        spawnParticleCount = 0;

        if (spawnRegions == null)
            return;

        foreach (SpawnRegion region in spawnRegions)
        {
            Vector3Int spawnCountPerAxis = CalculateSpawnCountPerAxisBox3D(region.size, spawnDensity);
            spawnParticleCount += spawnCountPerAxis.x * spawnCountPerAxis.y * spawnCountPerAxis.z;
        }
    }

    void OnDrawGizmos()
    {
        if (!showSpawnBoundsGizmos || spawnRegions == null || Application.isPlaying)
            return;

        foreach (SpawnRegion region in spawnRegions)
        {
            Gizmos.color = region.debugColor;
            Gizmos.DrawWireCube(region.position, region.size);
        }
    }
}
