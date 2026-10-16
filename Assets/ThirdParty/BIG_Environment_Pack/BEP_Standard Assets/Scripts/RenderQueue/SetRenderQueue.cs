using UnityEngine;
using System.Collections;

[AddComponentMenu("Effects/SetRenderQueue")]
[RequireComponent(typeof(Renderer))]

public class SetRenderQueue : MonoBehaviour {
	public int queue = 1;
	
	public int[] queues;
	
	protected void Start() {
		if (!GetComponent<Renderer>() || !GetComponent<Renderer>().sharedMaterial || queues == null)
			return;
		GetComponent<Renderer>().sharedMaterial.renderQueue = queue;
		for (int i = 0; i < queues.Length && i < GetComponent<Renderer>().sharedMaterials.Length; i++)
			GetComponent<Renderer>().sharedMaterials[i].renderQueue = queues[i];
	}
	
}