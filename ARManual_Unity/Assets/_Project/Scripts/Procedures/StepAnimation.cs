using UnityEngine;

/// <summary>
/// Base class for step animations (e.g. the lid sliding off).
/// Put a subclass on any object inside the Thermometer content and give it the
/// name used in the procedure file ("animation": "LidSlideOff").
/// The StepEngine finds it by that name and calls Play()/Stop().
/// </summary>
public abstract class StepAnimation : MonoBehaviour
{
    [Tooltip("Name used in the procedure file, e.g. LidSlideOff.")]
    public string animationName;

    public abstract void Play();
    public abstract void Stop();
}
