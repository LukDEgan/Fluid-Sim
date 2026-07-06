
using System;
using System.Text.RegularExpressions;
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
    public float particleRadius;
    [Range(0, 1)]
    public float collisionDamping = 0.95f;
    public float smoothingRadius;
    public Vector2 boundsSize;

    [Header("Interaction Settings")]
    public float interactionRadius = 2f;
    public float interactionStrength = 50f;

    [Header("Dependencies")]
    public ComputeShader simCompute;
    bool isPaused;
    bool stepOneFrame;
    int threadGroupSize = 256;
    Vector2[] positions;
    Vector2[] velocities;
    Entry[] spatialLookup;
    int[] startIndices;
    Vector2[] predictedPositions;
    Vector2[] densities; // density and near density
    int externalForcesKernel;
    int calculateDensitiesKernel;
    int calculatePressureForceKernel;
    int CalculateViscosityForceKernel;
    int updatePositionKernelKernel;




    (int x, int y)[] cellOffSets =
  {
    (-1, -1), (0, -1), (1, -1),
    (-1,  0), (0,  0), (1,  0),
    (-1,  1), (0,  1), (1,  1)
};


    // Buffers
    public ComputeBuffer positionBuffer { get; private set; }
    public ComputeBuffer velocityBuffer { get; private set; }
    public ComputeBuffer densitiesBuffer { get; private set; }
    ComputeBuffer predictedPositionBuffer;

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

        densities = new Vector2[numberOfParticles];
        positions = new Vector2[numberOfParticles];
        velocities = new Vector2[numberOfParticles];
        predictedPositions = new Vector2[numberOfParticles];
        spatialLookup = new Entry[numberOfParticles];
        startIndices = new int[numberOfParticles];

        int particlesPerRow = (int)Math.Sqrt(numberOfParticles);
        int particlesPerCol = (numberOfParticles - 1) / particlesPerRow + 1;
        float spacing = particleRadius * 2 + particleSpacing;

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
        densitiesBuffer = new ComputeBuffer(numberOfParticles, sizeof(float) * 2);
        predictedPositionBuffer = new ComputeBuffer(numberOfParticles, sizeof(float) * 2);
        velocityBuffer = new ComputeBuffer(numberOfParticles, sizeof(float) * 2);

        positionBuffer.SetData(positions);
        velocityBuffer.SetData(velocities);
        predictedPositionBuffer.SetData(predictedPositions);
        densitiesBuffer.SetData(densities);

        externalForcesKernel = simCompute.FindKernel("ExternalForces");
        calculateDensitiesKernel = simCompute.FindKernel("CalculateDensities");
        calculatePressureForceKernel = simCompute.FindKernel("CalculatePressureForce");
        CalculateViscosityForceKernel = simCompute.FindKernel("CalculateViscosityForce");
        updatePositionKernelKernel = simCompute.FindKernel("UpdatePositions");

        simCompute.SetBuffer(externalForcesKernel, "Positions", positionBuffer);
        simCompute.SetBuffer(externalForcesKernel, "Velocities", velocityBuffer);
        simCompute.SetBuffer(externalForcesKernel, "PredictedPositions", predictedPositionBuffer);

        simCompute.SetBuffer(calculateDensitiesKernel, "Densities", densitiesBuffer);
        simCompute.SetBuffer(calculateDensitiesKernel, "PredictedPositions", predictedPositionBuffer);

        simCompute.SetBuffer(calculatePressureForceKernel, "Densities", densitiesBuffer);
        simCompute.SetBuffer(calculatePressureForceKernel, "PredictedPositions", predictedPositionBuffer);
        simCompute.SetBuffer(calculatePressureForceKernel, "Velocities", velocityBuffer);

        simCompute.SetBuffer(CalculateViscosityForceKernel, "PredictedPositions", predictedPositionBuffer);
        simCompute.SetBuffer(CalculateViscosityForceKernel, "Velocities", velocityBuffer);

        simCompute.SetBuffer(updatePositionKernelKernel, "Positions", positionBuffer);
        simCompute.SetBuffer(updatePositionKernelKernel, "Velocities", velocityBuffer);

    }
    void SimulationStepGPU(float dt)
    {



        int groups = Mathf.CeilToInt(numberOfParticles / (float)threadGroupSize);
        simCompute.Dispatch(externalForcesKernel, groups, 1, 1);
        simCompute.Dispatch(calculateDensitiesKernel, groups, 1, 1);
        simCompute.Dispatch(calculatePressureForceKernel, groups, 1, 1);
        simCompute.Dispatch(CalculateViscosityForceKernel, groups, 1, 1);
        simCompute.Dispatch(updatePositionKernelKernel, groups, 1, 1);
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
                UpdateSettings(stepDt);
                SimulationStepGPU(stepDt);
            }

            stepOneFrame = false;
        }


    }
    void UpdateSettings(float dt)
    {
        simCompute.SetInt("numParticles", numberOfParticles);
        simCompute.SetFloat("deltaTime", dt);
        simCompute.SetFloat("gravity", gravity);
        simCompute.SetFloat("collisionDamping", collisionDamping);
        simCompute.SetFloat("smoothingRadius", smoothingRadius);
        simCompute.SetFloat("targetDensity", targetDensity);
        simCompute.SetFloat("pressureMultiplier", pressureMultiplier);
        simCompute.SetFloat("nearPressureMultiplier", nearPressureMultiplier);
        simCompute.SetFloat("viscosityStrength", viscosityStrength);
        simCompute.SetVector("boundsSize", boundsSize);

        Vector2 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        bool isPullInteraction = Input.GetMouseButton(0);
        bool isPushInteraction = Input.GetMouseButton(1);
        float currInteractStrength = 0;
        if (isPushInteraction || isPullInteraction)
        {
            currInteractStrength = isPushInteraction ? -interactionStrength : interactionStrength;
        }

        simCompute.SetVector("interactionPoint", mousePos);
        simCompute.SetFloat("interactionStrength", currInteractStrength);
        simCompute.SetFloat("interactionRadius", interactionRadius);
    }
    void HandleInput()
    {
        if (Input.GetKeyDown(KeyCode.Space))
            isPaused = !isPaused;

        if (Input.GetKeyDown(KeyCode.RightArrow))
            stepOneFrame = true;
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
        densitiesBuffer?.Release();
        predictedPositionBuffer?.Release();

    }
}
