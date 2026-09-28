using UnityEngine;
using TMPro;

public class FPSCounter : MonoBehaviour
{
    public TMP_Text fpsText;
    private float pollingTime = 0.5f;
    private float time;
    private int frameCount;

    void Update()
    {
        time += Time.deltaTime;
        frameCount++;

        if (time >= pollingTime)
        {
            var fps = frameCount / time;
            fpsText.text = $"FPS: {Mathf.RoundToInt(fps)}";

            time = 0f;
            frameCount = 0;
        }
    }
}