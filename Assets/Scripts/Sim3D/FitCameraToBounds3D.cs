using UnityEngine;

public class FitCameraToBounds3D : MonoBehaviour
{
    public Simulation3D sim;
    public float padding = 1.2f;
    public Vector3 viewDirection = new Vector3(0.35f, 0.35f, -1f);
    public float rotationSpeed = 60f;

    float yaw = 0f;
    float distance;
    Camera cam;

    void Awake()
    {
        cam = GetComponent<Camera>();
    }

    void LateUpdate()
    {
        if (sim == null) return;

        if (Input.GetKey(KeyCode.A))
            yaw -= rotationSpeed * Time.deltaTime;

        if (Input.GetKey(KeyCode.D))
            yaw += rotationSpeed * Time.deltaTime;

        float largest = Mathf.Max(sim.boundsSize.x, sim.boundsSize.y, sim.boundsSize.z);

        distance = (largest * padding) /
                   (2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad));

        Quaternion rotation = Quaternion.Euler(20f, yaw, 0f);
        Vector3 forward = rotation * Vector3.forward;

        transform.position = -forward * distance;
        transform.LookAt(Vector3.zero);
    }
}
