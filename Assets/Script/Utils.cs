using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Utils
{
    public static bool TryGetKernel(string kernelName, ref ComputeShader cs, ref int kernelID)
    {
        if (!cs.HasKernel(kernelName))
        {
            Debug.LogError(kernelName + " kernel not found in " + cs.name + "!");
            return false;
        }

        kernelID = cs.FindKernel(kernelName);
        return true;
    }
}
