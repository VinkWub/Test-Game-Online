using UnityEngine;

public class UILookAtCam : MonoBehaviour
{
    void Update()
    {
        transform.LookAt(transform.position + Camera.main.transform.forward);
    }
}
