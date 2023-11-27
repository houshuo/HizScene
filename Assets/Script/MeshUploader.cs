using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MeshUploader : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        foreach (var meshGo in GetComponentsInChildren<MeshFilter>(true))
        {
            MeshManager.JobQueue.Enqueue(meshGo.gameObject);
        }
    }
}
