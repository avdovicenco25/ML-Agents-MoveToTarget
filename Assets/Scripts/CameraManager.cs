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
        if (cameras[cameraActive ++] != null)
        {
            Debug.Log(cameraActive + "before ++");
            cameraActive++;
            Debug.Log(cameraActive + "after ++");
            cameras[cameraActive].SetActive(true);
        }
        else
        {
            cameraActive = 0;
            cameras[0].SetActive(true);
        }
    }

}
