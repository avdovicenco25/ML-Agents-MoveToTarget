using UnityEngine;

public class CameraManager : MonoBehaviour
{
    [SerializeField] private GameObject[] cameras;

    [SerializeField] private int cameraActive;

    private void Start()
    {
        cameras[0].SetActive(true);
        cameraActive = 0;

        for (int i = 1; i < cameras.Length; i++)  
        {
               cameras [i].SetActive(false);
        }
    }
    public void SwitchCamera()
    {
        cameras[cameraActive].SetActive(false);
        cameraActive = (cameraActive + 1) % cameras.Length;
        if (cameras[cameraActive] != null)
        {
            cameras[cameraActive].SetActive(true);
        }
    }

}
