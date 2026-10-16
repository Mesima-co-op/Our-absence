using UnityEngine;

/// <summary>A short flash: the light starts bright and fades out every time the effect is played.</summary>
[RequireComponent(typeof(Light))]
public class FXLightFade : MonoBehaviour
{
    public float duration = 0.12f;
    public AnimationCurve falloff = AnimationCurve.EaseInOut(0, 1, 1, 0);

    private Light flash;
    private float startIntensity;
    private float age;

    private void Awake()
    {
        flash = GetComponent<Light>();
        startIntensity = flash.intensity;
    }

    private void OnEnable()
    {
        age = 0f;
        if (flash != null) { flash.intensity = startIntensity; flash.enabled = true; }
    }

    private void Update()
    {
        // Unscaled: the flash should still read during hit stop
        age += Time.unscaledDeltaTime;
        float k = Mathf.Clamp01(age / Mathf.Max(duration, 0.001f));
        flash.intensity = startIntensity * falloff.Evaluate(k);
        if (k >= 1f) flash.enabled = false;
    }
}
