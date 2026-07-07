
using System;
using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Serialization;

public class Simulation : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    [Header("Settings")]
    public float timeScale = 1f;
    public int maxTimestepFPS = 60;
    public int substeps = 4;

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
    public Vector2 obstacleSize;
    public Vector2 obstacleCentre;

    [Header("Interaction Settings")]
    public float interactionRadius = 2f;
    public float interactionStrength = 50f;

    [Header("Dependencies")]
    public ComputeShader simCompute;
    public Spawner spawner;
    Spawner.ParticleSpawnData spawnData;
    bool isPaused;
    bool stepOneFrame;
    int threadGroupSize = 256;
    Vector2[] positions;
    Vector2[] velocities;
    int[] startIndices;
    Vector2[] predictedPositions;
    Vector2[] densities; // density and near density
    int externalForcesKernel;
    int calculateDensitiesKernel;
    int calculatePressureForceKernel;
    int CalculateViscosityForceKernel;
    int updatePositionKernelKernel;

    int numberOfParticles;


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
    void SetInitialBufferData(Spawner.ParticleSpawnData spawnData)
    {
        float2[] allPoints = new float2[spawnData.positions.Length]; //
        System.Array.Copy(spawnData.positions, allPoints, spawnData.positions.Length);

        positionBuffer.SetData(allPoints);
        predictedPositionBuffer.SetData(allPoints);
        velocityBuffer.SetData(spawnData.velocities);
    }
    void Start()
    {


        Init();
    }
    void Init()
    {
        float deltaTime = 1 / 60f;
        Time.fixedDeltaTime = deltaTime;

        spawnData = spawner.GetSpawnData();
        numberOfParticles = spawnData.positions.Length;
        densities = new Vector2[numberOfParticles];
        positionBuffer = new ComputeBuffer(numberOfParticles, sizeof(float) * 2);
        densitiesBuffer = new ComputeBuffer(numberOfParticles, sizeof(float) * 2);
        predictedPositionBuffer = new ComputeBuffer(numberOfParticles, sizeof(float) * 2);
        velocityBuffer = new ComputeBuffer(numberOfParticles, sizeof(float) * 2);



        externalForcesKernel = simCompute.FindKernel("ExternalForces");
        calculateDensitiesKernel = simCompute.FindKernel("CalculateDensities");
        calculatePressureForceKernel = simCompute.FindKernel("CalculatePressureForce");
        CalculateViscosityForceKernel = simCompute.FindKernel("CalculateViscosityForce");
        updatePositionKernelKernel = simCompute.FindKernel("UpdatePositions");

        SetInitialBufferData(spawnData);
        densitiesBuffer.SetData(densities);

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

        simCompute.SetInt("numParticles", numberOfParticles);

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
        simCompute.SetFloat("deltaTime", dt);
        simCompute.SetFloat("gravity", gravity);
        simCompute.SetFloat("collisionDamping", collisionDamping);
        simCompute.SetFloat("smoothingRadius", smoothingRadius);
        simCompute.SetFloat("targetDensity", targetDensity);
        simCompute.SetFloat("pressureMultiplier", pressureMultiplier);
        simCompute.SetFloat("nearPressureMultiplier", nearPressureMultiplier);
        simCompute.SetFloat("viscosityStrength", viscosityStrength);
        simCompute.SetVector("boundsSize", boundsSize);
        simCompute.SetVector("obstacleSize", obstacleSize);
        simCompute.SetVector("obstacleCentre", obstacleCentre);

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
        Gizmos.color = new Color(0, 1, 0, 0.4f);
        Gizmos.DrawWireCube(Vector2.zero, boundsSize);
        Gizmos.DrawWireCube(obstacleCentre, obstacleSize);
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
