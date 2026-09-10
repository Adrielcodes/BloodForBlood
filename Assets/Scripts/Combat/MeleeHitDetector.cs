using System.Collections.Generic;
using UnityEngine;

public static class MeleeHitDetector
{
    public static List<T> FindTargets<T>(Transform origin, float forwardOffset, float radius, float heightOffset = 1f)
        where T : Component
    {
        Vector3 center = origin.position + Vector3.up * heightOffset + origin.forward * forwardOffset;
        Collider[] hits = Physics.OverlapSphere(center, radius);

        var results = new List<T>();
        foreach (Collider hit in hits)
        {
            T target = hit.GetComponentInParent<T>();
            if (target != null && !results.Contains(target))
            {
                results.Add(target);
            }
        }

        return results;
    }
}
