
using System;

using System.Threading.Tasks;

using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;


public class Simulation : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    [Header("Settings")]
    public float timeScale = 1f;
    public int maxTimestepFPS = 60;
    public int substeps = 4;
    public int numberOfParticles;
    public float pressureMultiplier;
    public float nearPressureMultiplier;
    public float viscosityStrength = 0.5f;
    public float gravity;
    public float targetDensity;
    public float particleSpacing;
    public float radius;
    [Range(0, 1)] public float collisionDamping = 0.95f;
    public float smoothingRadius;
    public Vector2 boundsSize;

    [Header("Interaction Settings")]
    public float interactionRadius = 2f;
    public float interactionStrength = 50f;
    bool isPaused;

    bool stepOneFrame;


    Vector2[] positions;
    Vector2[] velocities;
    Entry[] spatialLookup;
    int[] startIndices;
    Vector2[] predictedPositions;

    (float, float)[] densities; // density and near density
    Vector2 mousePosition;
    bool isPulling;
    bool isPushing;



    (int x, int y)[] cellOffSets =
  {
    (-1, -1), (0, -1), (1, -1),
    (-1,  0), (0,  0), (1,  0),
    (-1,  1), (0,  1), (1,  1)
};
    // Buffers
    public ComputeBuffer positionBuffer { get; private set; }
    public ComputeBuffer velocityBuffer { get; private set; }
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



    void SpawnUniformParticles()
    {

        densities = new (float, float)[numberOfParticles];
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

        SpawnUniformParticles();
        Init();
    }
    void Init()
    {
        float deltaTime = 1 / 60f;
        Time.fixedDeltaTime = deltaTime;
        positionBuffer = new ComputeBuffer(numberOfParticles, sizeof(float) * 2);
        velocityBuffer = new ComputeBuffer(numberOfParticles, sizeof(float) * 2);


        positionBuffer.SetData(positions);
        velocityBuffer.SetData(velocities);

    }

    // Update is called once per frame
    void Update()
    {
        HandleInput();

        bool shouldSimulate = !isPaused || stepOneFrame;

        if (shouldSimulate)
        {
            float maxDeltaTime = maxTimestepFPS > 0 ? 1f / maxTimestepFPS : float.PositiveInfinity;
            float frameDt = Mathf.Min(Time.deltaTime * timeScale, maxDeltaTime);
            float stepDt = frameDt / substeps;

            for (int i = 0; i < substeps; i++)
            {
                SimulationStep(stepDt);
            }

            stepOneFrame = false;
        }

        positionBuffer.SetData(positions);
        velocityBuffer.SetData(velocities);

    }
    void HandleInput()
    {
        mousePosition = Camera.main.ScreenToWorldPoint(Input.mousePosition);

        isPulling = Input.GetMouseButton(0);
        isPushing = Input.GetMouseButton(1);

        if (Input.GetKeyDown(KeyCode.Space))
            isPaused = !isPaused;

        if (Input.GetKeyDown(KeyCode.RightArrow))
            stepOneFrame = true;
    }

    void SimulationStep(float dt)
    {
        // Apply gravity and predict positions
        Parallel.For(0, numberOfParticles, i =>
        {
            velocities[i] += Vector2.down * gravity * dt;
            predictedPositions[i] = positions[i] + velocities[i] * dt;


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
            Vector2 pressureAcceleration = pressureForce / densities[i].Item1;
            velocities[i] += pressureAcceleration * dt;

        });
        Parallel.For(0, numberOfParticles, i =>
        {
            Vector2 viscosityForce = CalculateViscosityForce(i);
            velocities[i] += viscosityForce * dt;
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
    float ViscositySmoothKernel(float radius, float dst)
    {
        if (dst >= radius) return 0;
        float volume = (Mathf.PI * Mathf.Pow(radius, 4)) / 6;
        float value = Mathf.Max(0, radius * radius - dst * dst);
        return value * value * value / volume;
    }
    float NearSmoothingKernel(float radius, float dst)
    {
        if (dst >= radius) return 0;
        float volume = (Mathf.PI * Mathf.Pow(radius, 5)) / 10f;
        return (radius - dst) * (radius - dst) * (radius - dst) / volume;
    }
    float NearSmoothingKernelDerivative(float radius, float dst)
    {
        if (dst >= radius) return 0;

        float volume = Mathf.PI * Mathf.Pow(radius, 5) / 10f;
        float value = radius - dst;

        return -3f * value * value / volume;
    }

    (float pressure, float nearPressure) CalculateSharedPressure(
     (float density, float nearDensity) densityA,
     (float density, float nearDensity) densityB)
    {
        (float pressureA, float nearPressureA) =
            ConvertDensityToPressure(densityA.density, densityA.nearDensity);

        (float pressureB, float nearPressureB) =
            ConvertDensityToPressure(densityB.density, densityB.nearDensity);

        return (
            (pressureA + pressureB) / 2f,
            (nearPressureA + nearPressureB) / 2f
        );
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
            float nearSlope = NearSmoothingKernelDerivative(smoothingRadius, dst);
            (float density, float nearDensity) densityA = densities[particleIndex];
            (float density, float nearDensity) densityB = densities[otherIndex];

            (float sharedPressure, float sharedNearPressure) =
                CalculateSharedPressure(densityA, densityB);
            pressureForce += (sharedPressure * slope + sharedNearPressure * nearSlope) * dir / densityB.density;
        });

        return pressureForce;
    }
    Vector2 CalculateViscosityForce(int particleIndex)
    {
        Vector2 viscosityForce = Vector2.zero;
        Vector2 position = predictedPositions[particleIndex];
        ForEachPointWithinRadius(position, otherIndex =>
        {
            float dst = (position - predictedPositions[otherIndex]).magnitude;
            float influence = ViscositySmoothKernel(smoothingRadius, dst);
            viscosityForce += (velocities[otherIndex] - velocities[particleIndex]) * influence;
        });
        return viscosityForce * viscosityStrength;
    }
    Vector2 InteractionForce(Vector2 inputPos, float radius, float strength, int particleIndex)
    {
        Vector2 interactionForce = Vector2.zero;
        Vector2 offset = inputPos - positions[particleIndex];
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
    (float, float) CalculateDensity(Vector2 samplePoint)
    {
        float density = 0;
        float nearDensity = 0;
        const float mass = 1;
        ForEachPointWithinRadius(samplePoint, particleIndex =>
        {
            float dst = (predictedPositions[particleIndex] - samplePoint).magnitude;
            float influence = SmoothingKernel(smoothingRadius, dst);
            float nearInfluence = NearSmoothingKernel(smoothingRadius, dst);
            density += mass * influence;
            nearDensity += mass * nearInfluence;
        });
        return (density, nearDensity);
    }
    (float, float) ConvertDensityToPressure(float density, float nearDensity)
    {
        float densityError = density - targetDensity;
        float pressure = densityError * pressureMultiplier;
        float nearPressure = nearDensity * nearPressureMultiplier;
        return (pressure, nearPressure);
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
    void OnDestroy()
    {
        positionBuffer?.Release();
        velocityBuffer?.Release();

    }
}
