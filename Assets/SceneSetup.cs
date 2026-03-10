using UnityEngine;

/// <summary>
/// Auto-configures the scene when it first runs:
///   - Positions the main camera to see all 3 cubes
///   - Adds a directional light if none exists
///   - Adds a ground plane
///   - Adds the MoveCubes component to itself
///
/// Attach this to any GameObject in a blank scene, or add it alongside MoveCubes.
/// You only need ONE of: SceneSetup OR manually placing the camera/light/ground.
/// </summary>
public class SceneSetup : MonoBehaviour
{
    void Awake()
    {
        SetupCamera();
        SetupLight();
        SetupGround();

        // Attach MoveCubes to this GameObject if not already present
        if (GetComponent<MoveCubes>() == null)
            gameObject.AddComponent<MoveCubes>();
    }

    private void SetupCamera()
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            GameObject camGO = new GameObject("Main Camera");
            camGO.tag = "MainCamera";
            cam = camGO.AddComponent<Camera>();
            camGO.AddComponent<AudioListener>();
        }

        // Position so all 3 cubes (at x = -3, 0, 3) are visible
        cam.transform.position = new Vector3(0f, 4f, -10f);
        cam.transform.LookAt(Vector3.zero);
        cam.backgroundColor = new Color(0.08f, 0.08f, 0.14f);
        cam.clearFlags = CameraClearFlags.SolidColor;
    }

    private void SetupLight()
    {
        if (FindFirstObjectByType<Light>() != null) return;

        GameObject lightGO = new GameObject("Directional Light");
        Light light = lightGO.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.2f;
        light.color = new Color(1f, 0.97f, 0.9f);
        lightGO.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
    }

    private void SetupGround()
    {
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.position = new Vector3(0f, -0.5f, 0f);
        ground.transform.localScale = new Vector3(3f, 1f, 2f);

        MeshRenderer rend = ground.GetComponent<MeshRenderer>();
        Material mat = new Material(Shader.Find("Standard"));
        mat.color = new Color(0.12f, 0.13f, 0.18f);
        rend.material = mat;
    }
}