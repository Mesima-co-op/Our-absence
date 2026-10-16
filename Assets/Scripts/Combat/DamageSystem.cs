using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DamageSystem : MonoBehaviour
{
    public EnemyAI script;
    // Start is called before the first frame update
    void Start()
    {
        script = GetComponentInParent<EnemyAI>();
    }
}
