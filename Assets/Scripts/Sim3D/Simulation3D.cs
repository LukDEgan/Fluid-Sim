
using System;
using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Serialization;

public class Simulation3D : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    [Header("Settings")]
    public float timeScale = 1f;
    public int maxTimestepFPS = 60;
    public int substeps = 4;
    int numberOfParticles;
    public float pressureMultiplier;
    public float nearPressureMultiplier;
    public float viscosityStrength = 0.5f;
    public float gravity;
    public float targetDensity;

    [Range(0, 1)]
    public float collisionDamping = 0.95f;
    public float smoothingRadius;
    public Vector3 boundsSize;
    public Vector3 obstacleSize;
    public Vector3 obstacleCentre;

    [Header("Interaction Settings")]
    public float interactionRadius = 2f;
    public float interactionStrength = 50f;
    public float interactionDepth = 0f;

    [Header("Dependencies")]
    public ComputeShader simCompute;
    public Spawner3D spawner;
    Spawner3D.ParticleSpawnData spawnData;
    bool isPaused;
    bool stepOneFrame;
    const int threadGroupSize = 256;
    Vector2[] densities; // density and near density
    int externalForcesKernel;
    int calculateDensitiesKernel;
    int calculatePressureForceKernel;
    int CalculateViscosityForceKernel;
    int updatePositionKernel;





    // Buffers
    public ComputeBuffer positionBuffer { get; private set; }
    public ComputeBuffer velocityBuffer { get; private set; }
    public ComputeBuffer densitiesBuffer { get; private set; }
    ComputeBuffer predictedPositionBuffer;

    void Start()
    {


        Init();
    }
    void Init()
    {
        if (simCompute == null)
        {
            Debug.LogError("Simulation3D: simCompute is not assigned.");
            enabled = false;
            return;
        }

        if (spawner == null)
        {
            Debug.LogError("Simulation3D: spawner is not assigned.");
            enabled = false;
            return;
        }


        float deltaTime = 1 / 60f;
        Time.fixedDeltaTime = deltaTime;

        spawnData = spawner.GetSpawnData();
        numberOfParticles = spawnData.positions.Length;


        if (numberOfParticles == 0)
        {
            Debug.LogError("Simulation3D: spawner returned 0 particles. Check spawn density and spawn region size.");
            enabled = false;
            return;
        }

        densities = new Vector2[numberOfParticles];

        positionBuffer = new ComputeBuffer(numberOfParticles, sizeof(float) * 3);
        densitiesBuffer = new ComputeBuffer(numberOfParticles, sizeof(float) * 2);
        predictedPositionBuffer = new ComputeBuffer(numberOfParticles, sizeof(float) * 3);
        velocityBuffer = new ComputeBuffer(numberOfParticles, sizeof(float) * 3);



        externalForcesKernel = simCompute.FindKernel("ExternalForces");
        calculateDensitiesKernel = simCompute.FindKernel("CalculateDensities");
        calculatePressureForceKernel = simCompute.FindKernel("CalculatePressureForce");
        CalculateViscosityForceKernel = simCompute.FindKernel("CalculateViscosityForce");
        updatePositionKernel = simCompute.FindKernel("UpdatePositions");

        SetInitialBufferData(spawnData);

        SetKernelBuffers();

        simCompute.SetInt("numParticles", numberOfParticles);

    }
    void SetKernelBuffers()
    {
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

        simCompute.SetBuffer(updatePositionKernel, "Positions", positionBuffer);
        simCompute.SetBuffer(updatePositionKernel, "Velocities", velocityBuffer);
    }

    void SetInitialBufferData(Spawner3D.ParticleSpawnData spawnData)
    {
        float3[] allPoints = new float3[spawnData.positions.Length]; //
        System.Array.Copy(spawnData.positions, allPoints, spawnData.positions.Length);

        positionBuffer.SetData(allPoints);
        predictedPositionBuffer.SetData(allPoints);
        velocityBuffer.SetData(spawnData.velocities);
        densitiesBuffer.SetData(densities);
    }
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

    void SimulationStepGPU(float dt)
    {

        int groups = Mathf.CeilToInt(numberOfParticles / (float)threadGroupSize);
        simCompute.Dispatch(externalForcesKernel, groups, 1, 1);
        simCompute.Dispatch(calculateDensitiesKernel, groups, 1, 1);
        simCompute.Dispatch(calculatePressureForceKernel, groups, 1, 1);
        simCompute.Dispatch(CalculateViscosityForceKernel, groups, 1, 1);
        simCompute.Dispatch(updatePositionKernel, groups, 1, 1);
    }
    // Update is called once per frame
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

        simCompute.SetMatrix("localToWorld", transform.localToWorldMatrix);
        simCompute.SetMatrix("worldToLocal", transform.worldToLocalMatrix);

        Vector3 mousePos = GetMouseInteractionPoint();
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
    Vector3 GetMouseInteractionPoint()
    {
        Camera cam = Camera.main;

        if (cam == null)
            return Vector3.zero;

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);

        // Interact with the Z = interactionDepth plane.
        Plane interactionPlane = new Plane(Vector3.forward, new Vector3(0f, 0f, interactionDepth));

        if (interactionPlane.Raycast(ray, out float enter))
            return ray.GetPoint(enter);

        return Vector3.zero;
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
        Gizmos.color = new Color(0f, 1f, 0f, 0.4f);
        Gizmos.DrawWireCube(Vector3.zero, boundsSize);

        Gizmos.color = new Color(1f, 0.5f, 0f, 0.4f);
        Gizmos.DrawWireCube(obstacleCentre, obstacleSize);

        if (Application.isPlaying && Camera.main != null)
        {
            bool isPullInteraction = Input.GetMouseButton(0);
            bool isPushInteraction = Input.GetMouseButton(1);
            bool isInteracting = isPullInteraction || isPushInteraction;

            if (isInteracting)
            {
                Gizmos.color = isPullInteraction ? Color.green : Color.red;
                Gizmos.DrawWireSphere(GetMouseInteractionPoint(), interactionRadius);
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
