
using System;

using System.Threading.Tasks;

using UnityEngine;
using UnityEngine.UIElements;


public class Simulation : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    public int numberOfParticles;
    public float targetDensity;
    public float pressureMultiplier;
    public float particleSpacing;
    public float radius;
    public float gravity;
    public float collisionDamping;
    public float smoothingRadius;
    public float interactionRadius = 2f;
    public float interactionStrength = 50f;
    public float maxVisualizedSpeed = 5f;

    public Vector2 boundsSize;
    Vector2[] positions;
    Vector2[] velocities;
    Entry[] spatialLookup;
    int[] startIndices;
    Vector2[] predictedPositions;
    Mesh circleMesh;
    Material circleMaterial;
    float[] densities;
    Vector2 mousePosition;
    bool isPulling;
    bool isPushing;
    public int substeps = 4;
    MaterialPropertyBlock propertyBlock;
    static readonly int ColorID = Shader.PropertyToID("_Color");
    (int x, int y)[] cellOffSets =
  {
    (-1, -1), (0, -1), (1, -1),
    (-1,  0), (0,  0), (1,  0),
    (-1,  1), (0,  1), (1,  1)
};
    public struct Entry : IComparable<Entry>
    {
        public int particleIndex;
        public uint cellKey;

        public Entry(int particleIndex, uint cellKey)
        {
            this.particleIndex = particleIndex;
            this.cellKey = cellKey;
        }

        public int CompareTo(Entry other)
        {
            return cellKey.CompareTo(other.cellKey);
        }
    }
    public void UpdateSpatialLookup(Vector2[] points)
    {

        Parallel.For(0, points.Length, i =>
        {
            (int cellX, int cellY) = PositionToCellCord(points[i], smoothingRadius);
            uint cellKey = GetKeyFromHash(HashCell(cellX, cellY));
            spatialLookup[i] = new Entry(i, cellKey);
            startIndices[i] = int.MaxValue;
        });
        Array.Sort(spatialLookup);
        Parallel.For(0, points.Length, i =>
        {
            uint key = spatialLookup[i].cellKey;
            uint keyPrev = i == 0 ? uint.MaxValue : spatialLookup[i - 1].cellKey;
            if (key != keyPrev)
            {
                startIndices[key] = i;
            }
        });
    }
    public (int x, int y) PositionToCellCord(Vector2 point, float cellSize)
    {
        int cellX = Mathf.FloorToInt(point.x / cellSize);
        int cellY = Mathf.FloorToInt(point.y / cellSize);
        return (cellX, cellY);
    }
    public uint HashCell(int cellX, int cellY)
    {
        uint a = (uint)cellX * 15823;
        uint b = (uint)cellY * 9737333;
        return a + b;
    }
    public uint GetKeyFromHash(uint hash)
    {
        return hash % (uint)spatialLookup.Length;
    }

    public void ForEachPointWithinRadius(Vector2 samplePoint, Action<int> callback)
    {
        (int centreX, int centreY) = PositionToCellCord(samplePoint, smoothingRadius);
        float sqrRadius = smoothingRadius * smoothingRadius;

        foreach ((int offSetX, int offSetY) in cellOffSets)

        {
            uint key = GetKeyFromHash(HashCell(centreX + offSetX, centreY + offSetY));
            int cellStartIndex = startIndices[key];
            if (cellStartIndex == int.MaxValue)
                continue;

            for (int i = cellStartIndex; i < spatialLookup.Length; i++)
            {
                if (spatialLookup[i].cellKey != key) break;
                int particleIndex = spatialLookup[i].particleIndex;
                float sqrDst = (predictedPositions[particleIndex] - samplePoint).sqrMagnitude;
                if (sqrDst <= sqrRadius)
                {
                    callback(particleIndex);
                }
            }
        }
    }
    void CreateCircleMesh()
    {
        int segments = 20;
        Vector3[] vertices = new Vector3[segments + 1];
        int[] triangles = new int[segments * 3];

        vertices[0] = Vector3.zero;
        for (int i = 0; i < segments; i++)
        {
            float angle = i / (float)segments * Mathf.PI * 2;
            vertices[i + 1] = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0);
        }

        for (int i = 0; i < segments; i++)
        {
            triangles[i * 3] = 0;
            triangles[i * 3 + 1] = i + 1;
            triangles[i * 3 + 2] = (i + 1) % segments + 1;
        }

        circleMesh = new Mesh();
        circleMesh.vertices = vertices;
        circleMesh.triangles = triangles;
        circleMesh.RecalculateNormals();

        circleMaterial = new Material(Shader.Find("Sprites/Default"));
    }
    void DrawCircle(Vector2 pos, float radius, Color colour)
    {
        propertyBlock.SetColor(ColorID, colour);

        Matrix4x4 matrix = Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one * radius);
        Graphics.DrawMesh(circleMesh, matrix, circleMaterial, 0, null, 0, propertyBlock);
    }

    void SpawnUniformParticles()
    {

        densities = new float[numberOfParticles];
        positions = new Vector2[numberOfParticles];
        velocities = new Vector2[numberOfParticles];
        predictedPositions = new Vector2[numberOfParticles];
        spatialLookup = new Entry[numberOfParticles];
        startIndices = new int[numberOfParticles];

        int particlesPerRow = (int)Math.Sqrt(numberOfParticles);
        int particlesPerCol = (numberOfParticles - 1) / particlesPerRow + 1;
        float spacing = radius * 2 + particleSpacing;

        for (int i = 0; i < numberOfParticles; i++)
        {
            int row = i / particlesPerRow;
            int col = i % particlesPerRow;

            float x = (col - particlesPerRow / 2f + 0.5f) * spacing;
            float y = (row - particlesPerCol / 2f + 0.5f) * spacing;

            positions[i] = new Vector2(x, y);
        }
    }

    void Start()
    {
        CreateCircleMesh();
        propertyBlock = new MaterialPropertyBlock();
        SpawnUniformParticles();
    }

    // Update is called once per frame
    void Update()
    {
        UpdateInput();
        float dt = Time.deltaTime / substeps;

        for (int step = 0; step < substeps; step++)
        {
            SimulationStep(dt);
        }

        for (int i = 0; i < numberOfParticles; i++)
        {
            float speed = velocities[i].magnitude;
            float t = Mathf.Clamp01(speed / maxVisualizedSpeed);
            Color color = Color.Lerp(Color.blue, Color.red, t);

            DrawCircle(positions[i], radius, color);
        }

    }
    void UpdateInput()
    {
        mousePosition = Camera.main.ScreenToWorldPoint(Input.mousePosition);

        isPulling = Input.GetMouseButton(0);
        isPushing = Input.GetMouseButton(1);
    }
    void SimulationStep(float dt)
    {
        // Apply gravity and predict positions
        Parallel.For(0, numberOfParticles, i =>
        {
            velocities[i] += Vector2.down * gravity * dt;
            predictedPositions[i] = positions[i] + velocities[i] * 1 / 120f;

        });

        UpdateSpatialLookup(predictedPositions);
        // Calculate densities from predicted positions
        Parallel.For(0, numberOfParticles, i =>
        {
            densities[i] = CalculateDensity(predictedPositions[i]);
        });

        // Apply pressure forces
        Parallel.For(0, numberOfParticles, i =>
        {
            Vector2 pressureForce = CalculatePressureForce(i);
            Vector2 pressureAcceleration = pressureForce / densities[i];
            velocities[i] += pressureAcceleration * dt;

        });
        if (isPulling || isPushing)
        {
            ApplyInteraction(dt);
        }

        // Update positions and collisions
        Parallel.For(0, numberOfParticles, i =>
        {
            positions[i] += velocities[i] * dt;
            ResolveCollisions(ref positions[i], ref velocities[i]);
        });
    }

    void ResolveCollisions(ref Vector2 position, ref Vector2 velocity)
    {
        Vector2 halfBoundsSize = boundsSize / 2 - Vector2.one * radius;
        if (Mathf.Abs(position.x) > halfBoundsSize.x)
        {
            position.x = halfBoundsSize.x * Mathf.Sign(position.x);
            velocity.x *= -1 * collisionDamping;
        }
        if (Mathf.Abs(position.y) > halfBoundsSize.y)
        {
            position.y = halfBoundsSize.y * Mathf.Sign(position.y);
            velocity.y *= -1 * collisionDamping;
        }
    }
    void ApplyInteraction(float dt)
    {

        float strength = isPulling
            ? interactionStrength
            : -interactionStrength;

        Parallel.For(0, numberOfParticles, i =>
        {
            velocities[i] += InteractionForce(
                mousePosition,
                interactionRadius,
                strength,
                i) * dt;
        });
    }

    float SmoothingKernel(float radius, float dst)
    {
        if (dst >= radius) return 0;
        float volume = (Mathf.PI * Mathf.Pow(radius, 4)) / 6;
        return (radius - dst) * (radius - dst) / volume;
    }
    float SmoothingKernelDerivative(float radius, float dst)
    {
        if (dst >= radius) return 0;

        float scale = 12 / (Mathf.PI * Mathf.Pow(radius, 4));
        return (dst - radius) * scale;
    }
    float CalculateSharedPressure(float densityA, float densityB)
    {
        float pressureA = ConvertDensityToPressure(densityA);
        float pressureB = ConvertDensityToPressure(densityB);
        return (pressureA + pressureB) / 2;
    }

    Vector2 CalculatePressureForce(int particleIndex)
    {
        Vector2 pressureForce = Vector2.zero;

        ForEachPointWithinRadius(predictedPositions[particleIndex], otherIndex =>
        {
            if (particleIndex == otherIndex) return;

            Vector2 offset = predictedPositions[otherIndex] - predictedPositions[particleIndex];
            float dst = offset.magnitude;

            if (dst == 0) return;

            Vector2 dir = offset / dst;

            float slope = SmoothingKernelDerivative(smoothingRadius, dst);

            float density = densities[otherIndex];
            float sharedPressure = CalculateSharedPressure(density, densities[particleIndex]);

            pressureForce += sharedPressure * dir * slope / density;
        });

        return pressureForce;
    }
    Vector2 InteractionForce(Vector2 inputPos, float radius, float strength, int particleIndex)
    {
        Vector2 interactionForce = Vector2.zero;
        Vector2 offset = inputPos - predictedPositions[particleIndex];
        float sqrDst = Vector2.Dot(offset, offset);

        if (sqrDst < radius * radius)
        {
            float dst = Mathf.Sqrt(sqrDst);
            Vector2 dirToInputPoint = dst <= float.Epsilon ? Vector2.zero : offset / dst;
            float centreT = 1 - dst / radius;
            interactionForce += (dirToInputPoint * strength - velocities[particleIndex]) * centreT;
        }
        return interactionForce;
    }
    float CalculateDensity(Vector2 samplePoint)
    {
        float density = 0;
        const float mass = 1;
        ForEachPointWithinRadius(samplePoint, particleIndex =>
        {
            float dst = (predictedPositions[particleIndex] - samplePoint).magnitude;
            float influence = SmoothingKernel(smoothingRadius, dst);
            density += mass * influence;
        });
        return density;
    }
    float ConvertDensityToPressure(float density)
    {
        float densityError = density - targetDensity;
        float pressure = densityError * pressureMultiplier;
        return pressure;
    }
    void OnDrawGizmos()
    {
        if (boundsSize != null)
        {
            Gizmos.DrawWireCube(Vector2.zero, boundsSize);
        }
        if (Application.isPlaying)
        {
            Vector2 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            bool isPullInteraction = Input.GetMouseButton(0);
            bool isPushInteraction = Input.GetMouseButton(1);
            bool isInteracting = isPullInteraction || isPushInteraction;
            if (isInteracting)
            {
                Gizmos.color = isPullInteraction ? Color.green : Color.red;
                Gizmos.DrawWireSphere(mousePos, interactionRadius);
            }
        }

    }

}
